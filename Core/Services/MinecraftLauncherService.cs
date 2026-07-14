using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MinecraftControlHub.Core.Models;

namespace MinecraftControlHub.Core.Services;

/// <summary>
/// Progress information reported while preparing/launching an installation.
/// </summary>
public class LaunchProgress
{
    public string Stage { get; set; } = string.Empty;
    /// <summary>0..100, or null for an indeterminate stage.</summary>
    public double? Percent { get; set; }
}

/// <summary>
/// Result of a launch attempt.
/// </summary>
public class LaunchResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int ProcessId { get; set; }
}

public interface IMinecraftLauncherService
{
    /// <summary>
    /// Downloads (if needed) all vanilla game files for the installation's Minecraft
    /// version into the shared cache, then launches the game using its own isolated
    /// game directory. When <paramref name="account"/> is a signed-in Microsoft
    /// account the game starts online (real access token) so premium servers work;
    /// otherwise it falls back to offline mode using <paramref name="offlineUsername"/>.
    /// </summary>
    Task<LaunchResult> LaunchAsync(Installation installation, string offlineUsername, MinecraftAccount? account = null, IProgress<LaunchProgress>? progress = null);
}

public class MinecraftLauncherService : IMinecraftLauncherService
{
    private const string VersionManifestUrl =
        "https://launchermeta.mojang.com/mc/game/version_manifest_v2.json";
    private const string ResourcesBaseUrl = "https://resources.download.minecraft.net";

    private readonly HttpClient _http;
    private readonly IJavaService _javaService;
    private readonly ILoaderService _loaderService;

    public MinecraftLauncherService(HttpClient http, IJavaService javaService, ILoaderService loaderService)
    {
        _http = http;
        _javaService = javaService;
        _loaderService = loaderService;
    }

    public async Task<LaunchResult> LaunchAsync(Installation installation, string offlineUsername, MinecraftAccount? account = null, IProgress<LaunchProgress>? progress = null)
    {
        try
        {
            var online = account is { IsSignedIn: true };
            var username = online
                ? account!.Username
                : (string.IsNullOrWhiteSpace(offlineUsername) ? "Player" : offlineUsername.Trim());
            var versionId = installation.MinecraftVersion?.Trim() ?? string.Empty;
            var launchVersionId = versionId;

            if (string.IsNullOrWhiteSpace(versionId))
                return Fail("This installation has no Minecraft version set.");

            // Every loader inherits the vanilla files, so the vanilla version is
            // always downloaded first; loader-specific extras (Fabric/Forge/…) are
            // layered on afterwards via the loader service.
            progress?.Report(new LaunchProgress { Stage = "Resolving Minecraft version…" });
            var versionUrl = await ResolveVersionUrlAsync(versionId);
            if (versionUrl == null)
                return Fail($"Minecraft version \"{versionId}\" was not found in Mojang's version manifest.");

            var versionDir = Path.Combine(AppPaths.VersionsDir, versionId);
            Directory.CreateDirectory(versionDir);
            var versionJsonPath = Path.Combine(versionDir, versionId + ".json");

            string versionJson;
            if (File.Exists(versionJsonPath))
            {
                versionJson = await File.ReadAllTextAsync(versionJsonPath);
            }
            else
            {
                progress?.Report(new LaunchProgress { Stage = "Downloading version metadata…" });
                versionJson = await _http.GetStringAsync(versionUrl);
                await File.WriteAllTextAsync(versionJsonPath, versionJson);
            }

            using var doc = JsonDocument.Parse(versionJson);
            var root = doc.RootElement;

            var mainClass = root.GetProperty("mainClass").GetString() ?? "net.minecraft.client.main.Main";
            var assetIndexId = root.GetProperty("assetIndex").GetProperty("id").GetString() ?? versionId;

            // ---- Client jar ----
            progress?.Report(new LaunchProgress { Stage = "Downloading game client…" });
            var clientJarPath = Path.Combine(versionDir, versionId + ".jar");
            if (!File.Exists(clientJarPath) && root.TryGetProperty("downloads", out var dls) &&
                dls.TryGetProperty("client", out var client) && client.TryGetProperty("url", out var clientUrl))
            {
                await DownloadFileAsync(clientUrl.GetString()!, clientJarPath);
            }

            // ---- Libraries + natives ----
            var nativesDir = Path.Combine(versionDir, "natives");
            Directory.CreateDirectory(nativesDir);
            var classpath = new List<string> { clientJarPath };

            var libs = root.TryGetProperty("libraries", out var libsEl) && libsEl.ValueKind == JsonValueKind.Array
                ? libsEl.EnumerateArray().ToList()
                : new List<JsonElement>();

            var total = libs.Count;
            var done = 0;
            foreach (var lib in libs)
            {
                done++;
                progress?.Report(new LaunchProgress
                {
                    Stage = "Downloading libraries…",
                    Percent = total > 0 ? done * 100.0 / total : null
                });

                if (!IsLibraryAllowed(lib))
                    continue;

                if (lib.TryGetProperty("downloads", out var libDownloads))
                {
                    // Main artifact.
                    if (libDownloads.TryGetProperty("artifact", out var artifact) &&
                        artifact.TryGetProperty("path", out var pathEl) &&
                        artifact.TryGetProperty("url", out var urlEl))
                    {
                        var libPath = Path.Combine(AppPaths.LibrariesDir, pathEl.GetString()!.Replace('/', Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(Path.GetDirectoryName(libPath)!);
                        if (!File.Exists(libPath))
                            await DownloadFileAsync(urlEl.GetString()!, libPath);

                        // Modern natives are shipped as normal artifacts whose name
                        // ends in "natives-windows"; those must be extracted, not
                        // added to the classpath.
                        if (IsNativeArtifact(lib, pathEl.GetString()!))
                            ExtractNatives(libPath, nativesDir);
                        else
                            classpath.Add(libPath);
                    }

                    // Legacy classifier-based natives.
                    var classifier = GetNativeClassifier(lib);
                    if (classifier != null && libDownloads.TryGetProperty("classifiers", out var classifiers) &&
                        classifiers.TryGetProperty(classifier, out var nativeArt) &&
                        nativeArt.TryGetProperty("url", out var nativeUrl) &&
                        nativeArt.TryGetProperty("path", out var nativePath))
                    {
                        var nPath = Path.Combine(AppPaths.LibrariesDir, nativePath.GetString()!.Replace('/', Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(Path.GetDirectoryName(nPath)!);
                        if (!File.Exists(nPath))
                            await DownloadFileAsync(nativeUrl.GetString()!, nPath);
                        ExtractNatives(nPath, nativesDir);
                    }
                }
            }

            // ---- Assets ----
            progress?.Report(new LaunchProgress { Stage = "Downloading assets…" });
            await DownloadAssetsAsync(root, assetIndexId, progress);

            // ---- Java + loader (Fabric/Forge/…) ----
            var java = await ResolveJavaAsync(installation);
            if (java == null)
                return Fail("No Java runtime was found. Install Java and set it in the installation settings.");

            LoaderLaunchInfo? loader = null;
            if (installation.Loader != LoaderType.Vanilla)
            {
                loader = await _loaderService.PrepareAsync(installation, java, progress);
                if (loader.Error != null)
                    return Fail(loader.Error);
                if (!string.IsNullOrWhiteSpace(loader.VersionId))
                    launchVersionId = loader.VersionId!;
                // BUGFIX: this used to only add the loader's libraries to the classpath
                // for Fabric/Quilt (!UseLoaderLaunch), skipping it for Forge/NeoForge.
                // But Forge/NeoForge's own JVM args reference "${classpath}" (they set
                // -DlegacyClassPath=${classpath} for their module-path bootstrap launcher
                // to load everything not on the boot module path) and expect it to contain
                // ALL jars — vanilla AND Forge/NeoForge's own patched/library jars. Leaving
                // Forge's jars out meant its own classes were missing at runtime, which is
                // exactly what "Forge crashes on startup" looks like (ClassNotFoundException
                // right after launch). Always include them now.
                classpath.AddRange(loader.ExtraClasspath);
                if (!string.IsNullOrWhiteSpace(loader.MainClass))
                    mainClass = loader.MainClass;
            }

            // ---- Build launch arguments ----
            progress?.Report(new LaunchProgress { Stage = "Starting game…" });

            var gameDir = installation.GameDirectory ?? AppPaths.InstanceDir(installation.Id);
            Directory.CreateDirectory(gameDir);

            // BUGFIX (real crash: "ResolutionException: Modules minecraft and _1._20 export
            // package com.mojang.blaze3d.systems to module neoforge"). Modern Forge/NeoForge
            // (1.17+, UseLoaderLaunch) locate/produce their OWN patched client module (the
            // "production client provider" seen in the log as e.g.
            // "client-1.20.6-...-srg.jar", registered as the module named "minecraft") from
            // the plain vanilla client jar directly — they don't need it handed to them again
            // via -DlegacyClassPath. Java's automatic-module namer turns a jar literally named
            // "<version>.jar" (e.g. "1.20.6.jar", exactly what clientJarPath is) into a module
            // name like "_1._20._6" purely from its filename. With BOTH that auto-module and
            // Forge/NeoForge's own "minecraft" module on the module path, the JVM's resolver
            // sees the identical Minecraft packages exported twice and refuses to boot — an
            // immediate, unrecoverable ResolutionException, not a "mod conflict". Vanilla and
            // Fabric/Quilt (classic -cp launch, no module layer involved) still need the
            // client jar on the classpath as before; only the module-path (loader) launch must
            // drop it.
            // BUGFIX (real crash: "NoClassDefFoundError: com/mojang/util/UndashedUuid",
            // thrown from vanilla's own net.minecraft.core.UUIDUtil while NeoForge was
            // loading). UndashedUuid only exists in newer builds of com.mojang:authlib
            // (added for 1.20.5/1.20.6's telemetry code) - and vanilla's own version.json
            // and NeoForge's loader profile can each declare a DIFFERENT version of the
            // very same library (authlib being exactly one of them). The previous de-dup
            // only dropped EXACT duplicate paths (same artifact, same version, same file),
            // so both an older vanilla-declared authlib and NeoForge's newer required one
            // ended up on the module path together - a split package across two automatic
            // modules, silently resolved by picking whichever jar's entry the classloader
            // happens to hit first. That happened to be the OLDER one here, which doesn't
            // contain UndashedUuid at all: NoClassDefFoundError. De-dup by Maven artifact
            // coordinate (group+artifactId, ignoring the version folder/filename) instead,
            // keeping the LAST occurrence of each - libraries are appended vanilla-first
            // then loader-required, so "last" is always the loader's own required version
            // whenever the two disagree.
            var dedupedClasspath = DeduplicateLibrariesByArtifact(classpath);

            var legacyClasspathEntries = loader?.UseLoaderLaunch == true
                ? dedupedClasspath.Where(p => !string.Equals(p, clientJarPath, StringComparison.OrdinalIgnoreCase))
                : dedupedClasspath;

            var placeholders = new Dictionary<string, string>
            {
                ["auth_player_name"] = username,
                ["version_name"] = launchVersionId,
                ["game_directory"] = gameDir,
                ["assets_root"] = AppPaths.AssetsDir,
                ["game_assets"] = AppPaths.AssetsDir,
                ["assets_index_name"] = assetIndexId,
                ["auth_uuid"] = online ? account!.Uuid : OfflineUuid(username),
                ["auth_access_token"] = online ? account!.AccessToken : "0",
                ["auth_session"] = online ? $"token:{account!.AccessToken}:{account.Uuid}" : "0",
                ["clientid"] = string.Empty,
                ["auth_xuid"] = online ? account!.Xuid : string.Empty,
                ["user_type"] = online ? "msa" : "legacy",
                ["version_type"] = root.TryGetProperty("type", out var t) ? t.GetString() ?? "release" : "release",
                ["user_properties"] = "{}",
                ["natives_directory"] = nativesDir,
                ["launcher_name"] = "MinecraftControlHub",
                ["launcher_version"] = "1.0",
                // BUGFIX: found while testing the -DlegacyClassPath fix below against a
                // real NeoForge install — NeoForge's (and Forge's) own "libraries" list
                // re-declares several common dependencies vanilla's own libraries.json
                // already lists at the exact same version (gson, guava, log4j-api/-core,
                // slf4j-api, commons-lang3, commons-io, jopt-simple, ...). Both resolve to
                // the identical local file path, so appending loader.ExtraClasspath after
                // the vanilla loop put the SAME jar on the classpath twice. Modern Forge/
                // NeoForge's BootstrapLauncher builds a java.nio "UnionFileSystem" keyed by
                // path and throws (Collectors "duplicate key" IllegalStateException, right
                // after boot-layer setup) the moment the same path shows up twice — another
                // immediate startup crash, just one step further than the legacyClassPath
                // issue. De-duplicate (case-insensitive: Windows paths) before joining.
                ["classpath"] = string.Join(Path.PathSeparator, legacyClasspathEntries.Distinct(StringComparer.OrdinalIgnoreCase)),
                // Extra placeholders used by loader (Forge/NeoForge) argument templates.
                ["library_directory"] = AppPaths.LibrariesDir,
                ["classpath_separator"] = Path.PathSeparator.ToString()
            };

            var jvmArgs = new List<string>();

            // Memory settings.
            if (installation.MaxMemoryMB is > 0)
                jvmArgs.Add($"-Xmx{installation.MaxMemoryMB}M");
            if (installation.MinMemoryMB is > 0)
                jvmArgs.Add($"-Xms{installation.MinMemoryMB}M");

            // User-supplied extra JVM arguments (Settings → installation → Advanced).
            if (!string.IsNullOrWhiteSpace(installation.CustomJvmArgs))
            {
                foreach (var arg in installation.CustomJvmArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    jvmArgs.Add(arg);
            }

            var gameArgs = new List<string>();

            if (loader?.UseLoaderLaunch == true)
            {
                // BUGFIX (this was the real cause of "Forge crashes on startup" /
                // NeoForge's ResolutionException about "package net.minecraft.server"
                // being exported by two modules): a previous pass here merged vanilla's
                // OWN "arguments.jvm" (from the plain 1.20.1 version json) on top of
                // Forge/NeoForge's own JVM args. Vanilla's jvm args include a literal
                // "-cp ${classpath}" — a REAL "-cp" JVM flag. Modern Forge/NeoForge
                // (ModLauncher + SecureJarHandler, 1.17+) already build their own
                // complete, self-contained JVM arg list that puts their bootstrap jars
                // on the actual Java module path (-p / --add-modules) and reads the
                // full classpath itself via the -DlegacyClassPath=${classpath} SYSTEM
                // PROPERTY, not a real -cp flag. Adding a real "-cp" with the same jars
                // on top made the JVM's module resolver see the Minecraft classes twice
                // — once as an automatic module derived from the client jar's filename,
                // once as the "minecraft" module Forge's own loader builds — both
                // exporting the same packages, which is an unresolvable module conflict
                // and crashes the JVM immediately. This is also explicitly documented on
                // LoaderLaunchInfo.UseLoaderLaunch: "vanilla args must not be mixed in".
                // Use ONLY the loader's own (already complete) JVM args.
                foreach (var a in loader.ExtraJvmArgs)
                    jvmArgs.Add(Substitute(a, placeholders));

                // BUGFIX (real crash: "NoSuchElementException: No value present" at
                // BootstrapLauncher.run -> ServiceLoader...findFirst().orElseThrow()).
                // Verified by actually running the official NeoForge installer and
                // decompiling cpw.mods.bootstraplauncher: modern Forge/NeoForge version
                // JSONs (1.17+) deliberately do NOT include a "-DlegacyClassPath=..."
                // JVM arg themselves — they expect the LAUNCHER to compute and supply it.
                // BootstrapLauncher.loadLegacyClassPath() falls back to the JVM's own
                // "java.class.path" property when that's missing, which is empty/"."
                // when we launch without "-cp". With no legacy classpath, BootstrapLauncher
                // can't turn cpw.mods:modlauncher (and every other loader/game jar) into
                // modules, so no module provides the Consumer service it looks up via
                // ServiceLoader — and .orElseThrow() blows up immediately, before the game
                // even gets a chance to start. Real launchers (official Minecraft
                // Launcher, PrismLauncher) always inject this themselves; do the same,
                // using the exact classpath we already built (vanilla libraries +
                // loader.ExtraClasspath). Skip it if the loader's own args already set it
                // (older/legacy Forge jsons sometimes do) to avoid a conflicting duplicate.
                if (!jvmArgs.Any(a => a.StartsWith("-DlegacyClassPath=")))
                    jvmArgs.Add("-DlegacyClassPath=" + placeholders["classpath"]);

                // Safety net: only add natives path ourselves if the loader's own args
                // (checked above) didn't already set it — Forge/NeoForge usually do.
                if (!jvmArgs.Any(a => a.StartsWith("-Djava.library.path")))
                    jvmArgs.Insert(0, "-Djava.library.path=" + nativesDir);

                // BUGFIX (real crash: "Error: Could not find or load main class
                // net.neoforged.fml.startup.Client" / ClassNotFoundException, JVM exits
                // immediately with code 1). Newer NeoForge builds dropped the old
                // cpw.mods.bootstraplauncher module-path bootstrap entirely: their own
                // main class (net.neoforged.fml.startup.Client) has to be found by the
                // JVM itself at startup, the exact same way as any ordinary "java -cp ...
                // Main" invocation — which means it must be reachable from a REAL
                // "-cp"/"--class-path" (or, for the older module-path style Forge/NeoForge
                // still use, "-p"/"--module-path") JVM flag. "-DlegacyClassPath" is only a
                // system PROPERTY; only the old BootstrapLauncher ever read it back out
                // manually to build its own module layer. Without -p or -cp, java has
                // nothing to load the main class from at all and exits immediately before
                // any Minecraft/NeoForge code even runs — exactly this crash. Trust the
                // loader's own jvm args completely whenever they already supply either
                // real flag (classic Forge/NeoForge always do, via "-p"); only add our
                // own "-cp" with the exact classpath already computed above when they
                // supply NEITHER.
                if (!jvmArgs.Any(a => a is "-p" or "--module-path" or "-cp" or "--class-path"))
                {
                    jvmArgs.Add("-cp");
                    jvmArgs.Add(placeholders["classpath"]);
                }

                // Game args ARE meant to be merged: Forge/NeoForge's own game-args list
                // is just a few extra FML-specific flags, not a replacement for the
                // standard --username/--uuid/--accessToken/... vanilla provides.
                if (root.TryGetProperty("arguments", out var vanillaArgs) &&
                    vanillaArgs.TryGetProperty("game", out var vanillaGame))
                    gameArgs.AddRange(ExtractArguments(vanillaGame, placeholders));
                else if (root.TryGetProperty("minecraftArguments", out var legacyGame))
                {
                    foreach (var token in (legacyGame.GetString() ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
                        gameArgs.Add(Substitute(token, placeholders));
                }

                foreach (var a in loader.ExtraGameArgs)
                    gameArgs.Add(Substitute(a, placeholders));
            }
            else if (root.TryGetProperty("arguments", out var argsEl))
            {
                // Modern (1.13+) split arguments.
                if (argsEl.TryGetProperty("jvm", out var jvmEl))
                    jvmArgs.AddRange(ExtractArguments(jvmEl, placeholders));
                if (argsEl.TryGetProperty("game", out var gameEl))
                    gameArgs.AddRange(ExtractArguments(gameEl, placeholders));
            }
            else if (root.TryGetProperty("minecraftArguments", out var oldArgs))
            {
                // Legacy (pre-1.13): a single game-argument string, JVM args implied.
                jvmArgs.Add("-Djava.library.path=" + nativesDir);
                jvmArgs.Add("-cp");
                jvmArgs.Add(placeholders["classpath"]);
                foreach (var token in (oldArgs.GetString() ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    gameArgs.Add(Substitute(token, placeholders));
            }

            if (loader?.UseLoaderLaunch != true)
            {
                // Ensure the classpath/natives are present even if the modern jvm
                // template somehow omitted them.
                if (!jvmArgs.Any(a => a == "-cp" || a.StartsWith("-Djava.library.path")))
                {
                    jvmArgs.Add("-Djava.library.path=" + nativesDir);
                    jvmArgs.Add("-cp");
                    jvmArgs.Add(placeholders["classpath"]);
                }

                // Fabric/Quilt extras still ride on the vanilla classpath launch path.
                if (loader != null)
                {
                    foreach (var a in loader.ExtraJvmArgs) jvmArgs.Add(Substitute(a, placeholders));
                    foreach (var a in loader.ExtraGameArgs) gameArgs.Add(Substitute(a, placeholders));
                }
            }

            var psi = new ProcessStartInfo
            {
                FileName = java,
                WorkingDirectory = gameDir,
                UseShellExecute = false,
                // BUGFIX: without these two, java.exe (a console app) pops its own
                // visible console window when spawned from a GUI app (this is exactly
                // the "NeoForge opens a cmd window" symptom) and its output goes
                // nowhere we can see. Redirect + hide it and capture everything into
                // a log file instead, so a crash is actually diagnosable.
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var a in jvmArgs) psi.ArgumentList.Add(a);
            psi.ArgumentList.Add(mainClass);
            foreach (var a in gameArgs) psi.ArgumentList.Add(a);

            // BUGFIX: there was no persistent log at all before - a crash just showed a
            // generic "launched successfully" with nothing to debug from. Every launch
            // now writes a full log (JVM args + game's own stdout/stderr) to
            // %LocalAppData%\MinecraftControlHub\logs\<installation>-latest.log, and if
            // the process dies within the first few seconds we treat that as a failed
            // launch and surface the tail of the log as the error instead of pretending
            // it succeeded.
            var logPath = AppPaths.GameLogFile(installation.Id, installation.Name);
            var logBuilder = new StringBuilder();
            logBuilder.AppendLine($"=== MinecraftControlHub launch log — {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
            logBuilder.AppendLine($"Java: {java}");
            logBuilder.AppendLine($"Main class: {mainClass}");
            logBuilder.AppendLine($"JVM args: {string.Join(" ", jvmArgs)}");
            logBuilder.AppendLine($"Game args: {string.Join(" ", gameArgs)}");
            logBuilder.AppendLine("--- process output ---");

            // BUGFIX (real symptom: NeoForge/Forge hangs on a black screen with just menu
            // music playing — a mod-loading stall, not a crash — and the "log" the app
            // wrote was USELESS for figuring out why). This used to be a `using var
            // process`, written to disk exactly ONCE, 6 seconds after launch, then
            // disposed. Two compounding problems: (1) `Process.Dispose()` closes the
            // redirected stdout/stderr pipes it owns, which stops `OutputDataReceived`/
            // `ErrorDataReceived` from ever firing again — so for a game that's still
            // running (the success path, exactly what a "hang" looks like from here),
            // every single line of output produced after that dispose was silently
            // dropped, forever, not just "not yet written". (2) even what little arrived
            // in the first 6 seconds was only flushed to disk once and never updated
            // again, so re-reading the log file 30 seconds into a stuck black screen
            // showed the exact same stale few lines. Loading mods/textures/shaders
            // routinely takes well past 6 seconds, so the actual FML/mod errors behind a
            // hang were essentially guaranteed to land after the cutoff. Now: the process
            // is NOT disposed while still running — it keeps streaming output into the
            // log for its whole lifetime, and a background task flushes the log file to
            // disk every couple of seconds AND once more on real exit. This turns
            // "<installation>-latest.log" into a genuinely live log that reflects what's
            // actually happening even while the window sits on a black screen.
            var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => { if (e.Data != null) lock (logBuilder) logBuilder.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (logBuilder) logBuilder.AppendLine(e.Data); };

            async Task FlushLogAsync()
            {
                try
                {
                    string snapshot;
                    lock (logBuilder) snapshot = logBuilder.ToString();
                    Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                    await File.WriteAllTextAsync(logPath, snapshot);
                }
                catch { /* best effort logging - never let logging itself break the launch */ }
            }

            if (!process.Start())
            {
                process.Dispose();
                return Fail("Failed to start the Java process.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            var processId = process.Id;

            // Give it a few seconds to either keep running (normal — the game window
            // is opening) or die immediately (a real crash, e.g. ClassNotFoundException,
            // missing native, bad main class). Either way, flush what we have so far.
            var crashed = false;
            int exitCode = 0;
            try
            {
                await Task.WhenAny(process.WaitForExitAsync(), Task.Delay(TimeSpan.FromSeconds(6)));
                if (process.HasExited)
                {
                    crashed = true;
                    exitCode = process.ExitCode;
                }
            }
            catch { /* process is still running - that's the good case */ }

            await FlushLogAsync();

            if (crashed)
            {
                var tail = TailLines(logBuilder.ToString(), 25);
                process.Dispose();
                return Fail($"The game exited immediately (code {exitCode}) — it crashed on startup instead of launching. Last lines of the log (full log: {logPath}):\n{tail}");
            }

            // Still running: keep the log genuinely live for the rest of the process's
            // life (periodic flush every 2s), and write one final snapshot + dispose
            // once it actually exits — all in the background, without blocking launch.
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!process.HasExited)
                    {
                        await Task.WhenAny(process.WaitForExitAsync(), Task.Delay(TimeSpan.FromSeconds(2)));
                        await FlushLogAsync();
                    }
                }
                catch { /* best effort - the process may already be gone */ }
                finally
                {
                    await FlushLogAsync();
                    process.Dispose();
                }
            });

            return new LaunchResult { Success = true, ProcessId = processId };
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    // ---------- Helpers ----------

    private static LaunchResult Fail(string message) => new() { Success = false, Error = message };

    /// <summary>
    /// De-duplicates a classpath list by Maven artifact coordinate (group + artifactId),
    /// not just by exact path. Every library path here follows the standard Maven repo
    /// layout under AppPaths.LibrariesDir: ".../&lt;group&gt;/&lt;artifactId&gt;/&lt;version&gt;/&lt;file&gt;.jar".
    /// When the SAME artifact appears at two different versions (vanilla's own
    /// version.json vs. a loader's profile declaring a different one), keeps whichever
    /// has the HIGHER version — comparing purely by append order ("loader always wins")
    /// was wrong: either side can be the one that's behind. Ties (equal or unparseable
    /// versions) keep the later occurrence, since callers append vanilla libraries first
    /// and loader-required ones after. Paths outside LibrariesDir (e.g. the
    /// version-specific client jar) aren't Maven artifacts and are always kept as-is.
    /// </summary>
    private static List<string> DeduplicateLibrariesByArtifact(IEnumerable<string> paths)
    {
        var order = new List<string>();
        var indexByKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths)
        {
            var key = GetArtifactKey(path) ?? path;
            if (indexByKey.TryGetValue(key, out var idx))
            {
                var existing = order[idx];
                var cmp = CompareArtifactVersions(GetVersionSegment(existing), GetVersionSegment(path));
                // Keep the new one unless the existing one is strictly newer.
                if (cmp <= 0)
                    order[idx] = path;
            }
            else
            {
                indexByKey[key] = order.Count;
                order.Add(path);
            }
        }

        return order;
    }

    /// <summary>Returns "group/artifactId" PLUS a classifier fingerprint for a library
    /// path under AppPaths.LibrariesDir, or null if it isn't one.</summary>
    /// <remarks>
    /// BUGFIX (real crash: "Failed to find system mod: forge" from FML's ModSorter right
    /// at startup). This used to key purely on "group/artifactId", dropping BOTH the
    /// version folder AND the filename entirely. That's correct for plain single-jar
    /// dependencies (gson, guava, ...) where an older/newer version is a straight
    /// swap-in-place. But modern Forge/NeoForge (1.20.4+, SecureJarHandler launch)
    /// publish SEVERAL differently-classified jars for the SAME artifact+version under
    /// "net.minecraftforge:forge" — notably a "-client" classified jar that contains the
    /// real FML code and the mods.toml declaring the "forge" system mod, alongside other
    /// classified/unclassified jars for the same coordinate. All of those collapsed to
    /// the identical "net/minecraftforge/forge" key here, so whichever one happened to
    /// be LAST in the loader's own libraries list silently overwrote the others in the
    /// dedup map — if that wasn't the "-client" jar, the real Forge jar never made it
    /// onto the classpath, and FML couldn't find its own "forge" system mod. The key now
    /// also folds in the file's classifier/shape (its filename with the version string
    /// stripped out), so "forge-&lt;ver&gt;-client.jar" and "forge-&lt;ver&gt;.jar" are treated as
    /// different files and BOTH survive, while genuine same-shape version duplicates
    /// (e.g. "gson-2.10.1.jar" vs "gson-2.13.2.jar", both just "gson-{v}.jar") still
    /// correctly dedupe to the newest one.
    /// </remarks>
    private static string? GetArtifactKey(string path)
    {
        var rel = Path.GetRelativePath(AppPaths.LibrariesDir, path);
        if (rel.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(rel))
            return null;

        var segments = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        // <group segments...>/<artifactId>/<version>/<file>.jar — need at least 3 segments
        // to have a version folder + filename to drop.
        if (segments.Length < 3)
            return null;

        var groupAndArtifact = string.Join('/', segments[..^2]);
        var version = segments[^2];
        var fileName = segments[^1];

        // Strip the version string out of the filename so different classifiers of the
        // same artifact ("-client", "-universal", none, ...) get distinct keys, while a
        // plain version bump of the same classifier still normalizes to the same key.
        var shape = version.Length > 0 ? fileName.Replace(version, "{v}") : fileName;

        return $"{groupAndArtifact}::{shape}";
    }

    /// <summary>Returns the version folder (second-to-last path segment) for a library
    /// path under AppPaths.LibrariesDir, or "" if it can't be determined.</summary>
    private static string GetVersionSegment(string path)
    {
        var rel = Path.GetRelativePath(AppPaths.LibrariesDir, path);
        if (rel.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(rel))
            return string.Empty;

        var segments = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Length >= 3 ? segments[^2] : string.Empty;
    }

    /// <summary>Compares two Maven-style version strings (e.g. "6.0.54"). Returns &gt;0 if
    /// <paramref name="a"/> is newer, &lt;0 if <paramref name="b"/> is newer, 0 if equal or
    /// neither side can be confidently parsed as a version (a safe "no strict winner"
    /// tie — callers fall back to preferring the later-appended one in that case).</summary>
    private static int CompareArtifactVersions(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
            return 0;

        if (Version.TryParse(NormalizeVersion(a), out var va) && Version.TryParse(NormalizeVersion(b), out var vb))
            return va.CompareTo(vb);

        return 0;
    }

    /// <summary>System.Version needs at least a "major.minor" numeric string; pads a bare
    /// integer like "54" to "54.0" and leaves anything already dotted as-is.</summary>
    private static string NormalizeVersion(string v) => v.Contains('.') ? v : v + ".0";

    /// <summary>Keeps only the last <paramref name="maxLines"/> lines of a log so error
    /// messages stay readable while still showing the actual failure reason.</summary>
    private static string TailLines(string log, int maxLines)
    {
        var lines = log.Split('\n');
        return lines.Length <= maxLines
            ? log.Trim()
            : string.Join('\n', lines.Skip(lines.Length - maxLines)).Trim();
    }

    private async Task<string?> ResolveVersionUrlAsync(string versionId)
    {
        var manifest = await _http.GetStringAsync(VersionManifestUrl);
        using var doc = JsonDocument.Parse(manifest);
        if (!doc.RootElement.TryGetProperty("versions", out var versions))
            return null;

        foreach (var v in versions.EnumerateArray())
        {
            if (v.TryGetProperty("id", out var id) &&
                string.Equals(id.GetString(), versionId, StringComparison.OrdinalIgnoreCase) &&
                v.TryGetProperty("url", out var url))
            {
                return url.GetString();
            }
        }
        return null;
    }

    private async Task<string?> ResolveJavaAsync(Installation installation)
    {
        if (!string.IsNullOrWhiteSpace(installation.JavaPath) && File.Exists(installation.JavaPath))
            return installation.JavaPath;

        var detected = await _javaService.DetectJavaAsync();
        return detected?.Path;
    }

    private async Task DownloadAssetsAsync(JsonElement root, string assetIndexId, IProgress<LaunchProgress>? progress)
    {
        if (!root.TryGetProperty("assetIndex", out var assetIndex) ||
            !assetIndex.TryGetProperty("url", out var indexUrl))
            return;

        var indexesDir = Path.Combine(AppPaths.AssetsDir, "indexes");
        Directory.CreateDirectory(indexesDir);
        var indexPath = Path.Combine(indexesDir, assetIndexId + ".json");

        string indexJson;
        if (File.Exists(indexPath))
        {
            indexJson = await File.ReadAllTextAsync(indexPath);
        }
        else
        {
            indexJson = await _http.GetStringAsync(indexUrl.GetString()!);
            await File.WriteAllTextAsync(indexPath, indexJson);
        }

        using var indexDoc = JsonDocument.Parse(indexJson);
        if (!indexDoc.RootElement.TryGetProperty("objects", out var objects))
            return;

        var objectsDir = Path.Combine(AppPaths.AssetsDir, "objects");
        Directory.CreateDirectory(objectsDir);

        var all = objects.EnumerateObject().ToList();
        var total = all.Count;
        var done = 0;
        foreach (var obj in all)
        {
            done++;
            if (!obj.Value.TryGetProperty("hash", out var hashEl))
                continue;

            var hash = hashEl.GetString()!;
            var prefix = hash.Substring(0, 2);
            var dir = Path.Combine(objectsDir, prefix);
            Directory.CreateDirectory(dir);
            var target = Path.Combine(dir, hash);

            if (!File.Exists(target))
                await DownloadFileAsync($"{ResourcesBaseUrl}/{prefix}/{hash}", target);

            if (done % 50 == 0 || done == total)
                progress?.Report(new LaunchProgress
                {
                    Stage = "Downloading assets…",
                    Percent = total > 0 ? done * 100.0 / total : null
                });
        }
    }

    private async Task DownloadFileAsync(string url, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var bytes = await _http.GetByteArrayAsync(url);
        await File.WriteAllBytesAsync(destination, bytes);
    }

    private static void ExtractNatives(string jarPath, string nativesDir)
    {
        try
        {
            using var archive = ZipFile.OpenRead(jarPath);
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue; // directory
                if (entry.FullName.StartsWith("META-INF", StringComparison.OrdinalIgnoreCase)) continue;

                var ext = Path.GetExtension(entry.Name).ToLowerInvariant();
                if (ext is not (".dll" or ".so" or ".dylib" or ".jnilib")) continue;

                var dest = Path.Combine(nativesDir, entry.Name);
                if (!File.Exists(dest))
                    entry.ExtractToFile(dest, overwrite: false);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Native extraction failed for {jarPath}: {ex.Message}");
        }
    }

    private static bool IsLibraryAllowed(JsonElement lib)
    {
        if (!lib.TryGetProperty("rules", out var rules) || rules.ValueKind != JsonValueKind.Array)
            return true;

        var allowed = false;
        foreach (var rule in rules.EnumerateArray())
        {
            var action = rule.TryGetProperty("action", out var a) ? a.GetString() : "allow";
            var matches = true;
            if (rule.TryGetProperty("os", out var os) && os.TryGetProperty("name", out var osName))
                matches = string.Equals(osName.GetString(), "windows", StringComparison.OrdinalIgnoreCase);

            if (matches)
                allowed = action == "allow";
        }
        return allowed;
    }

    private static string? GetNativeClassifier(JsonElement lib)
    {
        if (!lib.TryGetProperty("natives", out var natives) ||
            !natives.TryGetProperty("windows", out var win))
            return null;

        return (win.GetString() ?? string.Empty).Replace("${arch}", "64");
    }

    private static bool IsNativeArtifact(JsonElement lib, string path)
    {
        // Newer versions (1.19+) ship natives as normal artifacts whose maven name
        // contains a "natives-windows" classifier.
        if (lib.TryGetProperty("name", out var name))
        {
            var n = name.GetString() ?? string.Empty;
            if (n.Contains("natives-windows", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return path.Contains("natives-windows", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> ExtractArguments(JsonElement arr, Dictionary<string, string> placeholders)
    {
        if (arr.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                yield return Substitute(item.GetString()!, placeholders);
            }
            // Conditional (rule-based) arguments are objects. We only include the
            // ones that apply to Windows and have no feature requirements, to keep
            // the offline launch simple and robust.
            else if (item.ValueKind == JsonValueKind.Object &&
                     item.TryGetProperty("rules", out var rules) &&
                     AllRulesAllowWindowsNoFeatures(rules) &&
                     item.TryGetProperty("value", out var value))
            {
                if (value.ValueKind == JsonValueKind.String)
                {
                    yield return Substitute(value.GetString()!, placeholders);
                }
                else if (value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var v in value.EnumerateArray())
                        if (v.ValueKind == JsonValueKind.String)
                            yield return Substitute(v.GetString()!, placeholders);
                }
            }
        }
    }

    private static bool AllRulesAllowWindowsNoFeatures(JsonElement rules)
    {
        if (rules.ValueKind != JsonValueKind.Array)
            return false;

        var allowed = false;
        foreach (var rule in rules.EnumerateArray())
        {
            // Skip feature-gated args (demo, custom resolution, quick-play, ...).
            if (rule.TryGetProperty("features", out _))
                return false;

            var action = rule.TryGetProperty("action", out var a) ? a.GetString() : "allow";
            var matches = true;
            if (rule.TryGetProperty("os", out var os) && os.TryGetProperty("name", out var osName))
                matches = string.Equals(osName.GetString(), "windows", StringComparison.OrdinalIgnoreCase);

            if (matches)
                allowed = action == "allow";
        }
        return allowed;
    }

    private static string Substitute(string token, Dictionary<string, string> placeholders)
    {
        foreach (var kv in placeholders)
            token = token.Replace("${" + kv.Key + "}", kv.Value);
        return token;
    }

    /// <summary>
    /// Deterministic offline UUID derived from the username, matching the common
    /// "OfflinePlayer:&lt;name&gt;" MD5 scheme used by offline-mode servers.
    /// </summary>
    private static string OfflineUuid(string username)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + username));
        // Set version (3) and variant bits like a name-based UUID.
        bytes[6] = (byte)((bytes[6] & 0x0f) | 0x30);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        var hex = Convert.ToHexString(bytes).ToLowerInvariant();
        return $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}";
    }
}
