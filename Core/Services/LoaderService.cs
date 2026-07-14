using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using MinecraftControlHub.Core.Models;

namespace MinecraftControlHub.Core.Services;

/// <summary>
/// The extra pieces a mod loader (Fabric/Forge/…) contributes on top of the
/// vanilla launch: a different main class, additional classpath jars, and extra
/// JVM/game arguments. When <see cref="Handled"/> is false the caller should just
/// launch vanilla.
/// </summary>
public class LoaderLaunchInfo
{
    public bool Handled { get; set; }
    public string? Error { get; set; }
    /// <summary>Loader version id inside versions/ (e.g. 1.20.1-forge-47.4.20).</summary>
    public string? VersionId { get; set; }
    /// <summary>Forge/NeoForge ship a complete module-path launch profile; vanilla args must not be mixed in.</summary>
    public bool UseLoaderLaunch { get; set; }
    public string? MainClass { get; set; }
    public List<string> ExtraClasspath { get; } = new();
    public List<string> ExtraJvmArgs { get; } = new();
    public List<string> ExtraGameArgs { get; } = new();
}

/// <summary>A selectable Minecraft version from Mojang's manifest (id + whether it's a full release vs. a snapshot).</summary>
public record McVersionEntry(string Id, bool IsRelease);

public interface ILoaderService
{
    /// <summary>
    /// Prepares (downloading/installing as needed) the loader for an installation
    /// and returns what it adds to the launch. For vanilla it returns Handled=false.
    /// </summary>
    Task<LoaderLaunchInfo> PrepareAsync(Installation installation, string javaPath, IProgress<LaunchProgress>? progress = null);

    /// <summary>All official Minecraft versions (releases + snapshots), newest first — for the version picker.</summary>
    Task<List<McVersionEntry>> GetMinecraftVersionsAsync();

    /// <summary>All available loader builds for a given Minecraft version, newest first — for the loader-version picker.</summary>
    Task<List<string>> GetAvailableLoaderVersionsAsync(LoaderType loader, string mc);
}

public class LoaderService : ILoaderService
{
    private const string FabricMetaBase = "https://meta.fabricmc.net/v2/versions/loader";
    private const string QuiltMetaBase = "https://meta.quiltmc.org/v3/versions/loader";
    private const string ForgeMavenMeta = "https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml";
    private const string NeoForgeMavenMeta = "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml";
    // BUGFIX: NeoForge did not exist as its own maven artifact until MC 1.20.2. Its very
    // first release, for MC 1.20.1, was published under the OLD "forge" artifact/version
    // scheme (net.neoforged:forge, versions like "1.20.1-47.1.106") — the SAME layout as
    // classic Minecraft Forge, just hosted on NeoForge's maven. From 1.20.2 onward it moved
    // to its own "net.neoforged:neoforge" artifact with the short "20.2.x" style versions.
    // The maven-metadata.xml above has NO "20.1.x" entries at all (versions start at
    // "20.2.x"), so picking a NeoForge build for MC 1.20.1 previously fell through to the
    // loose "20." prefix and grabbed the highest version overall (e.g. "20.6.139", built for
    // MC 1.20.6) — a real version/MC mismatch that crashes the JVM on startup with a module
    // ResolutionException. Use this legacy artifact whenever the installation's MC version
    // is 1.20.1.
    private const string NeoForgeLegacyMavenMeta = "https://maven.neoforged.net/releases/net/neoforged/forge/maven-metadata.xml";
    private const string NeoForgeLegacyMcVersion = "1.20.1";
    private const string McVersionManifest = "https://launchermeta.mojang.com/mc/game/version_manifest_v2.json";

    private readonly HttpClient _http;

    public LoaderService(HttpClient http)
    {
        _http = http;
    }

    public async Task<LoaderLaunchInfo> PrepareAsync(Installation installation, string javaPath, IProgress<LaunchProgress>? progress = null)
    {
        var mc = installation.MinecraftVersion?.Trim() ?? string.Empty;
        try
        {
            return installation.Loader switch
            {
                LoaderType.Fabric => await PrepareFabricLikeAsync(installation, mc, FabricMetaBase, progress),
                LoaderType.Quilt => await PrepareFabricLikeAsync(installation, mc, QuiltMetaBase, progress),
                LoaderType.Forge => await PrepareForgeLikeAsync(installation, mc, javaPath, false, progress),
                LoaderType.NeoForge => await PrepareForgeLikeAsync(installation, mc, javaPath, true, progress),
                _ => new LoaderLaunchInfo { Handled = false }
            };
        }
        catch (Exception ex)
        {
            return new LoaderLaunchInfo { Handled = true, Error = ex.Message };
        }
    }

    public async Task<List<McVersionEntry>> GetMinecraftVersionsAsync()
    {
        var json = await _http.GetStringAsync(McVersionManifest);
        using var doc = JsonDocument.Parse(json);
        var result = new List<McVersionEntry>();
        if (doc.RootElement.TryGetProperty("versions", out var versions) && versions.ValueKind == JsonValueKind.Array)
        {
            foreach (var v in versions.EnumerateArray())
            {
                if (!v.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.String) continue;
                var id = idEl.GetString();
                if (string.IsNullOrWhiteSpace(id)) continue;
                var isRelease = v.TryGetProperty("type", out var typeEl) && typeEl.GetString() == "release";
                result.Add(new McVersionEntry(id!, isRelease));
            }
        }
        // Mojang's manifest is already newest-first; keep that order as-is.
        return result;
    }

    public async Task<List<string>> GetAvailableLoaderVersionsAsync(LoaderType loader, string mc)
    {
        mc = mc?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(mc))
            return new List<string>();

        try
        {
            return loader switch
            {
                LoaderType.Fabric => await GetFabricLikeVersionsAsync(mc, FabricMetaBase),
                LoaderType.Quilt => await GetFabricLikeVersionsAsync(mc, QuiltMetaBase),
                LoaderType.Forge => await GetForgeLikeVersionsAsync(mc, false),
                LoaderType.NeoForge => await GetForgeLikeVersionsAsync(mc, true),
                _ => new List<string>()
            };
        }
        catch
        {
            return new List<string>();
        }
    }

    private async Task<List<string>> GetFabricLikeVersionsAsync(string mc, string metaBase)
    {
        var listJson = await _http.GetStringAsync($"{metaBase}/{Uri.EscapeDataString(mc)}");
        using var listDoc = JsonDocument.Parse(listJson);
        if (listDoc.RootElement.ValueKind != JsonValueKind.Array)
            return new List<string>();

        // The Fabric/Quilt meta API already returns builds newest-first.
        return listDoc.RootElement.EnumerateArray()
            .Select(e => e.TryGetProperty("loader", out var l) && l.TryGetProperty("version", out var v) ? v.GetString() : null)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!)
            .ToList();
    }

    private async Task<List<string>> GetForgeLikeVersionsAsync(string mc, bool neo)
    {
        if (neo)
        {
            if (mc == NeoForgeLegacyMcVersion)
            {
                var legacyXml = await _http.GetStringAsync(NeoForgeLegacyMavenMeta);
                var legacyVersions = ExtractXmlVersions(legacyXml);
                return legacyVersions.Where(v => v.StartsWith(mc + "-", StringComparison.Ordinal))
                    .OrderByDescending(v => v, NumericVersionComparer).ToList();
            }

            var xml = await _http.GetStringAsync(NeoForgeMavenMeta);
            var versions = ExtractXmlVersions(xml);
            return PickAllNeoForgeVersions(versions, mc);
        }

        var forgeXml = await _http.GetStringAsync(ForgeMavenMeta);
        var forgeVersions = ExtractXmlVersions(forgeXml);
        return forgeVersions.Where(v => v.StartsWith(mc + "-", StringComparison.Ordinal))
            .OrderByDescending(v => v, NumericVersionComparer).ToList();
    }

    /// <summary>Same matching rules as <see cref="PickNeoForgeVersion"/> but returns every matching build, newest first.</summary>
    private static List<string> PickAllNeoForgeVersions(List<string> versions, string mc)
    {
        var parts = mc.Split('.');
        if (parts.Length < 2)
            return versions.OrderByDescending(v => v, NumericVersionComparer).ToList();

        var prefixes = new List<string>();
        if (parts.Length >= 3)
            prefixes.Add($"{parts[1]}.{parts[2]}.");
        prefixes.Add($"{parts[1]}.0.");
        prefixes.Add($"{parts[1]}.");

        foreach (var prefix in prefixes)
        {
            var matches = versions.Where(v => v.StartsWith(prefix, StringComparison.Ordinal)).ToList();
            if (matches.Count > 0)
                return matches.OrderByDescending(v => v, NumericVersionComparer).ToList();
        }

        return new List<string>();
    }

    // ------------------------------------------------------------ Fabric / Quilt

    private async Task<LoaderLaunchInfo> PrepareFabricLikeAsync(Installation installation, string mc, string metaBase, IProgress<LaunchProgress>? progress)
    {
        var info = new LoaderLaunchInfo { Handled = true };
        progress?.Report(new LaunchProgress { Stage = "Resolving loader…" });

        string loaderVersion;
        if (!string.IsNullOrWhiteSpace(installation.LoaderVersion))
        {
            // Respect a user-pinned exact build from the loader-version picker.
            loaderVersion = installation.LoaderVersion!.Trim();
        }
        else
        {
            // Newest compatible loader for this game version.
            var listJson = await _http.GetStringAsync($"{metaBase}/{Uri.EscapeDataString(mc)}");
            using var listDoc = JsonDocument.Parse(listJson);
            if (listDoc.RootElement.ValueKind != JsonValueKind.Array || listDoc.RootElement.GetArrayLength() == 0)
            {
                info.Error = $"No loader build is available for Minecraft {mc}.";
                return info;
            }

            loaderVersion = listDoc.RootElement[0].GetProperty("loader").GetProperty("version").GetString()!;
        }

        // The launcher profile JSON (libraries + mainClass) for game+loader.
        progress?.Report(new LaunchProgress { Stage = "Downloading loader metadata…" });
        var profileJson = await _http.GetStringAsync(
            $"{metaBase}/{Uri.EscapeDataString(mc)}/{Uri.EscapeDataString(loaderVersion)}/profile/json");
        using var profileDoc = JsonDocument.Parse(profileJson);
        var root = profileDoc.RootElement;

        if (root.TryGetProperty("mainClass", out var mainClass) && mainClass.ValueKind == JsonValueKind.String)
            info.MainClass = mainClass.GetString();

        if (root.TryGetProperty("libraries", out var libs) && libs.ValueKind == JsonValueKind.Array)
        {
            var all = libs.EnumerateArray().ToList();
            var done = 0;
            foreach (var lib in all)
            {
                done++;
                progress?.Report(new LaunchProgress
                {
                    Stage = "Downloading loader libraries…",
                    Percent = all.Count > 0 ? done * 100.0 / all.Count : null
                });

                if (!lib.TryGetProperty("name", out var nameEl)) continue;
                var name = nameEl.GetString();
                if (string.IsNullOrWhiteSpace(name)) continue;

                var baseUrl = lib.TryGetProperty("url", out var urlEl) && urlEl.ValueKind == JsonValueKind.String
                    ? urlEl.GetString()!
                    : "https://maven.fabricmc.net/";

                var local = await DownloadMavenAsync(name, baseUrl);
                if (local != null)
                    info.ExtraClasspath.Add(local);
            }
        }

        // Fabric/Quilt use the vanilla arguments as-is; nothing extra needed.
        return info;
    }

    // ------------------------------------------------------------ Forge / NeoForge

    private async Task<LoaderLaunchInfo> PrepareForgeLikeAsync(
        Installation installation, string mc, string javaPath, bool neo, IProgress<LaunchProgress>? progress)
    {
        var info = new LoaderLaunchInfo { Handled = true, UseLoaderLaunch = true };
        progress?.Report(new LaunchProgress { Stage = "Resolving loader…" });

        // Respect a user-pinned exact build (from the loader-version picker); otherwise
        // fall back to resolving the newest compatible build, as before.
        var loaderVersion = !string.IsNullOrWhiteSpace(installation.LoaderVersion)
            ? installation.LoaderVersion!.Trim()
            : await ResolveForgeVersionAsync(mc, neo);
        if (loaderVersion == null)
        {
            info.Error = $"No {(neo ? "NeoForge" : "Forge")} build was found for Minecraft {mc}.";
            return info;
        }

        // Pre-1.13 Forge ships a completely different (Swing-only) installer with no
        // headless "--installClient" support and no processor system; it would just
        // hang waiting for a display here. That legacy install flow isn't implemented,
        // so fail clearly instead of hanging or crashing mysteriously.
        if (!neo && IsLegacyForgeMinecraftVersion(mc))
        {
            info.Error = $"Forge for Minecraft {mc} uses the old pre-1.13 installer, which this app's automatic headless installer does not support yet. Please install it manually via the official Forge installer, then re-select this installation.";
            return info;
        }

        var versionId = ToLoaderVersionId(loaderVersion, neo);
        info.VersionId = versionId;

        var installedJson = FindInstalledLoaderJson(versionId, mc, neo, loaderVersion);
        if (installedJson == null)
        {
            progress?.Report(new LaunchProgress { Stage = "Running loader installer (this can take a while)…" });
            var (installOk, installLog) = await RunForgeInstallerAsync(mc, loaderVersion, javaPath, neo);
            if (!installOk)
            {
                info.Error = $"The {(neo ? "NeoForge" : "Forge")} installer for {loaderVersion} failed:\n{installLog}";
                return info;
            }
            installedJson = FindInstalledLoaderJson(versionId, mc, neo, loaderVersion);
            if (installedJson == null)
            {
                info.Error = $"The {(neo ? "NeoForge" : "Forge")} installer reported success but no version metadata was found at versions\\{versionId}\\{versionId}.json. Installer log:\n{installLog}";
                return info;
            }
        }

        progress?.Report(new LaunchProgress { Stage = "Reading loader metadata…" });
        var json = await File.ReadAllTextAsync(installedJson);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
            info.VersionId = idEl.GetString();

        if (root.TryGetProperty("mainClass", out var mainClass) && mainClass.ValueKind == JsonValueKind.String)
            info.MainClass = mainClass.GetString();

        if (root.TryGetProperty("libraries", out var libs) && libs.ValueKind == JsonValueKind.Array)
        {
            progress?.Report(new LaunchProgress { Stage = "Downloading loader libraries…" });
            await EnsureForgeLibrariesAsync(libs, progress);

            foreach (var lib in libs.EnumerateArray())
            {
                var local = ResolveForgeLibraryPath(lib);
                if (local != null && File.Exists(local))
                    info.ExtraClasspath.Add(local);
            }
        }

        if (root.TryGetProperty("arguments", out var args))
        {
            if (args.TryGetProperty("jvm", out var jvm) && jvm.ValueKind == JsonValueKind.Array)
                foreach (var a in jvm.EnumerateArray())
                    if (a.ValueKind == JsonValueKind.String)
                        info.ExtraJvmArgs.Add(a.GetString()!);

            if (args.TryGetProperty("game", out var game) && game.ValueKind == JsonValueKind.Array)
                foreach (var a in game.EnumerateArray())
                    if (a.ValueKind == JsonValueKind.String)
                        info.ExtraGameArgs.Add(a.GetString()!);
        }

        return info;
    }

    /// <summary>Maps a maven loader version to the folder name under versions/.</summary>
    private static string ToLoaderVersionId(string loaderVersion, bool neo)
    {
        if (neo)
            return $"neoforge-{loaderVersion}";

        // "1.20.1-47.4.20" -> "1.20.1-forge-47.4.20"
        var dash = loaderVersion.IndexOf('-');
        return dash < 0
            ? loaderVersion
            : $"{loaderVersion[..dash]}-forge-{loaderVersion[(dash + 1)..]}";
    }

    private async Task<string?> ResolveForgeVersionAsync(string mc, bool neo)
    {
        try
        {
            if (neo)
            {
                if (mc == NeoForgeLegacyMcVersion)
                {
                    // Legacy 1.20.1 NeoForge: same maven layout/versioning as classic Forge.
                    var legacyXml = await _http.GetStringAsync(NeoForgeLegacyMavenMeta);
                    var legacyVersions = ExtractXmlVersions(legacyXml);
                    var legacyForMc = legacyVersions.Where(v => v.StartsWith(mc + "-", StringComparison.Ordinal)).ToList();
                    return legacyForMc.OrderBy(v => v, NumericVersionComparer).LastOrDefault();
                }

                var xml = await _http.GetStringAsync(NeoForgeMavenMeta);
                var versions = ExtractXmlVersions(xml);
                return PickNeoForgeVersion(versions, mc);
            }

            var forgeXml = await _http.GetStringAsync(ForgeMavenMeta);
            var forgeVersions = ExtractXmlVersions(forgeXml);
            var forMc = forgeVersions.Where(v => v.StartsWith(mc + "-", StringComparison.Ordinal)).ToList();
            // NOTE: plain string ordering is wrong here — "47.4.9" sorts AFTER "47.4.20"
            // alphabetically even though 20 > 9 numerically, so the naive .LastOrDefault()
            // that used to be here could silently pick an OLDER build than the real latest
            // one, sometimes one with known bugs/crashes. Sort numerically instead.
            return forMc.OrderBy(v => v, NumericVersionComparer).LastOrDefault();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// NeoForge versions encode MC version in semver: 21.1.x = MC 1.21.1, 20.4.x = MC 1.20.4.
    /// </summary>
    private static string? PickNeoForgeVersion(List<string> versions, string mc)
    {
        var parts = mc.Split('.');
        if (parts.Length < 2)
            return versions.OrderBy(v => v, NumericVersionComparer).LastOrDefault();

        var prefixes = new List<string>();
        if (parts.Length >= 3)
            prefixes.Add($"{parts[1]}.{parts[2]}.");
        prefixes.Add($"{parts[1]}.0.");
        prefixes.Add($"{parts[1]}.");

        foreach (var prefix in prefixes)
        {
            var matches = versions.Where(v => v.StartsWith(prefix, StringComparison.Ordinal)).ToList();
            if (matches.Count == 0)
                continue;

            var stable = matches.Where(v => !v.Contains("-beta", StringComparison.OrdinalIgnoreCase)).ToList();
            // Same numeric-vs-alphabetic pitfall as Forge above — sort properly before
            // taking the last (i.e. highest) build instead of trusting maven's raw order.
            return (stable.Count > 0 ? stable : matches).OrderBy(v => v, NumericVersionComparer).Last();
        }

        return null;
    }

    /// <summary>
    /// Compares dotted/dashed version strings (e.g. "47.4.20", "21.1.90-beta") segment by
    /// segment, numerically when a segment is a number, so "47.4.20" correctly sorts after
    /// "47.4.9" (plain string/ordinal comparison gets this backwards).
    /// </summary>
    private static readonly IComparer<string> NumericVersionComparer = Comparer<string>.Create((a, b) =>
    {
        var ta = System.Text.RegularExpressions.Regex.Split(a, @"[.\-]");
        var tb = System.Text.RegularExpressions.Regex.Split(b, @"[.\-]");
        var len = Math.Max(ta.Length, tb.Length);
        for (var i = 0; i < len; i++)
        {
            var sa = i < ta.Length ? ta[i] : string.Empty;
            var sb = i < tb.Length ? tb[i] : string.Empty;
            int cmp;
            if (int.TryParse(sa, out var na) && int.TryParse(sb, out var nb))
                cmp = na.CompareTo(nb);
            else
                cmp = string.CompareOrdinal(sa, sb);
            if (cmp != 0) return cmp;
        }
        return 0;
    });

    /// <summary>True for Minecraft versions older than 1.13, which use Forge's legacy installer.</summary>
    private static bool IsLegacyForgeMinecraftVersion(string mc)
    {
        var parts = mc.Split('.');
        if (parts.Length < 2 || !int.TryParse(parts[0], out var major) || !int.TryParse(parts[1], out var minor))
            return false;
        return major == 1 && minor < 13;
    }

    private static List<string> ExtractXmlVersions(string xml)
    {
        var result = new List<string>();
        var matches = System.Text.RegularExpressions.Regex.Matches(xml, "<version>(.*?)</version>");
        foreach (System.Text.RegularExpressions.Match m in matches)
            result.Add(m.Groups[1].Value);
        return result;
    }

    private static string? FindInstalledLoaderJson(string versionId, string mc, bool neo, string loaderVersion)
    {
        var direct = Path.Combine(AppPaths.VersionsDir, versionId, versionId + ".json");
        if (File.Exists(direct))
            return direct;

        if (!Directory.Exists(AppPaths.VersionsDir))
            return null;

        foreach (var dir in Directory.GetDirectories(AppPaths.VersionsDir))
        {
            var name = Path.GetFileName(dir);
            if (neo)
            {
                if (!name.StartsWith("neoforge-", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!name.Contains(loaderVersion, StringComparison.Ordinal))
                    continue;
            }
            else
            {
                // BUGFIX (real crash: Forge launching with NeoForge's own profile —
                // "Failed to find system mod: forge" from FML's ModSorter at startup,
                // because the jars actually on the classpath were NeoForge's, not
                // Forge's). "neoforge" contains the substring "forge", so a plain
                // Contains("forge") check here could match a NEOFORGE version folder
                // whenever its name also happened to include this installation's
                // Minecraft version — which is exactly the case for the legacy 1.20.1
                // NeoForge build, folder-named "neoforge-1.20.1-...". Explicitly rule
                // out any "neoforge-" folder before falling back to the loose search.
                if (name.StartsWith("neoforge-", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!name.Contains("forge", StringComparison.OrdinalIgnoreCase) ||
                    !name.Contains(mc, StringComparison.Ordinal))
                    continue;
            }

            var json = Path.Combine(dir, name + ".json");
            if (File.Exists(json))
                return json;
        }

        return null;
    }

    private async Task EnsureForgeLibrariesAsync(JsonElement libs, IProgress<LaunchProgress>? progress)
    {
        var all = libs.EnumerateArray().ToList();
        var done = 0;
        foreach (var lib in all)
        {
            done++;
            progress?.Report(new LaunchProgress
            {
                Stage = "Downloading loader libraries…",
                Percent = all.Count > 0 ? done * 100.0 / all.Count : null
            });

            if (!lib.TryGetProperty("downloads", out var dls) ||
                !dls.TryGetProperty("artifact", out var artifact))
                continue;

            if (!artifact.TryGetProperty("path", out var pathEl) ||
                pathEl.ValueKind != JsonValueKind.String)
                continue;

            var local = Path.Combine(AppPaths.LibrariesDir,
                pathEl.GetString()!.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(local))
                continue;

            if (!artifact.TryGetProperty("url", out var urlEl) ||
                urlEl.ValueKind != JsonValueKind.String)
                continue;

            Directory.CreateDirectory(Path.GetDirectoryName(local)!);
            var bytes = await _http.GetByteArrayAsync(urlEl.GetString()!);
            await File.WriteAllBytesAsync(local, bytes);
        }
    }

    private async Task<(bool Ok, string Log)> RunForgeInstallerAsync(string mc, string forgeVersion, string javaPath, bool neo)
    {
        // The official installer expects a .minecraft-style folder that contains a
        // launcher_profiles.json; it then installs the version + libraries there.
        var target = AppPaths.MinecraftRoot;
        var profilesFile = Path.Combine(target, "launcher_profiles.json");
        if (!File.Exists(profilesFile))
            await File.WriteAllTextAsync(profilesFile, "{\"profiles\":{},\"settings\":{},\"version\":3}");

        var isLegacyNeo = neo && mc == NeoForgeLegacyMcVersion;
        var installerUrl = neo
            ? (isLegacyNeo
                ? $"https://maven.neoforged.net/releases/net/neoforged/forge/{forgeVersion}/forge-{forgeVersion}-installer.jar"
                : $"https://maven.neoforged.net/releases/net/neoforged/neoforge/{forgeVersion}/neoforge-{forgeVersion}-installer.jar")
            : $"https://maven.minecraftforge.net/net/minecraftforge/forge/{forgeVersion}/forge-{forgeVersion}-installer.jar";

        var installerPath = Path.Combine(Path.GetTempPath(), $"mch-{(neo ? "neoforge" : "forge")}-{forgeVersion}-installer.jar");
        var bytes = await _http.GetByteArrayAsync(installerUrl);
        await File.WriteAllBytesAsync(installerPath, bytes);

        var psi = new ProcessStartInfo
        {
            FileName = javaPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = target
        };
        // Forces the installer's Swing GUI off. Without this, if for whatever reason the
        // CLI flag below isn't recognised by a given installer build, SimpleInstaller
        // silently falls back to opening a GUI window — which then just hangs forever with
        // no display attached. That looked exactly like "Forge crashes on startup" from the
        // outside (the whole launch would just sit there / eventually the app gave up).
        psi.ArgumentList.Add("-Djava.awt.headless=true");
        psi.ArgumentList.Add("-jar");
        psi.ArgumentList.Add(installerPath);
        // IMPORTANT: the official installer parses this with jopt-simple as an OPTIONAL
        // argument. jopt-simple only binds an optional argument's value when it's attached
        // to the SAME token with '='; passed as two separate tokens (as this used to be),
        // the path is left unbound and silently ignored (installer falls back to its
        // default target, "."). Joining them with '=' makes sure the intended folder is
        // actually used.
        psi.ArgumentList.Add($"--installClient={target}");

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

        if (!process.Start())
            return (false, "Failed to start the Java process for the loader installer.");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var finished = true;
        using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5)))
        {
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                finished = false;
                try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
            }
        }

        try { File.Delete(installerPath); } catch { /* ignore */ }

        var log = TailLines(stdout.ToString() + stderr, 60);

        if (!finished)
            return (false, "The installer did not finish within 5 minutes and was stopped.\n" + log);

        if (process.ExitCode != 0)
            return (false, $"The installer exited with code {process.ExitCode}.\n{log}");

        return (true, log);
    }

    /// <summary>Keeps only the last <paramref name="maxLines"/> lines of a log so error
    /// messages stay readable while still showing the actual failure reason.</summary>
    private static string TailLines(string log, int maxLines)
    {
        var lines = log.Split('\n');
        return lines.Length <= maxLines
            ? log.Trim()
            : string.Join('\n', lines.Skip(lines.Length - maxLines)).Trim();
    }

    private static string? ResolveForgeLibraryPath(JsonElement lib)
    {
        // Prefer an explicit downloaded path, else derive from the maven name.
        if (lib.TryGetProperty("downloads", out var dls) &&
            dls.TryGetProperty("artifact", out var artifact) &&
            artifact.TryGetProperty("path", out var pathEl) &&
            pathEl.ValueKind == JsonValueKind.String)
        {
            return Path.Combine(AppPaths.LibrariesDir, pathEl.GetString()!.Replace('/', Path.DirectorySeparatorChar));
        }

        if (lib.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
        {
            var rel = MavenToPath(nameEl.GetString()!);
            if (rel != null)
                return Path.Combine(AppPaths.LibrariesDir, rel.Replace('/', Path.DirectorySeparatorChar));
        }
        return null;
    }

    // -------------------------------------------------------------- Maven helpers

    /// <summary>Downloads a maven artifact (by "group:artifact:version[:classifier]")
    /// from a base repo URL into the shared libraries dir; returns the local path.</summary>
    private async Task<string?> DownloadMavenAsync(string name, string baseUrl)
    {
        var rel = MavenToPath(name);
        if (rel == null) return null;

        var local = Path.Combine(AppPaths.LibrariesDir, rel.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(local)) return local;

        Directory.CreateDirectory(Path.GetDirectoryName(local)!);
        var url = baseUrl.TrimEnd('/') + "/" + rel;
        var bytes = await _http.GetByteArrayAsync(url);
        await File.WriteAllBytesAsync(local, bytes);
        return local;
    }

    /// <summary>Converts a maven coordinate to its repo-relative jar path.</summary>
    private static string? MavenToPath(string name)
    {
        // group:artifact:version[:classifier][@ext]
        var ext = "jar";
        var at = name.IndexOf('@');
        if (at >= 0)
        {
            ext = name[(at + 1)..];
            name = name[..at];
        }

        var parts = name.Split(':');
        if (parts.Length < 3) return null;

        var group = parts[0].Replace('.', '/');
        var artifact = parts[1];
        var version = parts[2];
        var classifier = parts.Length > 3 ? "-" + parts[3] : string.Empty;

        return $"{group}/{artifact}/{version}/{artifact}-{version}{classifier}.{ext}";
    }
}
