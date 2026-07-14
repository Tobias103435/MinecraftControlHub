using System.IO;
using System.Text.Json;
using MinecraftControlHub.Core.Models;

namespace MinecraftControlHub.Core.Services;

public interface IModService
{
    Task<List<Mod>> GetInstalledModsAsync(Guid installationId);
    Task<Mod?> GetModAsync(Guid id);
    Task InstallModAsync(Guid installationId, Mod mod);
    Task UninstallModAsync(Guid installationId, Guid modId);
    Task<ModSearchPage> SearchModsAsync(string? query, string? minecraftVersion = null, LoaderType? loader = null, int offset = 0, int limit = 20, string index = "downloads");
    Task<ModDetail?> GetModDetailAsync(string modrinthId);
    Task<List<ModVersion>> GetModVersionsAsync(string modrinthId, string? minecraftVersion = null, LoaderType? loader = null);

    /// <summary>
    /// Downloads the given mod's compatible file to the installation's mods folder and
    /// automatically resolves + installs any missing required dependencies.
    /// </summary>
    Task<ModInstallResult> InstallModFromSearchAsync(Installation installation, ModSearchResult searchResult);

    /// <summary>
    /// Checks the installed mods of the given installations for newer Modrinth versions.
    /// When <paramref name="applyUpdates"/> is true, newer versions are downloaded and swapped in.
    /// </summary>
    Task<ModUpdateCheckResult> CheckForUpdatesAsync(IEnumerable<Installation> installations, bool applyUpdates);

    /// <summary>
    /// Flags each installed mod of the given installation with whether a newer
    /// compatible Modrinth version exists (without downloading anything).
    /// </summary>
    Task RefreshUpdateStatusAsync(Installation installation);

    /// <summary>
    /// Downloads the newest compatible version of a single installed mod and swaps
    /// it in place. Returns true when an update was applied.
    /// </summary>
    Task<bool> UpdateModAsync(Installation installation, Guid modId);

    /// <summary>
    /// Downloads a SPECIFIC chosen version of an already-installed mod and swaps it
    /// in place, regardless of whether it's newer or older than what's installed —
    /// used by the version-picker dropdown in the mod list.
    /// </summary>
    Task<bool> ChangeModVersionAsync(Installation installation, Guid modId, ModVersion targetVersion);

    /// <summary>
    /// Scans every installed mod's declared required dependencies and checks that
    /// each one is (a) actually installed and (b) installed at a version compatible
    /// with this installation's Minecraft version/loader — neither too old nor too new.
    /// </summary>
    Task<DependencyCheckResult> CheckDependencyCompatibilityAsync(Installation installation);

    /// <summary>
    /// Applies the fix for a single <see cref="DependencyIssue"/>: installs the
    /// missing dependency, or swaps an incompatible one to the best compatible version.
    /// </summary>
    Task<bool> FixDependencyIssueAsync(Installation installation, DependencyIssue issue);

    /// <summary>Resolves (and creates) the on-disk mods folder for an installation, so
    /// the UI can offer an "open folder" shortcut for manually dropping in mod jars.</summary>
    string GetModsFolderPath(Installation installation);

    /// <summary>
    /// Toggles a mod between enabled (.jar) and disabled (.jar.disabled) by renaming
    /// the file on disk. Persists the updated IsEnabled flag in the installation's mod list.
    /// </summary>
    Task<bool> ToggleModEnabledAsync(Installation installation, Guid modId);

    /// <summary>
    /// Searches for mods across one or both platforms (Modrinth / CurseForge).
    /// When <paramref name="source"/> is null, both platforms are queried in parallel
    /// and results are merged (duplicates removed, Modrinth preferred).
    /// </summary>
    Task<ModSearchPage> SearchUnifiedAsync(string? query, string? minecraftVersion, LoaderType? loader, ModSource? source, int offset = 0, int limit = 20, string index = "downloads");

    /// <summary>Fetches the detail view for a CurseForge mod (screenshots, description, etc.).</summary>
    Task<ModDetail?> GetCfModDetailAsync(int curseForgeId);
}

public class ModService : IModService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly IModrinthApiClient _modrinthClient;
    private readonly ICurseForgeApiClient _curseForgeClient;
    private readonly IAppLogService _log;
    private readonly Dictionary<Guid, List<Mod>> _installationMods;

    public ModService(IModrinthApiClient modrinthClient, ICurseForgeApiClient curseForgeClient, IAppLogService log)
    {
        _modrinthClient = modrinthClient;
        _curseForgeClient = curseForgeClient;
        _log = log;
        _installationMods = LoadInstalledMods();
    }

    private static Dictionary<Guid, List<Mod>> LoadInstalledMods()
    {
        try
        {
            if (File.Exists(AppPaths.InstalledModsFile))
            {
                var json = File.ReadAllText(AppPaths.InstalledModsFile);
                var data = JsonSerializer.Deserialize<Dictionary<Guid, List<Mod>>>(json, JsonOptions);
                if (data != null)
                    return data;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Installed mods load failed: {ex.Message}");
        }
        return new Dictionary<Guid, List<Mod>>();
    }

    private void PersistInstalledMods()
    {
        try
        {
            File.WriteAllText(AppPaths.InstalledModsFile, JsonSerializer.Serialize(_installationMods, JsonOptions));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Installed mods save failed: {ex.Message}");
        }
    }

    public Task<List<Mod>> GetInstalledModsAsync(Guid installationId)
    {
        if (!_installationMods.TryGetValue(installationId, out var mods))
        {
            mods = new List<Mod>();
            _installationMods[installationId] = mods;
        }

        // Merge in any .jar files that physically exist in the installation's mods
        // folder but aren't tracked yet (e.g. mods imported from the official
        // launcher, or files copied in manually). This makes the Installed tab
        // reflect the actual contents of the mods folder.
        SyncFromDisk(installationId, mods);

        return Task.FromResult(mods);
    }

    /// <summary>
    /// Adds untracked .jar files found on disk to the in-memory installed list so
    /// the UI shows the real contents of the installation's mods folder.
    /// </summary>
    private void SyncFromDisk(Guid installationId, List<Mod> mods)
    {
        try
        {
            var modsDir = Path.Combine(AppPaths.InstanceDir(installationId), "mods");
            if (!Directory.Exists(modsDir))
                return;

            var tracked = new HashSet<string>(
                mods.Where(m => !string.IsNullOrEmpty(m.FileName)).Select(m => m.FileName!),
                StringComparer.OrdinalIgnoreCase);

            var changed = false;
            foreach (var jar in Directory.GetFiles(modsDir, "*.jar"))
            {
                var fileName = Path.GetFileName(jar);
                if (tracked.Contains(fileName))
                    continue;

                var info = new FileInfo(jar);
                mods.Add(new Mod
                {
                    Id = Guid.NewGuid(),
                    Name = Path.GetFileNameWithoutExtension(jar),
                    FileName = fileName,
                    FilePath = jar,
                    FileSize = info.Length,
                    IsInstalled = true,
                    InstalledAt = info.LastWriteTimeUtc
                });
                changed = true;
            }

            if (changed)
                PersistInstalledMods();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Mods disk sync failed: {ex.Message}");
        }
    }

    public Task<Mod?> GetModAsync(Guid id)
    {
        foreach (var modList in _installationMods.Values)
        {
            var mod = modList.FirstOrDefault(m => m.Id == id);
            if (mod != null)
                return Task.FromResult<Mod?>(mod);
        }
        return Task.FromResult<Mod?>(null);
    }

    public Task InstallModAsync(Guid installationId, Mod mod)
    {
        if (!_installationMods.ContainsKey(installationId))
        {
            _installationMods[installationId] = new List<Mod>();
        }

        if (mod.Id == Guid.Empty)
            mod.Id = Guid.NewGuid();
        mod.InstalledAt = DateTime.UtcNow;
        mod.IsInstalled = true;

        _installationMods[installationId].Add(mod);
        PersistInstalledMods();
        return Task.CompletedTask;
    }

    public Task UninstallModAsync(Guid installationId, Guid modId)
    {
        if (_installationMods.TryGetValue(installationId, out var mods))
        {
            var mod = mods.FirstOrDefault(m => m.Id == modId);
            if (mod != null)
            {
                mods.Remove(mod);
                // Delete the downloaded file from disk if it still exists.
                if (!string.IsNullOrEmpty(mod.FilePath) && File.Exists(mod.FilePath))
                {
                    try { File.Delete(mod.FilePath); }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Delete failed: {ex.Message}"); }
                }
                PersistInstalledMods();
            }
        }
        return Task.CompletedTask;
    }

    public async Task<ModSearchPage> SearchModsAsync(string? query, string? minecraftVersion = null, LoaderType? loader = null, int offset = 0, int limit = 20, string index = "downloads")
    {
        return await _modrinthClient.SearchModsAsync(query, minecraftVersion, loader, offset, limit, index);
    }

    public async Task<ModSearchPage> SearchUnifiedAsync(string? query, string? minecraftVersion, LoaderType? loader, ModSource? source, int offset = 0, int limit = 20, string index = "downloads")
    {
        if (source == ModSource.Modrinth)
            return await _modrinthClient.SearchModsAsync(query, minecraftVersion, loader, offset, limit, index);

        if (source == ModSource.CurseForge)
            return await _curseForgeClient.SearchModsAsync(query, minecraftVersion, loader, offset, limit);

        // Both: fire parallel requests, merge, deduplicate by name (prefer Modrinth)
        var modrinthTask = _modrinthClient.SearchModsAsync(query, minecraftVersion, loader, offset, limit, index);
        var cfTask = _curseForgeClient.SearchModsAsync(query, minecraftVersion, loader, offset, limit);
        await Task.WhenAll(modrinthTask, cfTask);

        var modrinthPage = await modrinthTask;
        var cfPage = await cfTask;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var merged = new List<ModSearchResult>();

        // Modrinth first (preferred for better dependency metadata)
        foreach (var hit in modrinthPage.Hits)
        {
            if (seen.Add(hit.Name))
                merged.Add(hit);
        }
        foreach (var hit in cfPage.Hits)
        {
            if (seen.Add(hit.Name))
                merged.Add(hit);
        }

        return new ModSearchPage
        {
            Hits = merged,
            Offset = offset,
            Limit = limit,
            TotalHits = modrinthPage.TotalHits + cfPage.TotalHits
        };
    }

    public async Task<ModDetail?> GetModDetailAsync(string modrinthId)
    {
        return await _modrinthClient.GetModDetailAsync(modrinthId);
    }

    public async Task<ModDetail?> GetCfModDetailAsync(int curseForgeId)
    {
        return await _curseForgeClient.GetModDetailAsync(curseForgeId);
    }

    public async Task<List<ModVersion>> GetModVersionsAsync(string modrinthId, string? minecraftVersion = null, LoaderType? loader = null)
    {
        return await _modrinthClient.GetModVersionsAsync(modrinthId, minecraftVersion, loader);
    }

    public async Task<ModInstallResult> InstallModFromSearchAsync(Installation installation, ModSearchResult searchResult)
    {
        var result = new ModInstallResult();

        // Resolve the best compatible version for this installation from the correct source.
        ModVersion? version;
        if (searchResult.Source == ModSource.CurseForge && searchResult.CurseForgeId.HasValue)
        {
            version = await ResolveCfBestVersionAsync(searchResult.CurseForgeId.Value, installation);
        }
        else
        {
            version = await ResolveBestVersionAsync(searchResult.ModrinthId, installation);
        }

        if (version == null || string.IsNullOrEmpty(version.DownloadUrl))
        {
            result.Error = $"No compatible version of \"{searchResult.Name}\" found for Minecraft {installation.MinecraftVersion} ({installation.Loader}).";
            result.Summary = result.Error;
            return result;
        }

        // Track which projects are already present so we never install twice.
        var installed = await GetInstalledModsAsync(installation.Id);
        var known = new HashSet<string>(
            installed.Where(m => !string.IsNullOrEmpty(m.ModrinthId)).Select(m => m.ModrinthId!),
            StringComparer.OrdinalIgnoreCase);
        // Also track CurseForge IDs
        foreach (var m in installed.Where(m => m.CurseForgeId.HasValue))
            known.Add($"cf:{m.CurseForgeId.GetValueOrDefault()}");

        var modsDir = GetModsDirectory(installation);

        // Install the primary mod itself.
        var primaryModrinthId = searchResult.Source == ModSource.CurseForge ? string.Empty : searchResult.ModrinthId;
        var primary = await DownloadAndRegisterAsync(
            installation.Id, modsDir, primaryModrinthId, searchResult.Name,
            searchResult.Description, searchResult.Author, searchResult.IconUrl,
            installation, version, isDependency: false, source: searchResult.Source,
            curseForgeId: searchResult.CurseForgeId);
        result.PrimaryMod = primary;
        result.Success = true;
        known.Add(searchResult.Source == ModSource.CurseForge && searchResult.CurseForgeId.HasValue
            ? $"cf:{searchResult.CurseForgeId.Value}" : searchResult.ModrinthId);

        // Resolve required dependencies breadth-first (skip already-installed / already-visited).
        var queue = new Queue<ModDependency>(version.Dependencies);
        while (queue.Count > 0)
        {
            var dep = queue.Dequeue();

            if (dep.Type == DependencyType.Incompatible)
            {
                if (!string.IsNullOrEmpty(dep.ProjectId) && known.Contains(dep.ProjectId))
                    result.Warnings.Add($"Installed mod may be incompatible with project {dep.ProjectId}.");
                continue;
            }

            // Only auto-install required dependencies.
            if (dep.Type != DependencyType.Required)
                continue;

            var depProjectId = dep.ProjectId;
            ModVersion? depVersion = null;

            if (!string.IsNullOrEmpty(dep.VersionId))
            {
                depVersion = await _modrinthClient.GetVersionByIdAsync(dep.VersionId!);
                depProjectId ??= depVersion?.ModrinthId;
            }

            if (string.IsNullOrEmpty(depProjectId))
            {
                result.Warnings.Add("A required dependency could not be identified and was skipped.");
                continue;
            }

            if (known.Contains(depProjectId))
            {
                result.SkippedAlreadyInstalled.Add(depProjectId);
                continue;
            }

            depVersion ??= await ResolveBestVersionAsync(depProjectId, installation);
            if (depVersion == null || string.IsNullOrEmpty(depVersion.DownloadUrl))
            {
                result.Warnings.Add($"Could not resolve a compatible version for required dependency {depProjectId}.");
                continue;
            }

            // Fetch a friendlier name/icon for the dependency.
            var depProject = await _modrinthClient.GetModAsync(depProjectId);
            var depName = depProject?.Name ?? depProjectId;

            var depMod = await DownloadAndRegisterAsync(
                installation.Id, modsDir, depProjectId, depName,
                depProject?.Description, depProject?.Author, depProject?.IconUrl,
                installation, depVersion, isDependency: true);

            result.InstalledDependencies.Add(depMod);
            known.Add(depProjectId);

            // Recurse into this dependency's own required dependencies.
            foreach (var nested in depVersion.Dependencies)
                queue.Enqueue(nested);
        }

        result.Summary = BuildSummary(result);
        return result;
    }

    public async Task<ModUpdateCheckResult> CheckForUpdatesAsync(IEnumerable<Installation> installations, bool applyUpdates)
    {
        var result = new ModUpdateCheckResult();

        foreach (var installation in installations)
        {
            if (!_installationMods.TryGetValue(installation.Id, out var mods) || mods.Count == 0)
                continue;

            // Snapshot the list because applying updates mutates it.
            foreach (var mod in mods.ToList())
            {
                if (string.IsNullOrEmpty(mod.ModrinthId) && !mod.CurseForgeId.HasValue)
                    continue;

                result.Checked++;

                // Resolve the latest version from the correct source
                ModVersion? latest;
                if (mod.Source == ModSource.CurseForge && mod.CurseForgeId.HasValue)
                    latest = await ResolveCfBestVersionAsync(mod.CurseForgeId.Value, installation);
                else if (!string.IsNullOrEmpty(mod.ModrinthId))
                    latest = await ResolveBestVersionAsync(mod.ModrinthId!, installation);
                else
                    continue;
                if (latest == null || string.IsNullOrEmpty(latest.DownloadUrl))
                    continue;

                // A different version number (and a newer publish date) means an update is available.
                var isNewer = !string.Equals(latest.VersionNumber, mod.Version, StringComparison.OrdinalIgnoreCase);
                if (!isNewer)
                    continue;

                // Respect version pinning: skip updates that target a different MC version.
                if (installation.PinMinecraftVersion &&
                    !string.IsNullOrWhiteSpace(latest.MinecraftVersion) &&
                    !string.Equals(latest.MinecraftVersion, installation.MinecraftVersion,
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                var label = $"{mod.Name}: {mod.Version} → {latest.VersionNumber} ({installation.Name})";
                result.UpdatesAvailable.Add(label);

                if (!applyUpdates)
                    continue;

                // Remove the old file, then download the newer one in place.
                if (!string.IsNullOrEmpty(mod.FilePath) && File.Exists(mod.FilePath))
                {
                    try { File.Delete(mod.FilePath); }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Update delete failed: {ex.Message}"); }
                }

                var modsDir = GetModsDirectory(installation);
                var newFileName = !string.IsNullOrWhiteSpace(latest.FileName)
                    ? latest.FileName!
                    : $"{SanitizeFileName(mod.Name)}.jar";
                var newPath = Path.Combine(modsDir, newFileName);
                var bytes = mod.Source == ModSource.CurseForge
                    ? await _curseForgeClient.DownloadModAsync(latest.DownloadUrl!)
                    : await _modrinthClient.DownloadModAsync(latest.DownloadUrl!);
                await File.WriteAllBytesAsync(newPath, bytes);

                mod.Version = latest.VersionNumber;
                mod.MinecraftVersion = latest.MinecraftVersion ?? installation.MinecraftVersion;
                mod.DownloadUrl = latest.DownloadUrl;
                mod.FileName = newFileName;
                mod.FilePath = newPath;
                mod.FileSize = latest.FileSize;
                mod.Sha1Hash = latest.Sha1Hash;
                mod.Sha512Hash = latest.Sha512Hash;
                mod.InstalledAt = DateTime.UtcNow;

                result.Updated.Add(label);
            }
        }

        if (result.Updated.Count > 0)
            PersistInstalledMods();

        result.Summary = BuildUpdateSummary(result, applyUpdates);
        return result;
    }

    public async Task RefreshUpdateStatusAsync(Installation installation)
    {
        if (!_installationMods.TryGetValue(installation.Id, out var mods) || mods.Count == 0)
            return;

        foreach (var mod in mods)
        {
            mod.UpdateAvailable = false;
            mod.LatestVersion = null;

            if (string.IsNullOrEmpty(mod.ModrinthId) && !mod.CurseForgeId.HasValue)
                continue;

            ModVersion? latest;
            if (mod.Source == ModSource.CurseForge && mod.CurseForgeId.HasValue)
                latest = await ResolveCfBestVersionAsync(mod.CurseForgeId.Value, installation);
            else if (!string.IsNullOrEmpty(mod.ModrinthId))
                latest = await ResolveBestVersionAsync(mod.ModrinthId!, installation);
            else
                continue;

            if (latest == null || string.IsNullOrEmpty(latest.DownloadUrl))
                continue;

            // Same fix as UpdateModAsync below: compare the actual file (hash, falling
            // back to download URL), not the display version number — mods frequently
            // reuse the same version number across different per-Minecraft-version
            // republishes, so a text compare alone can't tell a wrong-MC-version file
            // apart from the correct replacement.
            var sameFile = latest.Sha1Hash != null && mod.Sha1Hash != null
                ? string.Equals(latest.Sha1Hash, mod.Sha1Hash, StringComparison.OrdinalIgnoreCase)
                : string.Equals(latest.DownloadUrl, mod.DownloadUrl, StringComparison.OrdinalIgnoreCase);

            if (!sameFile)
            {
                mod.UpdateAvailable = true;
                mod.LatestVersion = latest.VersionNumber;
            }
        }
    }

    public async Task<bool> UpdateModAsync(Installation installation, Guid modId)
    {
        if (!_installationMods.TryGetValue(installation.Id, out var mods))
            return false;

        var mod = mods.FirstOrDefault(m => m.Id == modId);
        if (mod == null || (string.IsNullOrEmpty(mod.ModrinthId) && !mod.CurseForgeId.HasValue))
            return false;

        _log.Log("Update", $"mod=\"{mod.Name}\" (currently v{mod.Version}, mc={mod.MinecraftVersion}, loader={mod.Loader}) on installation={installation.Name} (mc={installation.MinecraftVersion}, loader={installation.Loader})");

        ModVersion? latest;
        if (mod.Source == ModSource.CurseForge && mod.CurseForgeId.HasValue)
            latest = await ResolveCfBestVersionAsync(mod.CurseForgeId.Value, installation);
        else
            latest = await ResolveBestVersionAsync(mod.ModrinthId!, installation);
        if (latest == null || string.IsNullOrEmpty(latest.DownloadUrl))
        {
            _log.Log("Update", $"  -> no compatible build found for \"{mod.Name}\", nothing to update to.");
            return false;
        }

        // BUGFIX (real symptom: clicking "Fix" on a flagged "wrong Minecraft version"
        // mod just failed with "can't be automatically fixed", even though a perfectly
        // good matching build existed on Modrinth). This used to treat "same version
        // NUMBER" as "already installed, nothing to do" and bail out. But a mod's
        // version number very often does NOT change between per-Minecraft-version
        // republishes (e.g. Sodium/Iris commonly publish "mc1.21" and "mc1.21.1"
        // builds under the exact same version number) — so the currently-installed
        // WRONG-version file and the correct REPLACEMENT file can share the identical
        // version number while being completely different jars. Comparing the file
        // hash (falls back to the download URL) instead of the display version number
        // actually tells the two apart; only skip the download when it's truly the
        // same file already on disk.
        var alreadyInstalled = latest.Sha1Hash != null && mod.Sha1Hash != null
            ? string.Equals(latest.Sha1Hash, mod.Sha1Hash, StringComparison.OrdinalIgnoreCase)
            : string.Equals(latest.DownloadUrl, mod.DownloadUrl, StringComparison.OrdinalIgnoreCase);

        if (alreadyInstalled)
        {
            _log.Log("Update", $"  -> \"{mod.Name}\" is already on the matching build ({latest.FileName}), nothing to do.");
            mod.UpdateAvailable = false;
            mod.LatestVersion = null;
            return false;
        }

        // Remove the old file, then download the newer one in place.
        if (!string.IsNullOrEmpty(mod.FilePath) && File.Exists(mod.FilePath))
        {
            try { File.Delete(mod.FilePath); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Update delete failed: {ex.Message}"); }
        }

        var modsDir = GetModsDirectory(installation);
        var newFileName = !string.IsNullOrWhiteSpace(latest.FileName)
            ? latest.FileName!
            : $"{SanitizeFileName(mod.Name)}.jar";
        var newPath = Path.Combine(modsDir, newFileName);
        var bytes = mod.Source == ModSource.CurseForge
            ? await _curseForgeClient.DownloadModAsync(latest.DownloadUrl!)
            : await _modrinthClient.DownloadModAsync(latest.DownloadUrl!);
        await File.WriteAllBytesAsync(newPath, bytes);

        mod.Version = latest.VersionNumber;
        mod.VersionId = latest.Id;
        mod.MinecraftVersion = latest.MinecraftVersion ?? installation.MinecraftVersion;
        mod.Loader = latest.Loader;
        mod.DownloadUrl = latest.DownloadUrl;
        mod.FileName = newFileName;
        mod.FilePath = newPath;
        mod.FileSize = latest.FileSize;
        mod.Sha1Hash = latest.Sha1Hash;
        mod.Sha512Hash = latest.Sha512Hash;
        mod.InstalledAt = DateTime.UtcNow;
        mod.UpdateAvailable = false;
        mod.LatestVersion = null;

        _log.Log("Update", $"  -> swapped \"{mod.Name}\" to {newFileName} (v{mod.Version}, mc={mod.MinecraftVersion}, loader={mod.Loader}).");

        PersistInstalledMods();
        return true;
    }

    private static string BuildUpdateSummary(ModUpdateCheckResult result, bool applyUpdates)
    {
        if (result.Checked == 0)
            return "No installed mods to check.";
        if (result.UpdatesAvailable.Count == 0)
            return $"All {result.Checked} installed mod(s) are up to date.";
        return applyUpdates
            ? $"Updated {result.Updated.Count} of {result.UpdatesAvailable.Count} mod(s) with newer versions."
            : $"{result.UpdatesAvailable.Count} mod(s) have updates available.";
    }

    private async Task<ModVersion?> ResolveBestVersionAsync(string modrinthId, Installation installation)
    {
        // IMPORTANT: this used to relax down to "same major.minor branch" when no
        // exact-version build existed (e.g. treating a "1.21.1"-only build as fine
        // on a "1.21" instance) — real-world testing proved that assumption unsafe:
        // Minecraft patch releases (1.21 -> 1.21.1 is a good example) regularly ship
        // breaking changes, and NeoForge/Forge bump their own required build right
        // along with them. That relaxed match is exactly what put a 1.21.1-only
        // Sodium/Iris build on a 1.21 instance and crashed the game on launch with a
        // pile of "Missing or unsupported mandatory dependencies" errors. Compatibility
        // now requires an EXACT declared Minecraft version match — never a same-branch
        // guess — and the loader must always match too.
        var mc = installation.MinecraftVersion;
        var loader = installation.Loader;
        _log.Log("Resolve", $"modrinthId={modrinthId}, mc={mc}, loader={loader}");

        // 1) Server-side filtered by exact Minecraft version + loader.
        var exact = await _modrinthClient.GetModVersionsAsync(modrinthId, mc, loader);
        var best = PickBestForVersion(exact, mc);
        if (best != null)
        {
            var tier = best.IsPrerelease ? $"{best.VersionType} (no stable release exists yet for mc={mc})" : "release";
            _log.Log("Resolve", $"  server-side filtered: {exact.Count} version(s) -> selected {best.FileName} [{tier}], game_versions=[{string.Join(", ", best.GameVersions)}]");
            return best;
        }
        _log.Log("Resolve", $"  server-side filtered: {exact.Count} version(s), none matched mc={mc} exactly (checked release and beta)");

        // 2) Fall back to fetching everything for this loader (in case the API's
        // version filter missed a listing) and still require an exact MC match
        // client-side — never widen to a neighboring patch version.
        if (!string.IsNullOrWhiteSpace(mc))
        {
            var sameLoader = await _modrinthClient.GetModVersionsAsync(modrinthId, null, loader);
            best = PickBestForVersion(sameLoader, mc);
            if (best != null)
            {
                var tier = best.IsPrerelease ? $"{best.VersionType} (no stable release exists yet for mc={mc})" : "release";
                _log.Log("Resolve", $"  all-{loader}-versions: {sameLoader.Count} version(s) -> selected {best.FileName} [{tier}], game_versions=[{string.Join(", ", best.GameVersions)}]");
                return best;
            }
            _log.Log("Resolve", $"  all-{loader}-versions: {sameLoader.Count} version(s), still no exact mc={mc} match (checked release and beta)");
        }

        // No compatible build exists for this exact Minecraft version on this loader.
        // Deliberately do NOT fall back further (wrong loader, or a neighboring MC
        // version) — that is what previously produced incompatible/crashing installs.
        _log.Log("Resolve", $"  no compatible build found for modrinthId={modrinthId} on mc={mc}/{loader} (no release, no beta) — giving up rather than guessing.");
        return null;
    }

    /// <summary>
    /// Picks the newest-published version whose declared game_versions actually
    /// contains <paramref name="mc"/> exactly. No "close enough" branch matching —
    /// see the note on <see cref="ResolveBestVersionAsync"/> for why.
    /// Prefers a stable "release" build, but falls back to a "beta" build for that
    /// SAME exact Minecraft version when no release exists yet (common right after a
    /// fresh Minecraft release — e.g. Sodium/Iris often ship only a beta for the very
    /// first build targeting a brand-new Minecraft version). Never falls back to
    /// "alpha", and never widens to a neighboring Minecraft version — if neither a
    /// release nor a beta exists for the exact version, there is genuinely no
    /// compatible build and the caller must surface that instead of guessing.
    /// </summary>
    private static ModVersion? PickBestForVersion(List<ModVersion> versions, string? mc)
    {
        if (string.IsNullOrWhiteSpace(mc))
            return versions.OrderByDescending(v => v.DatePublished).FirstOrDefault();

        var matching = versions
            .Where(v => v.GameVersions.Contains(mc, StringComparer.OrdinalIgnoreCase))
            .ToList();

        var release = matching
            .Where(v => !v.IsPrerelease)
            .OrderByDescending(v => v.DatePublished)
            .FirstOrDefault();
        if (release != null)
            return release;

        return matching
            .Where(v => string.Equals(v.VersionType, "beta", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(v => v.DatePublished)
            .FirstOrDefault();
    }

    private async Task<Mod> DownloadAndRegisterAsync(
        Guid installationId, string modsDir, string modrinthId, string name,
        string? description, string? author, string? iconUrl,
        Installation installation, ModVersion version, bool isDependency,
        ModSource source = ModSource.Modrinth, int? curseForgeId = null)
    {
        var fileName = !string.IsNullOrWhiteSpace(version.FileName)
            ? version.FileName!
            : $"{SanitizeFileName(name)}.jar";
        var filePath = Path.Combine(modsDir, fileName);

        var bytes = source == ModSource.CurseForge
            ? await _curseForgeClient.DownloadModAsync(version.DownloadUrl!)
            : await _modrinthClient.DownloadModAsync(version.DownloadUrl!);
        await File.WriteAllBytesAsync(filePath, bytes);

        var mod = new Mod
        {
            ModrinthId = source == ModSource.Modrinth ? modrinthId : null,
            CurseForgeId = curseForgeId,
            Source = source,
            Name = name,
            Description = description,
            Author = author,
            IconUrl = iconUrl,
            Version = version.VersionNumber,
            VersionId = version.Id,
            MinecraftVersion = version.MinecraftVersion ?? installation.MinecraftVersion,
            Loader = version.Loader,
            DownloadUrl = version.DownloadUrl,
            FileName = fileName,
            FilePath = filePath,
            FileSize = version.FileSize,
            Sha1Hash = version.Sha1Hash,
            Sha512Hash = version.Sha512Hash,
            IsDependency = isDependency,
            IsInstalled = true,
            InstalledAt = DateTime.UtcNow
        };

        await InstallModAsync(installationId, mod);
        return mod;
    }

    /// <summary>
    /// Resolves the best compatible CurseForge file for a given mod and installation.
    /// Same logic as ResolveBestVersionAsync but against the CurseForge files endpoint.
    /// </summary>
    private async Task<ModVersion?> ResolveCfBestVersionAsync(int curseForgeId, Installation installation)
    {
        var mc = installation.MinecraftVersion;
        var loader = installation.Loader;
        _log.Log("ResolveCF", $"curseForgeId={curseForgeId}, mc={mc}, loader={loader}");

        var files = await _curseForgeClient.GetModFilesAsync(curseForgeId, mc, loader);
        var best = PickBestForVersion(files, mc);
        if (best != null)
        {
            _log.Log("ResolveCF", $"  {files.Count} file(s) -> selected {best.FileName}");
            return best;
        }

        // Fallback: fetch all files for this loader (no MC filter), then filter client-side
        if (!string.IsNullOrWhiteSpace(mc))
        {
            var allFiles = await _curseForgeClient.GetModFilesAsync(curseForgeId, null, loader);
            best = PickBestForVersion(allFiles, mc);
            if (best != null)
            {
                _log.Log("ResolveCF", $"  all-loader files: {allFiles.Count} -> selected {best.FileName}");
                return best;
            }
        }

        _log.Log("ResolveCF", $"  no compatible build found for curseForgeId={curseForgeId} on mc={mc}/{loader}");
        return null;
    }

    /// <summary>
    /// Resolves (and creates) the mods folder for an installation. Uses the installation's
    /// configured game directory when set; otherwise a per-installation folder under LocalAppData.
    /// </summary>
    public string GetModsFolderPath(Installation installation) => GetModsDirectory(installation);

    private static string GetModsDirectory(Installation installation)
    {
        var baseDir = !string.IsNullOrWhiteSpace(installation.GameDirectory)
            ? installation.GameDirectory!
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MinecraftControlHub", "installations", installation.Id.ToString());

        var modsDir = Path.Combine(baseDir, "mods");
        Directory.CreateDirectory(modsDir);
        return modsDir;
    }

    public Task<bool> ToggleModEnabledAsync(Installation installation, Guid modId)
    {
        if (!_installationMods.TryGetValue(installation.Id, out var mods))
            return Task.FromResult(false);

        var mod = mods.FirstOrDefault(m => m.Id == modId);
        if (mod == null || string.IsNullOrEmpty(mod.FilePath))
            return Task.FromResult(false);

        var enabledPath  = mod.FilePath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
            ? mod.FilePath[..^9]
            : mod.FilePath;
        var disabledPath = enabledPath + ".disabled";

        try
        {
            if (mod.IsEnabled)
            {
                if (File.Exists(enabledPath))
                    File.Move(enabledPath, disabledPath);
                mod.IsEnabled = false;
                mod.FilePath  = disabledPath;
            }
            else
            {
                if (File.Exists(disabledPath))
                    File.Move(disabledPath, enabledPath);
                mod.IsEnabled = true;
                mod.FilePath  = enabledPath;
            }

            PersistInstalledMods();
            return Task.FromResult(true);
        }
        catch (IOException ex)
        {
            System.Diagnostics.Debug.WriteLine($"ToggleModEnabled failed for {mod.Name}: {ex.Message}");
            return Task.FromResult(false);
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "mod" : cleaned;
    }

    private static string BuildSummary(ModInstallResult result)
    {
        var name = result.PrimaryMod?.Name ?? "Mod";
        var parts = new List<string> { $"Installed {name}" };

        if (result.InstalledDependencies.Count > 0)
        {
            var depNames = string.Join(", ", result.InstalledDependencies.Select(d => d.Name));
            parts.Add($"+ {result.InstalledDependencies.Count} dependency(ies): {depNames}");
        }

        if (result.SkippedAlreadyInstalled.Count > 0)
            parts.Add($"({result.SkippedAlreadyInstalled.Count} dependency(ies) already installed)");

        var summary = string.Join(" ", parts) + ".";
        if (result.Warnings.Count > 0)
            summary += " " + string.Join(" ", result.Warnings);

        return summary;
    }

    public async Task<bool> ChangeModVersionAsync(Installation installation, Guid modId, ModVersion targetVersion)
    {
        if (!_installationMods.TryGetValue(installation.Id, out var mods))
            return false;

        var mod = mods.FirstOrDefault(m => m.Id == modId);
        if (mod == null || string.IsNullOrEmpty(targetVersion.DownloadUrl))
            return false;

        // Remove the old file, then download the chosen one in place — same dance
        // as UpdateModAsync, just for a version the user picked instead of "latest".
        if (!string.IsNullOrEmpty(mod.FilePath) && File.Exists(mod.FilePath))
        {
            try { File.Delete(mod.FilePath); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Version switch delete failed: {ex.Message}"); }
        }

        var modsDir = GetModsDirectory(installation);
        var newFileName = !string.IsNullOrWhiteSpace(targetVersion.FileName)
            ? targetVersion.FileName!
            : $"{SanitizeFileName(mod.Name)}.jar";
        var newPath = Path.Combine(modsDir, newFileName);
        var bytes = mod.Source == ModSource.CurseForge
            ? await _curseForgeClient.DownloadModAsync(targetVersion.DownloadUrl!)
            : await _modrinthClient.DownloadModAsync(targetVersion.DownloadUrl!);
        await File.WriteAllBytesAsync(newPath, bytes);

        mod.Version = targetVersion.VersionNumber;
        mod.MinecraftVersion = targetVersion.MinecraftVersion ?? installation.MinecraftVersion;
        mod.DownloadUrl = targetVersion.DownloadUrl;
        mod.FileName = newFileName;
        mod.FilePath = newPath;
        mod.FileSize = targetVersion.FileSize;
        mod.Sha1Hash = targetVersion.Sha1Hash;
        mod.Sha512Hash = targetVersion.Sha512Hash;
        mod.InstalledAt = DateTime.UtcNow;
        mod.UpdateAvailable = false;
        mod.LatestVersion = null;

        PersistInstalledMods();
        return true;
    }

    public async Task<DependencyCheckResult> CheckDependencyCompatibilityAsync(Installation installation)
    {
        var result = new DependencyCheckResult();
        var installed = await GetInstalledModsAsync(installation.Id);

        // First installed mod wins if (somehow) the same project got installed twice.
        var byProjectId = new Dictionary<string, Mod>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in installed)
        {
            if (!string.IsNullOrEmpty(m.ModrinthId) && !byProjectId.ContainsKey(m.ModrinthId!))
                byProjectId[m.ModrinthId!] = m;
            if (m.CurseForgeId.HasValue)
            {
                var cfKey = m.CurseForgeId.Value.ToString();
                if (!byProjectId.ContainsKey(cfKey))
                    byProjectId[cfKey] = m;
            }
        }

        // Dedupe + cycle guards shared across the WHOLE run (not per top-level mod) —
        // a dependency shared by several mods (e.g. Fabric API) only needs reporting
        // once, and a dependency cycle (A needs B, B needs A) must not loop forever.
        var reportedMissing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var reportedIncompatible = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visitedAsDependency = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var mod in installed)
        {
            if (string.IsNullOrEmpty(mod.ModrinthId) && !mod.CurseForgeId.HasValue)
                continue;

            result.ModsChecked++;

            // Check the mod ITSELF first — not just what it depends on. A mod that was
            // manually switched to a version for the wrong Minecraft release (via the
            // version-picker) won't show up as anyone's "missing dependency", but it is
            // exactly as likely to crash the game on launch, so it must be flagged too.
            var selfCompatible = mod.Loader == installation.Loader
                && !string.IsNullOrWhiteSpace(mod.MinecraftVersion)
                && string.Equals(mod.MinecraftVersion, installation.MinecraftVersion, StringComparison.OrdinalIgnoreCase);

            if (!selfCompatible)
            {
                var selfProjectId = !string.IsNullOrEmpty(mod.ModrinthId)
                    ? mod.ModrinthId!
                    : mod.CurseForgeId?.ToString() ?? string.Empty;
                result.Issues.Add(new DependencyIssue
                {
                    RequiringModName = mod.Name,
                    DependencyProjectId = selfProjectId,
                    DependencyName = mod.Name,
                    Type = DependencyIssueType.IncompatibleVersion,
                    InstalledModId = mod.Id,
                    CurseForgeModId = mod.CurseForgeId,
                    Description = $"{mod.Name} (v{mod.Version}, MC {mod.MinecraftVersion}) isn't compatible with this installation's Minecraft {installation.MinecraftVersion} \u2014 it will likely crash on launch."
                });
            }

            var installedVersion = await ResolveInstalledVersionAsync(mod, installation);
            if (installedVersion == null)
                continue; // Genuinely no compatible build exists to read a dependency list from.

            // Walk this mod's ENTIRE dependency chain — not just what it directly
            // requires, but what THOSE dependencies themselves require too (nested,
            // like a Fabric API submodule three levels deep) — checked against the
            // exact current instance version at every level, same as the standing
            // rule for the top-level mods.
            await CheckDependencyChainAsync(mod.Name, installedVersion, installation, byProjectId,
                reportedMissing, reportedIncompatible, visitedAsDependency, result);
        }

        result.Summary = result.Issues.Count == 0
            ? $"Checked {result.ModsChecked} mod(s) — all dependencies look compatible."
            : $"Checked {result.ModsChecked} mod(s) — found {result.Issues.Count} dependency issue(s).";

        _log.Log("Check", $"installation={installation.Name} (mc={installation.MinecraftVersion}, loader={installation.Loader}): {result.Summary}");
        foreach (var issue in result.Issues)
            _log.Log("Check", $"  issue: {issue.Type} — {issue.Description}");

        return result;
    }

    /// <summary>
    /// Identifies the Modrinth version that matches what's ACTUALLY installed for
    /// <paramref name="mod"/>, so dependency/compat checks read the real dependency
    /// list for this exact build rather than "whatever's newest". Tries progressively
    /// less certain approaches:
    /// 1) The exact version ID recorded at install/update time — Prism Launcher's own
    ///    approach (their local ".index" metadata), zero guessing involved.
    /// 2) The file's SHA1 hash via Modrinth's own "GET /version_file/{hash}" lookup —
    ///    for mods installed before VersionId was tracked, or manually replaced files.
    /// 3) A loader-filtered version list, text-matched by filename/version-number.
    /// 4) Last resort: whatever we'd install today for this exact installation.
    /// </summary>
    private async Task<ModVersion?> ResolveInstalledVersionAsync(Mod mod, Installation installation)
    {
        // CurseForge mods: use CurseForge file lookup
        if (mod.Source == ModSource.CurseForge && mod.CurseForgeId.HasValue)
        {
            // Try fingerprint lookup if we have a hash
            if (!string.IsNullOrEmpty(mod.Sha1Hash) && uint.TryParse(mod.Sha1Hash, out var fp))
            {
                var byFp = await _curseForgeClient.GetFileByFingerprintAsync(fp);
                if (byFp != null)
                    return byFp;
            }

            // Fall back to listing files
            var cfFiles = await _curseForgeClient.GetModFilesAsync(mod.CurseForgeId.Value, null, installation.Loader);
            var cfMatch = cfFiles.FirstOrDefault(v =>
                    !string.IsNullOrEmpty(mod.FileName) && string.Equals(v.FileName, mod.FileName, StringComparison.OrdinalIgnoreCase))
                ?? cfFiles.FirstOrDefault(v =>
                    !string.IsNullOrEmpty(mod.Version) && string.Equals(v.VersionNumber, mod.Version, StringComparison.OrdinalIgnoreCase));
            if (cfMatch != null)
                return cfMatch;

            return await ResolveCfBestVersionAsync(mod.CurseForgeId.Value, installation);
        }

        // Modrinth mods: existing flow
        if (!string.IsNullOrEmpty(mod.VersionId))
        {
            var byId = await _modrinthClient.GetVersionByIdAsync(mod.VersionId!);
            if (byId != null)
                return byId;
        }

        if (!string.IsNullOrEmpty(mod.Sha1Hash))
        {
            var byHash = await _modrinthClient.GetVersionByHashAsync(mod.Sha1Hash!);
            if (byHash != null)
                return byHash;
        }

        if (string.IsNullOrEmpty(mod.ModrinthId))
            return null;

        List<ModVersion> candidates;
        try
        {
            candidates = await _modrinthClient.GetModVersionsAsync(mod.ModrinthId!, null, installation.Loader);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Dependency check: fetching versions for {mod.Name} failed: {ex.Message}");
            candidates = new List<ModVersion>();
        }

        var textMatch = candidates.FirstOrDefault(v =>
                !string.IsNullOrEmpty(mod.FileName) && string.Equals(v.FileName, mod.FileName, StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault(v =>
                !string.IsNullOrEmpty(mod.Version) && string.Equals(v.VersionNumber, mod.Version, StringComparison.OrdinalIgnoreCase));
        if (textMatch != null)
            return textMatch;

        return await ResolveBestVersionAsync(mod.ModrinthId!, installation);
    }

    /// <summary>
    /// Recursively checks a version's "Required" dependencies — and each of THOSE
    /// dependencies' own required dependencies, and so on — against what's actually
    /// installed for this exact instance version. <paramref name="visited"/> guards
    /// against re-walking a dependency shared by multiple mods and against cycles.
    /// </summary>
    private async Task CheckDependencyChainAsync(
        string requiringModName, ModVersion version, Installation installation,
        Dictionary<string, Mod> byProjectId, HashSet<string> reportedMissing,
        HashSet<string> reportedIncompatible, HashSet<string> visited, DependencyCheckResult result)
    {
        foreach (var dep in version.Dependencies)
        {
            if (dep.Type != DependencyType.Required)
                continue;

            var depProjectId = dep.ProjectId;
            if (string.IsNullOrEmpty(depProjectId) && !string.IsNullOrEmpty(dep.VersionId))
            {
                var depVerInfo = await _modrinthClient.GetVersionByIdAsync(dep.VersionId!);
                depProjectId = depVerInfo?.ModrinthId;
            }
            if (string.IsNullOrEmpty(depProjectId))
                continue;

            if (!visited.Add(depProjectId))
                continue; // already walked this project this run — shared dep or a cycle.

            if (!byProjectId.TryGetValue(depProjectId, out var depMod))
            {
                if (reportedMissing.Add(depProjectId))
                {
                    var depProject = await _modrinthClient.GetModAsync(depProjectId);
                    result.Issues.Add(new DependencyIssue
                    {
                        RequiringModName = requiringModName,
                        DependencyProjectId = depProjectId,
                        DependencyName = depProject?.Name ?? depProjectId,
                        Type = DependencyIssueType.Missing,
                        Description = $"{requiringModName} needs \"{depProject?.Name ?? depProjectId}\", which isn't installed."
                    });
                }
                continue; // not installed, so there's no jar to inspect for nested deps.
            }

            // Present, but is THIS installed version actually compatible (same loader +
            // EXACT same Minecraft version) with the installation? Catches both
            // directions — a dependency build too old, and one too new. No "close
            // enough" branch matching — see the note on ResolveBestVersionAsync.
            var compatible = depMod.Loader == installation.Loader
                && !string.IsNullOrWhiteSpace(depMod.MinecraftVersion)
                && string.Equals(depMod.MinecraftVersion, installation.MinecraftVersion, StringComparison.OrdinalIgnoreCase);

            if (!compatible && reportedIncompatible.Add(depProjectId))
            {
                result.Issues.Add(new DependencyIssue
                {
                    RequiringModName = requiringModName,
                    DependencyProjectId = depProjectId,
                    DependencyName = depMod.Name,
                    Type = DependencyIssueType.IncompatibleVersion,
                    InstalledModId = depMod.Id,
                    Description = $"{depMod.Name} (v{depMod.Version}, MC {depMod.MinecraftVersion}) isn't compatible with {installation.MinecraftVersion} — needed by {requiringModName}."
                });
            }

            // NESTED: now check what THIS dependency itself requires — this is what
            // catches a chain like "Iris needs Sodium, Sodium needs Fabric API 0.100+"
            // instead of stopping after the first hop.
            var depInstalledVersion = await ResolveInstalledVersionAsync(depMod, installation);
            if (depInstalledVersion != null)
            {
                await CheckDependencyChainAsync(depMod.Name, depInstalledVersion, installation, byProjectId,
                    reportedMissing, reportedIncompatible, visited, result);
            }
        }
    }

    public async Task<bool> FixDependencyIssueAsync(Installation installation, DependencyIssue issue)
    {
        _log.Log("Fix", $"issue Type={issue.Type}, dependency=\"{issue.DependencyName}\" ({issue.DependencyProjectId}), requiredBy=\"{issue.RequiringModName}\", installation={installation.Name} (mc={installation.MinecraftVersion}, loader={installation.Loader})");

        if (issue.Type == DependencyIssueType.IncompatibleVersion && issue.InstalledModId.HasValue)
        {
            bool updated;
            try
            {
                updated = await UpdateModAsync(installation, issue.InstalledModId.Value);
            }
            catch (Exception ex)
            {
                _log.LogError("Fix", $"UpdateModAsync threw while fixing \"{issue.DependencyName}\"", ex);
                throw;
            }
            _log.Log("Fix", updated
                ? $"  -> updated \"{issue.DependencyName}\" to a compatible build."
                : $"  -> UpdateModAsync returned false for \"{issue.DependencyName}\" (no compatible build found, or already up to date).");
            return updated;
        }

        if (issue.Type == DependencyIssueType.Missing)
        {
            // Try Modrinth first
            ModVersion? version = null;
            ModSource depSource = ModSource.Modrinth;
            int? cfModId = issue.CurseForgeModId;

            if (!string.IsNullOrEmpty(issue.DependencyProjectId))
            {
                version = await ResolveBestVersionAsync(issue.DependencyProjectId, installation);
            }

            // Fall back to CurseForge if Modrinth didn't yield a result
            if ((version == null || string.IsNullOrEmpty(version.DownloadUrl)) && cfModId.HasValue)
            {
                version = await ResolveCfBestVersionAsync(cfModId.Value, installation);
                depSource = ModSource.CurseForge;
            }

            // If still no version and no CF ID known, search CurseForge by name
            if (version == null || string.IsNullOrEmpty(version.DownloadUrl))
            {
                var cfSearch = await _curseForgeClient.SearchModsAsync(
                    issue.DependencyName, installation.MinecraftVersion, installation.Loader, 0, 5);
                var cfMatch = cfSearch.Hits.FirstOrDefault(h =>
                    h.Name.Equals(issue.DependencyName, StringComparison.OrdinalIgnoreCase));
                if (cfMatch?.CurseForgeId != null)
                {
                    version = await ResolveCfBestVersionAsync(cfMatch.CurseForgeId.Value, installation);
                    if (version != null && !string.IsNullOrEmpty(version.DownloadUrl))
                    {
                        depSource = ModSource.CurseForge;
                        cfModId = cfMatch.CurseForgeId;
                    }
                }
            }

            if (version == null || string.IsNullOrEmpty(version.DownloadUrl))
            {
                _log.Log("Fix", $"  -> could not fix: no compatible build of \"{issue.DependencyName}\" exists for mc={installation.MinecraftVersion}/{installation.Loader}.");
                return false;
            }

            string? depName = null, depDesc = null, depAuthor = null, depIcon = null;
            if (depSource == ModSource.Modrinth)
            {
                var depProject = await _modrinthClient.GetModAsync(issue.DependencyProjectId);
                depName = depProject?.Name;
                depDesc = depProject?.Description;
                depAuthor = depProject?.Author;
                depIcon = depProject?.IconUrl;
            }
            else if (cfModId.HasValue)
            {
                var cfDetail = await _curseForgeClient.GetModDetailAsync(cfModId.Value);
                depName = cfDetail?.Name;
                depDesc = cfDetail?.Description;
                depAuthor = cfDetail?.Author;
                depIcon = cfDetail?.IconUrl;
            }

            var modsDir = GetModsDirectory(installation);
            try
            {
                await DownloadAndRegisterAsync(
                    installation.Id, modsDir,
                    depSource == ModSource.Modrinth ? issue.DependencyProjectId : string.Empty,
                    depName ?? issue.DependencyName,
                    depDesc, depAuthor, depIcon,
                    installation, version, isDependency: true,
                    source: depSource, curseForgeId: cfModId);
            }
            catch (Exception ex)
            {
                _log.LogError("Fix", $"Installing missing dependency \"{issue.DependencyName}\" failed", ex);
                throw;
            }
            _log.Log("Fix", $"  -> installed \"{depName ?? issue.DependencyName}\" {version.VersionNumber} ({version.FileName}) as a dependency from {depSource}.");
            return true;
        }

        _log.Log("Fix", $"  -> unhandled issue type {issue.Type}, nothing done.");
        return false;
    }
}
