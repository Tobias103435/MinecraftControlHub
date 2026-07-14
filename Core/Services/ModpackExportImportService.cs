using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using MinecraftControlHub.Core.Models;

namespace MinecraftControlHub.Core.Services;

public interface IModpackExportImportService
{
    /// <summary>
    /// Exports an installation to a .mrpack file — the Modrinth Modpack Format that
    /// Prism Launcher itself uses for "Export Instance → Modrinth pack format", so the
    /// result opens directly via Prism's "Import Instance" with no conversion needed.
    /// </summary>
    Task<ModpackExportResult> ExportAsync(Installation installation, string outputFilePath, IProgress<string>? progress = null);

    /// <summary>
    /// Imports a modpack zip as a brand-new installation. Auto-detects which of the
    /// two real-world formats it is:
    /// - Prism Launcher / MultiMC's own NATIVE instance export (mmc-pack.json +
    ///   instance.cfg + a "minecraft" folder) — what Prism's "Export Instance" button
    ///   produces by default.
    /// - The Modrinth Modpack Format (modrinth.index.json, i.e. a ".mrpack") — used by
    ///   Modrinth App and Prism's "Export Instance → Modrinth pack format" option.
    /// Either way: creates the installation, brings in every mod file, and copies in
    /// configs/resourcepacks/shaderpacks/side-loaded mods.
    /// </summary>
/// <summary>
    /// Exports an installation to a Prism Launcher / MultiMC native instance zip —
    /// the format Prism's own "Export Instance" button produces by default (mmc-pack.json
    /// + instance.cfg + a "minecraft" folder with mods/config/resourcepacks/shaderpacks).
    /// This zip can be imported straight into Prism via "Add Instance → Import from zip"
    /// without any extra steps.  World saves are intentionally excluded.
    /// </summary>
    Task<ModpackExportResult> ExportPrismNativeAsync(Installation installation, string outputFilePath, IProgress<string>? progress = null);

    Task<ModpackImportResult> ImportAsync(string filePath, IProgress<string>? progress = null);
}

public class ModpackExportImportService : IModpackExportImportService
{
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly IModService _modService;
    private readonly IInstallationService _installationService;
    private readonly IModrinthApiClient _modrinthClient;
    private readonly ILoaderService _loaderService;
    private readonly IAppLogService _log;

    // Folders copied verbatim into overrides/. World saves are deliberately excluded —
    // a modpack format ships the *setup* (mods/configs/resourcepacks/shaders), not a
    // savegame backup, exactly like Prism's own Modrinth-pack export behaves.
    private static readonly string[] OverrideFolders = { "config", "resourcepacks", "shaderpacks" };

    public ModpackExportImportService(
        IModService modService,
        IInstallationService installationService,
        IModrinthApiClient modrinthClient,
        ILoaderService loaderService,
        IAppLogService log)
    {
        _modService = modService;
        _installationService = installationService;
        _modrinthClient = modrinthClient;
        _loaderService = loaderService;
        _log = log;
    }

    public async Task<ModpackExportResult> ExportAsync(Installation installation, string outputFilePath, IProgress<string>? progress = null)
    {
        var result = new ModpackExportResult();
        var workDir = Path.Combine(Path.GetTempPath(), "MinecraftControlHub-export-" + Guid.NewGuid());

        try
        {
            Directory.CreateDirectory(workDir);
            var overridesDir = Path.Combine(workDir, "overrides");
            Directory.CreateDirectory(overridesDir);

            progress?.Report("Collecting installed mods…");
            var mods = await _modService.GetInstalledModsAsync(installation.Id);
            var instanceDir = AppPaths.InstanceDir(installation.Id);

            var index = new ModrinthModpackIndex
            {
                VersionId = installation.Id.ToString("N"),
                Name = installation.Name,
                Summary = $"Exported from Minecraft Control Hub on {DateTime.Now:yyyy-MM-dd}.",
                Dependencies = new Dictionary<string, string> { ["minecraft"] = installation.MinecraftVersion }
            };

            var loaderKey = LoaderDependencyKey(installation.Loader);
            if (loaderKey != null)
            {
                var loaderVersion = installation.LoaderVersion;
                if (string.IsNullOrWhiteSpace(loaderVersion))
                {
                    // "Latest (recommended)" was pinned at launch time, not to a concrete
                    // build — resolve the newest compatible build right now so Prism has
                    // an actual version number to install (it can't resolve "latest" itself).
                    try
                    {
                        var builds = await _loaderService.GetAvailableLoaderVersionsAsync(installation.Loader, installation.MinecraftVersion);
                        loaderVersion = builds.FirstOrDefault();
                    }
                    catch (Exception ex)
                    {
                        result.Warnings.Add($"Could not resolve an exact {loaderKey} build to pin in the export ({ex.Message}); pick the loader manually in Prism.");
                    }
                }

                if (!string.IsNullOrWhiteSpace(loaderVersion))
                    index.Dependencies[loaderKey] = loaderVersion!;
            }

            // Mods installed from Modrinth (have a download URL + both hashes) are
            // declared as external downloads — exactly like Prism/Modrinth's own
            // exporter, which keeps the .mrpack small since jar bytes aren't embedded.
            // Anything else (manually side-loaded jars, or older records missing hash
            // data) is copied byte-for-byte into overrides/mods instead, so nothing is lost.
            progress?.Report("Packing mods…");
            foreach (var mod in mods)
            {
                var canDeclareAsDownload =
                    !string.IsNullOrWhiteSpace(mod.DownloadUrl) &&
                    !string.IsNullOrWhiteSpace(mod.Sha1Hash) &&
                    !string.IsNullOrWhiteSpace(mod.Sha512Hash) &&
                    !string.IsNullOrWhiteSpace(mod.FileName) &&
                    mod.FileSize.HasValue;

                if (canDeclareAsDownload)
                {
                    index.Files.Add(new ModrinthModpackFile
                    {
                        Path = $"mods/{mod.FileName}",
                        Hashes = new Dictionary<string, string> { ["sha1"] = mod.Sha1Hash!, ["sha512"] = mod.Sha512Hash! },
                        Env = new ModrinthModpackEnv { Client = "required", Server = "unsupported" },
                        Downloads = new List<string> { mod.DownloadUrl! },
                        FileSize = mod.FileSize!.Value
                    });
                    result.ModsIncludedAsDownloads++;
                }
                else if (!string.IsNullOrWhiteSpace(mod.FilePath) && File.Exists(mod.FilePath))
                {
                    var modsOverrideDir = Path.Combine(overridesDir, "mods");
                    Directory.CreateDirectory(modsOverrideDir);
                    File.Copy(mod.FilePath, Path.Combine(modsOverrideDir, Path.GetFileName(mod.FilePath)), overwrite: true);
                    result.ModsIncludedAsOverrides++;
                }
                else
                {
                    result.Warnings.Add($"Skipped \"{mod.Name}\" — its file could not be found on disk.");
                }
            }

            // Copy config/resourcepacks/shaderpacks + options.txt verbatim.
            progress?.Report("Packing config, resourcepacks and shaderpacks…");
            foreach (var folder in OverrideFolders)
            {
                var source = Path.Combine(instanceDir, folder);
                if (Directory.Exists(source))
                    CopyDirectory(source, Path.Combine(overridesDir, folder));
            }
            var optionsFile = Path.Combine(instanceDir, "options.txt");
            if (File.Exists(optionsFile))
                File.Copy(optionsFile, Path.Combine(overridesDir, "options.txt"), overwrite: true);

            var manifestPath = Path.Combine(workDir, "modrinth.index.json");
            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(index, ManifestJsonOptions));

            progress?.Report("Writing .mrpack file…");
            if (!outputFilePath.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase) &&
                !outputFilePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                outputFilePath += ".mrpack";
            }
            if (File.Exists(outputFilePath))
                File.Delete(outputFilePath);
            ZipFile.CreateFromDirectory(workDir, outputFilePath, CompressionLevel.Optimal, includeBaseDirectory: false);

            result.Success = true;
            result.FilePath = outputFilePath;
            result.Summary = $"Exported \"{installation.Name}\" — {result.ModsIncludedAsDownloads} mod(s) as downloads" +
                              (result.ModsIncludedAsOverrides > 0 ? $", {result.ModsIncludedAsOverrides} bundled directly" : "") +
                              ". Open it with Prism Launcher's \"Import Instance\" (or drag it in) to switch over.";

            _log.Log("Modpack", $"Exported \"{installation.Name}\" to {outputFilePath} ({result.ModsIncludedAsDownloads} downloads, {result.ModsIncludedAsOverrides} bundled).");
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            result.Summary = $"Export failed: {ex.Message}";
            _log.LogError("Modpack", "Export failed", ex);
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch { /* best effort */ }
        }

        return result;
    }


    public async Task<ModpackExportResult> ExportPrismNativeAsync(Installation installation, string outputFilePath, IProgress<string>? progress = null)
    {
        var result = new ModpackExportResult();
        var workDir = Path.Combine(Path.GetTempPath(), "MinecraftControlHub-prismexport-" + Guid.NewGuid());

        try
        {
            Directory.CreateDirectory(workDir);

            // ── mmc-pack.json ────────────────────────────────────────────────────────
            // Declares the Minecraft version and mod loader via Prism's component UIDs.
            progress?.Report("Building mmc-pack.json…");

            var loaderVersion = installation.LoaderVersion;
            if (string.IsNullOrWhiteSpace(loaderVersion) && installation.Loader != LoaderType.Vanilla)
            {
                // Resolve "Latest (recommended)" to a concrete build — same logic as the .mrpack exporter.
                try
                {
                    var builds = await _loaderService.GetAvailableLoaderVersionsAsync(installation.Loader, installation.MinecraftVersion);
                    loaderVersion = builds.FirstOrDefault();
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Could not resolve an exact {installation.Loader} build to pin in the export ({ex.Message}); pick the loader manually after importing in Prism.");
                }
            }

            var components = new List<object>
            {
                new { uid = "net.minecraft", version = installation.MinecraftVersion }
            };

            var loaderUid = LoaderToMmcUid(installation.Loader);
            if (loaderUid != null && !string.IsNullOrWhiteSpace(loaderVersion))
                components.Add(new { uid = loaderUid, version = loaderVersion });

            var mmcPack = new { formatVersion = 1, components };
            await File.WriteAllTextAsync(
                Path.Combine(workDir, "mmc-pack.json"),
                JsonSerializer.Serialize(mmcPack, ManifestJsonOptions));

            // ── instance.cfg ─────────────────────────────────────────────────────────
            // Qt QSettings INI format; Prism reads the [General] section for the display name,
            // memory overrides, and custom JVM args.
            progress?.Report("Building instance.cfg…");
            var cfgLines = new List<string>
            {
                "[General]",
                $"name={installation.Name}",
                "iconKey=default",
                "instanceType=OneSix"
            };
            if (installation.MaxMemoryMB.HasValue && installation.MaxMemoryMB > 0)
            {
                cfgLines.Add("OverrideMemory=true");
                cfgLines.Add($"MaxMemAlloc={installation.MaxMemoryMB}");
                if (installation.MinMemoryMB.HasValue && installation.MinMemoryMB > 0)
                    cfgLines.Add($"MinMemAlloc={installation.MinMemoryMB}");
            }
            if (!string.IsNullOrWhiteSpace(installation.CustomJvmArgs))
            {
                cfgLines.Add("OverrideJavaArgs=true");
                cfgLines.Add($"JvmArgs={installation.CustomJvmArgs}");
            }
            await File.WriteAllTextAsync(Path.Combine(workDir, "instance.cfg"), string.Join(Environment.NewLine, cfgLines));

            // ── minecraft/ game folder ────────────────────────────────────────────────
            // Prism expects the game data under "minecraft/" (not ".minecraft/").
            progress?.Report("Packing mods…");
            var instanceDir = AppPaths.InstanceDir(installation.Id);
            var mcDir = Path.Combine(workDir, "minecraft");
            Directory.CreateDirectory(mcDir);

            // Copy mods — every jar goes in as a physical file (no external URLs, unlike
            // .mrpack) so the import works offline and requires no re-download.
            var modsDir = Path.Combine(instanceDir, "mods");
            if (Directory.Exists(modsDir))
            {
                var destModsDir = Path.Combine(mcDir, "mods");
                result.ModsIncludedAsOverrides += CopyDirectory(modsDir, destModsDir);
                result.ModsIncludedAsDownloads = result.ModsIncludedAsOverrides; // re-use field for reporting
            }

            // Copy config/resourcepacks/shaderpacks + options.txt.
            progress?.Report("Packing config, resourcepacks and shaderpacks…");
            foreach (var folder in OverrideFolders)
            {
                var source = Path.Combine(instanceDir, folder);
                if (Directory.Exists(source))
                    CopyDirectory(source, Path.Combine(mcDir, folder));
            }
            var optionsFile = Path.Combine(instanceDir, "options.txt");
            if (File.Exists(optionsFile))
                File.Copy(optionsFile, Path.Combine(mcDir, "options.txt"), overwrite: true);

            // ── zip it up ─────────────────────────────────────────────────────────────
            progress?.Report("Writing Prism zip…");
            if (!outputFilePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                outputFilePath += ".zip";
            if (File.Exists(outputFilePath))
                File.Delete(outputFilePath);
            ZipFile.CreateFromDirectory(workDir, outputFilePath, CompressionLevel.Optimal, includeBaseDirectory: false);

            result.Success = true;
            result.FilePath = outputFilePath;
            result.Summary = $"Exported \"{installation.Name}\" as a Prism Launcher instance zip — " +
                              $"{result.ModsIncludedAsOverrides} mod(s) included. " +
                              "Import it in Prism via Add Instance → Import from zip (or drag it onto the window).";

            _log.Log("Modpack", $"Exported \"{installation.Name}\" (Prism native) to {outputFilePath} ({result.ModsIncludedAsOverrides} mods).");
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            result.Summary = $"Export failed: {ex.Message}";
            _log.LogError("Modpack", "Prism-native export failed", ex);
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch { /* best effort */ }
        }

        return result;
    }

    public async Task<ModpackImportResult> ImportAsync(string filePath, IProgress<string>? progress = null)
    {
        var result = new ModpackImportResult();
        var extractDir = Path.Combine(Path.GetTempPath(), "MinecraftControlHub-import-" + Guid.NewGuid());

        try
        {
            progress?.Report("Reading modpack…");
            Directory.CreateDirectory(extractDir);
            ZipFile.ExtractToDirectory(filePath, extractDir);

            // Real Prism Launcher exports (mmc-pack.json) and .mrpack files both put
            // their marker file at the zip root - but some tools wrap everything in one
            // extra top-level folder first, so check one level deep too before giving up.
            var nativeRoot = FindPackRoot(extractDir, "mmc-pack.json");
            var mrpackRoot = nativeRoot == null ? FindPackRoot(extractDir, "modrinth.index.json") : null;

            if (nativeRoot != null)
            {
                await ImportPrismNativeAsync(nativeRoot, filePath, result, progress);
            }
            else if (mrpackRoot != null)
            {
                await ImportMrpackAsync(mrpackRoot, filePath, result, progress);
            }
            else
            {
                result.Error = "Unrecognized modpack format - expected either a Prism Launcher instance export (contains \"mmc-pack.json\") or a Modrinth .mrpack (contains \"modrinth.index.json\").";
                result.Summary = result.Error;
                return result;
            }

            if (result.Success)
                _log.Log("Modpack", $"Imported \"{result.Installation?.Name}\" from {filePath} ({result.ModsDownloaded} mods, {result.OverrideFilesCopied} override files).");
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            result.Summary = $"Import failed: {ex.Message}";
            _log.LogError("Modpack", "Import failed", ex);
        }
        finally
        {
            try { Directory.Delete(extractDir, recursive: true); } catch { /* best effort */ }
        }

        return result;
    }

    /// <summary>
    /// Imports the Modrinth Modpack Format (.mrpack) - modrinth.index.json declares
    /// mods as external downloads (by hash + URL), with everything else (config,
    /// resourcepacks, shaderpacks, side-loaded mods) sitting verbatim in "overrides/".
    /// </summary>
    private async Task ImportMrpackAsync(string root, string sourceFilePath, ModpackImportResult result, IProgress<string>? progress)
    {
        var manifestPath = Path.Combine(root, "modrinth.index.json");
        var manifestJson = await File.ReadAllTextAsync(manifestPath);
        var index = JsonSerializer.Deserialize<ModrinthModpackIndex>(manifestJson, ManifestJsonOptions);
        if (index == null)
        {
            result.Error = "Could not parse the modpack's modrinth.index.json.";
            result.Summary = result.Error;
            return;
        }

        var minecraftVersion = index.Dependencies.GetValueOrDefault("minecraft", string.Empty);
        var loader = LoaderFromDependencies(index.Dependencies, out var loaderVersion);

        var installation = new Installation
        {
            Name = string.IsNullOrWhiteSpace(index.Name) ? Path.GetFileNameWithoutExtension(sourceFilePath) : index.Name,
            MinecraftVersion = minecraftVersion,
            Loader = loader,
            LoaderVersion = loaderVersion
        };

        // CreateInstallationAsync assigns a fresh Id and calls EnsureIsolatedLayout,
        // which already creates the mods/resourcepacks/saves/config/shaderpacks folders.
        installation = await _installationService.CreateInstallationAsync(installation);
        var instanceDir = AppPaths.InstanceDir(installation.Id);

        // Download every declared file straight into the isolated instance folder.
        progress?.Report($"Downloading {index.Files.Count} file(s)…");
        foreach (var file in index.Files)
        {
            var relativePath = NormalizeAndValidatePath(file.Path);
            if (relativePath == null)
            {
                result.Warnings.Add($"Skipped a file with an unsafe path: \"{file.Path}\".");
                continue;
            }

            var destPath = Path.Combine(instanceDir, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

            byte[]? bytes = null;
            foreach (var url in file.Downloads)
            {
                try
                {
                    bytes = await _modrinthClient.DownloadModAsync(url);
                    break;
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Download failed for {relativePath} from {url}: {ex.Message}");
                }
            }

            if (bytes == null)
            {
                result.Warnings.Add($"Could not download {relativePath} — skipped.");
                continue;
            }

            await File.WriteAllBytesAsync(destPath, bytes);

            // Register mods as proper Mod records with full Modrinth metadata when
            // possible (via the same hash lookup the update/dependency checks use),
            // so an imported installation gets full update-checking support too —
            // not just bare untracked files.
            var normalizedForward = relativePath.Replace('\\', '/');
            if (normalizedForward.StartsWith("mods/", StringComparison.OrdinalIgnoreCase) &&
                normalizedForward.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
            {
                var sha1 = file.Hashes.GetValueOrDefault("sha1");
                ModVersion? version = !string.IsNullOrEmpty(sha1)
                    ? await _modrinthClient.GetVersionByHashAsync(sha1)
                    : null;

                var mod = new Mod
                {
                    Name = version?.Name ?? Path.GetFileNameWithoutExtension(relativePath),
                    ModrinthId = version?.ModrinthId,
                    VersionId = version?.Id,
                    Version = version?.VersionNumber,
                    MinecraftVersion = version?.MinecraftVersion ?? minecraftVersion,
                    Loader = loader,
                    DownloadUrl = file.Downloads.FirstOrDefault(),
                    FileName = Path.GetFileName(relativePath),
                    FilePath = destPath,
                    FileSize = file.FileSize,
                    Sha1Hash = sha1,
                    Sha512Hash = file.Hashes.GetValueOrDefault("sha512"),
                    IsInstalled = true
                };

                await _modService.InstallModAsync(installation.Id, mod);
                result.ModsDownloaded++;
            }
        }

        // Copy overrides/ (and client-overrides/) verbatim into the instance folder —
        // config, resourcepacks, shaderpacks, options.txt, manually side-loaded mods.
        // server-overrides is intentionally skipped: this app only ever launches a client.
        progress?.Report("Applying overrides…");
        foreach (var overrideFolderName in new[] { "overrides", "client-overrides" })
        {
            var overridesSource = Path.Combine(root, overrideFolderName);
            if (Directory.Exists(overridesSource))
                result.OverrideFilesCopied += CopyDirectory(overridesSource, instanceDir);
        }

        result.Success = true;
        result.Installation = installation;
        result.Summary = $"Imported \"{installation.Name}\" — {result.ModsDownloaded} mod(s) downloaded" +
                          (result.OverrideFilesCopied > 0 ? $", {result.OverrideFilesCopied} extra file(s) applied" : "") +
                          (result.Warnings.Count > 0 ? $" ({result.Warnings.Count} warning(s))" : "") + ".";
    }

    /// <summary>
    /// Imports Prism Launcher's (and MultiMC/PolyMC's) own NATIVE instance export
    /// format - mmc-pack.json (Minecraft version + loader), instance.cfg (name +
    /// memory/JVM overrides), and a "minecraft"/".minecraft" game folder with the
    /// actual mods/config/resourcepacks/shaderpacks. This is what Prism's "Export
    /// Instance" button produces by default, so this is the path that makes a
    /// real-world "exported from Prism" zip actually importable.
    /// </summary>
    private async Task ImportPrismNativeAsync(string root, string sourceFilePath, ModpackImportResult result, IProgress<string>? progress)
    {
        var mmcPackJson = await File.ReadAllTextAsync(Path.Combine(root, "mmc-pack.json"));
        var mmcPack = JsonSerializer.Deserialize<MmcPackFile>(mmcPackJson, ManifestJsonOptions);
        if (mmcPack == null)
        {
            result.Error = "Could not parse the instance's mmc-pack.json.";
            result.Summary = result.Error;
            return;
        }

        var minecraftVersion = mmcPack.Components.FirstOrDefault(c => c.Uid == MmcComponentUid.Minecraft)?.Version ?? string.Empty;
        var (loader, loaderVersion) = LoaderFromMmcComponents(mmcPack.Components);

        // instance.cfg carries the display name plus optional memory/JVM overrides -
        // every key is optional, Prism just omits ones that were never customized.
        var cfg = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var cfgPath = Path.Combine(root, "instance.cfg");
        if (File.Exists(cfgPath))
            cfg = ParseInstanceCfg(await File.ReadAllTextAsync(cfgPath));

        var name = cfg.GetValueOrDefault("name");
        if (string.IsNullOrWhiteSpace(name))
            name = Path.GetFileNameWithoutExtension(sourceFilePath);

        var installation = new Installation
        {
            Name = name,
            MinecraftVersion = minecraftVersion,
            Loader = loader,
            LoaderVersion = loaderVersion
        };

        if (string.Equals(cfg.GetValueOrDefault("OverrideMemory"), "true", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(cfg.GetValueOrDefault("MaxMemAlloc"), out var maxMb)) installation.MaxMemoryMB = maxMb;
            if (int.TryParse(cfg.GetValueOrDefault("MinMemAlloc"), out var minMb)) installation.MinMemoryMB = minMb;
        }
        if (string.Equals(cfg.GetValueOrDefault("OverrideJavaArgs"), "true", StringComparison.OrdinalIgnoreCase))
        {
            var jvmArgs = cfg.GetValueOrDefault("JvmArgs");
            if (!string.IsNullOrWhiteSpace(jvmArgs)) installation.CustomJvmArgs = jvmArgs;
        }

        // CreateInstallationAsync assigns a fresh Id and calls EnsureIsolatedLayout,
        // which already creates the mods/resourcepacks/saves/config/shaderpacks folders.
        installation = await _installationService.CreateInstallationAsync(installation);
        var instanceDir = AppPaths.InstanceDir(installation.Id);

        // The game folder is normally called "minecraft", but ".minecraft" shows up
        // too depending on the export source - accept either.
        var gameDir = new[] { "minecraft", ".minecraft" }
            .Select(n => Path.Combine(root, n))
            .FirstOrDefault(Directory.Exists);

        if (gameDir == null)
        {
            result.Warnings.Add("No \"minecraft\" game folder found in the instance export - only the Minecraft version/loader were imported.");
        }
        else
        {
            // World saves are deliberately skipped, same policy as the .mrpack import -
            // this brings over the installation's *setup*, not a savegame backup.
            progress?.Report("Copying mods, config and resourcepacks…");
            foreach (var folder in new[] { "mods", "config", "resourcepacks", "shaderpacks" })
            {
                var source = Path.Combine(gameDir, folder);
                if (Directory.Exists(source))
                    result.OverrideFilesCopied += CopyDirectory(source, Path.Combine(instanceDir, folder));
            }
            var optionsFile = Path.Combine(gameDir, "options.txt");
            if (File.Exists(optionsFile))
            {
                File.Copy(optionsFile, Path.Combine(instanceDir, "options.txt"), overwrite: true);
                result.OverrideFilesCopied++;
            }

            // Try to enrich every copied mod jar with real Modrinth metadata (via its
            // SHA1 hash) so it gets full update/dependency-check support, exactly like
            // the .mrpack import path - not left as just a bare untracked file.
            progress?.Report("Identifying mods…");
            var modsDir = Path.Combine(instanceDir, "mods");
            if (Directory.Exists(modsDir))
            {
                foreach (var jar in Directory.GetFiles(modsDir, "*.jar"))
                {
                    try
                    {
                        var sha1 = ComputeSha1(jar);
                        var version = await _modrinthClient.GetVersionByHashAsync(sha1);
                        var info = new FileInfo(jar);

                        var mod = new Mod
                        {
                            Name = version?.Name ?? Path.GetFileNameWithoutExtension(jar),
                            ModrinthId = version?.ModrinthId,
                            VersionId = version?.Id,
                            Version = version?.VersionNumber,
                            MinecraftVersion = version?.MinecraftVersion ?? minecraftVersion,
                            Loader = loader,
                            DownloadUrl = version?.DownloadUrl,
                            FileName = Path.GetFileName(jar),
                            FilePath = jar,
                            FileSize = info.Length,
                            Sha1Hash = sha1,
                            Sha512Hash = version?.Sha512Hash,
                            IsInstalled = true
                        };

                        await _modService.InstallModAsync(installation.Id, mod);
                        result.ModsDownloaded++;
                    }
                    catch (Exception ex)
                    {
                        // Not identified on Modrinth (e.g. a manually-built or removed
                        // mod) - it still works fine as a plain file; the Mods tab picks
                        // it up automatically as an untracked mod the first time it loads.
                        result.Warnings.Add($"Could not identify \"{Path.GetFileName(jar)}\" on Modrinth ({ex.Message}) - it was still copied in and will work, just without update-checking metadata.");
                    }
                }
            }
        }

        result.Success = true;
        result.Installation = installation;
        result.Summary = $"Imported \"{installation.Name}\" from a Prism Launcher instance export — {result.ModsDownloaded} mod(s) added" +
                          (result.OverrideFilesCopied > 0 ? $", {result.OverrideFilesCopied} file(s) copied" : "") +
                          (result.Warnings.Count > 0 ? $" ({result.Warnings.Count} warning(s))" : "") + ".";
    }



    private static string? LoaderDependencyKey(LoaderType loader) => loader switch
    {
        LoaderType.Fabric => "fabric-loader",
        LoaderType.Quilt => "quilt-loader",
        LoaderType.Forge => "forge",
        LoaderType.NeoForge => "neoforge",
        _ => null
    };


    /// <summary>Maps our <see cref="LoaderType"/> to Prism/MultiMC's component UID string.</summary>
    private static string? LoaderToMmcUid(LoaderType loader) => loader switch
    {
        LoaderType.Fabric  => MmcComponentUid.Fabric,
        LoaderType.Quilt   => MmcComponentUid.Quilt,
        LoaderType.Forge   => MmcComponentUid.Forge,
        LoaderType.NeoForge => MmcComponentUid.NeoForge,
        _ => null
    };

    /// <summary>Reads whichever loader dependency key is present back into our <see cref="LoaderType"/> + pinned version.</summary>
    private static LoaderType LoaderFromDependencies(Dictionary<string, string> dependencies, out string? loaderVersion)
    {
        foreach (var (key, type) in new (string Key, LoaderType Type)[]
                 {
                     ("fabric-loader", LoaderType.Fabric),
                     ("quilt-loader", LoaderType.Quilt),
                     ("neoforge", LoaderType.NeoForge),
                     ("forge", LoaderType.Forge)
                 })
        {
            if (dependencies.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v))
            {
                loaderVersion = v;
                return type;
            }
        }

        loaderVersion = null;
        return LoaderType.Vanilla;
    }

    /// <summary>
    /// Mirrors the mrpack spec's own security requirement: a file's destination path
    /// must stay inside the instance directory. Rejects "..' segments and
    /// absolute/drive paths before ever combining them onto a real folder.
    /// </summary>
    private static string? NormalizeAndValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        var normalized = path.Replace('\\', '/').TrimStart('/');
        if (normalized.Contains("..")) return null;
        if (Regex.IsMatch(normalized, @"^[A-Za-z]:")) return null;

        return normalized.Replace('/', Path.DirectorySeparatorChar);
    }

    private static int CopyDirectory(string sourceDir, string destDir)
    {
        var count = 0;
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(sourceDir, file);
            var dest = Path.Combine(destDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
            count++;
        }
        return count;
    }

    /// <summary>
    /// Finds the folder inside <paramref name="extractDir"/> that directly contains
    /// <paramref name="markerFileName"/> - usually the extract root itself, but some
    /// exports wrap everything in one extra top-level folder first (e.g.
    /// "MyInstance/mmc-pack.json" instead of "mmc-pack.json" at the very root).
    /// </summary>
    private static string? FindPackRoot(string extractDir, string markerFileName)
    {
        if (File.Exists(Path.Combine(extractDir, markerFileName)))
            return extractDir;

        foreach (var dir in Directory.GetDirectories(extractDir))
        {
            if (File.Exists(Path.Combine(dir, markerFileName)))
                return dir;
        }

        return null;
    }

    /// <summary>
    /// Parses Prism/MultiMC's instance.cfg: a flat "key=value" file (Qt QSettings INI
    /// format) that may start with a "[General]" section header - safely ignored since
    /// everything the app cares about lives in that one section anyway.
    /// </summary>
    private static Dictionary<string, string> ParseInstanceCfg(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim().TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith('[') || line.StartsWith(';') || line.StartsWith('#'))
                continue;

            var eq = line.IndexOf('=');
            if (eq <= 0) continue;

            result[line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }
        return result;
    }

    private static string ComputeSha1(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha1 = System.Security.Cryptography.SHA1.Create();
        var hash = sha1.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Maps an mmc-pack.json component list to our <see cref="LoaderType"/> + pinned version.</summary>
    private static (LoaderType Loader, string? LoaderVersion) LoaderFromMmcComponents(List<MmcPackComponent> components)
    {
        foreach (var (uid, type) in new (string Uid, LoaderType Type)[]
                 {
                     (MmcComponentUid.Fabric, LoaderType.Fabric),
                     (MmcComponentUid.Quilt, LoaderType.Quilt),
                     (MmcComponentUid.NeoForge, LoaderType.NeoForge),
                     (MmcComponentUid.Forge, LoaderType.Forge)
                 })
        {
            var match = components.FirstOrDefault(c => c.Uid == uid);
            if (match != null && !string.IsNullOrWhiteSpace(match.Version))
                return (type, match.Version);
        }

        return (LoaderType.Vanilla, null);
    }
}
