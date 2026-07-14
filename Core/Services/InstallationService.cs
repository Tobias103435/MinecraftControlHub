using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using MinecraftControlHub.Core.Models;

namespace MinecraftControlHub.Core.Services;

public interface IInstallationService
{
    Task<List<Installation>> GetAllInstallationsAsync();
    Task<Installation?> GetInstallationAsync(Guid id);
    Task<Installation> CreateInstallationAsync(Installation installation);
    Task UpdateInstallationAsync(Installation installation);
    Task DeleteInstallationAsync(Guid id);

    /// <summary>
    /// Imports installations from the official Minecraft launcher's
    /// launcher_profiles.json. Returns the number of new installations added.
    /// </summary>
    Task<int> ImportLauncherProfilesAsync();

    /// <summary>True when a local Minecraft launcher profiles file was found.</summary>
    bool LauncherDetected { get; }

    /// <summary>
    /// Raised whenever an installation is created or deleted, so any ViewModel
    /// (e.g. HomePageViewModel) can reload without polling.
    /// </summary>
    event EventHandler? InstallationsChanged;
}

public class InstallationService : IInstallationService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly List<Installation> _installations;

    public bool LauncherDetected => File.Exists(AppPaths.LauncherProfilesFile);

    /// <inheritdoc/>
    public event EventHandler? InstallationsChanged;

    public InstallationService()
    {
        _installations = Load();
        MigrateToIsolatedLayout();
    }

    /// <summary>
    /// Ensures every installation has its own isolated game directory (under
    /// %LocalAppData%\MinecraftControlHub\instances\&lt;id&gt;) with the standard
    /// sub-folders. Older installations that still pointed at the shared
    /// .minecraft folder are migrated, copying any existing .jar mods across so
    /// their mods keep showing up.
    /// </summary>
    private void MigrateToIsolatedLayout()
    {
        var changed = false;
        foreach (var installation in _installations)
        {
            var isolated = AppPaths.InstanceDir(installation.Id);
            if (!string.Equals(installation.GameDirectory, isolated, StringComparison.OrdinalIgnoreCase))
            {
                var previous = installation.GameDirectory;
                EnsureIsolatedLayout(installation, previous);
                changed = true;
            }
            else
            {
                EnsureIsolatedLayout(installation, null);
            }
        }

        if (changed)
            Persist();
    }

    /// <summary>
    /// Creates the isolated game directory + standard sub-folders for an
    /// installation, points its <see cref="Installation.GameDirectory"/> at it,
    /// and (best-effort) copies existing .jar mods from a previous game folder.
    /// </summary>
    public static void EnsureIsolatedLayout(Installation installation, string? copyModsFrom)
    {
        var dir = AppPaths.InstanceDir(installation.Id);
        foreach (var sub in new[] { "mods", "resourcepacks", "saves", "config", "shaderpacks" })
            Directory.CreateDirectory(Path.Combine(dir, sub));

        // Copy any pre-existing mods from a previous (e.g. imported) game folder
        // so the isolated installation keeps showing them.
        if (!string.IsNullOrWhiteSpace(copyModsFrom) &&
            !string.Equals(copyModsFrom, dir, StringComparison.OrdinalIgnoreCase))
        {
            var sourceMods = Path.Combine(copyModsFrom, "mods");
            var targetMods = Path.Combine(dir, "mods");
            if (Directory.Exists(sourceMods))
            {
                try
                {
                    foreach (var jar in Directory.GetFiles(sourceMods, "*.jar"))
                    {
                        var dest = Path.Combine(targetMods, Path.GetFileName(jar));
                        if (!File.Exists(dest))
                            File.Copy(jar, dest);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Mod copy during migration failed: {ex.Message}");
                }
            }
        }

        installation.GameDirectory = dir;
    }

    private static List<Installation> Load()
    {
        try
        {
            if (File.Exists(AppPaths.InstallationsFile))
            {
                var json = File.ReadAllText(AppPaths.InstallationsFile);
                var list = JsonSerializer.Deserialize<List<Installation>>(json, JsonOptions);
                if (list != null)
                    return list;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Installations load failed: {ex.Message}");
        }

        return new List<Installation>();
    }

private static void Save(List<Installation> installations)
    {
        try
        {
            File.WriteAllText(AppPaths.InstallationsFile, JsonSerializer.Serialize(installations, JsonOptions));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Installations save failed: {ex.Message}");
        }
    }

    private void Persist() => Save(_installations);

    public Task<List<Installation>> GetAllInstallationsAsync()
    {
        // BUGFIX: this used to return the internal _installations list BY REFERENCE.
        // HomePageViewModel.Installations uses SetProperty(ref field, value), which
        // checks equality before raising PropertyChanged - and since Add()/Remove() on
        // this service mutate that SAME list in place, the "new" value handed back here
        // was literally the same object reference as what the view model already held.
        // Reference equality made SetProperty think nothing changed, so PropertyChanged
        // never fired and the Home page silently never re-rendered its list after
        // creating/deleting an installation - only navigating away and back (which builds
        // a brand new view model) made it "refresh". Returning a copy means every call
        // hands back a distinct list instance, so the change is always detected.
        return Task.FromResult(_installations.ToList());
    }

    public Task<Installation?> GetInstallationAsync(Guid id)
    {
        var installation = _installations.FirstOrDefault(i => i.Id == id);
        return Task.FromResult(installation);
    }

    public Task<Installation> CreateInstallationAsync(Installation installation)
    {
        installation.Id = Guid.NewGuid();
        installation.CreatedAt = DateTime.UtcNow;
        EnsureIsolatedLayout(installation, null);
        _installations.Add(installation);
        Persist();
        InstallationsChanged?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(installation);
    }

    public Task UpdateInstallationAsync(Installation installation)
    {
        var existing = _installations.FirstOrDefault(i => i.Id == installation.Id);
        if (existing != null)
        {
            var index = _installations.IndexOf(existing);
            _installations[index] = installation;
            Persist();
        }
        return Task.CompletedTask;
    }

    public Task DeleteInstallationAsync(Guid id)
    {
        var installation = _installations.FirstOrDefault(i => i.Id == id);
        if (installation != null)
        {
            _installations.Remove(installation);
            Persist();
            InstallationsChanged?.Invoke(this, EventArgs.Empty);

            // Also remove the isolated instance folder (mods, saves, config, …)
            // so a deleted installation doesn't leave data behind on disk.
            try
            {
                var dir = Path.Combine(AppPaths.InstancesRoot, id.ToString());
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Instance folder delete failed: {ex.Message}");
            }
        }
        return Task.CompletedTask;
    }

    public Task<int> ImportLauncherProfilesAsync()
    {
        if (!File.Exists(AppPaths.LauncherProfilesFile))
            return Task.FromResult(0);

        var added = 0;
        try
        {
            var json = File.ReadAllText(AppPaths.LauncherProfilesFile);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("profiles", out var profiles))
                return Task.FromResult(0);

            foreach (var profile in profiles.EnumerateObject())
            {
                var p = profile.Value;
                var lastVersionId = p.TryGetProperty("lastVersionId", out var lv) ? lv.GetString() : null;
                if (string.IsNullOrWhiteSpace(lastVersionId))
                    continue;

                var name = p.TryGetProperty("name", out var n) && !string.IsNullOrWhiteSpace(n.GetString())
                    ? n.GetString()!
                    : profile.Name;

                var loader = DetectLoader(lastVersionId);
                var mcVersion = ExtractMinecraftVersion(lastVersionId, loader);
                var gameDir = p.TryGetProperty("gameDir", out var gd) && !string.IsNullOrWhiteSpace(gd.GetString())
                    ? gd.GetString()
                    : AppPaths.DefaultMinecraftDir;

                // Skip if we already imported an equivalent installation.
                if (_installations.Any(i =>
                        string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase) &&
                        i.MinecraftVersion == mcVersion &&
                        i.Loader == loader))
                    continue;

                var imported = new Installation
                {
                    Id = Guid.NewGuid(),
                    Name = name,
                    MinecraftVersion = mcVersion,
                    Loader = loader,
                    CreatedAt = DateTime.UtcNow,
                    LastPlayed = p.TryGetProperty("lastUsed", out var lu) && lu.TryGetDateTime(out var dt)
                        ? dt
                        : DateTime.MinValue
                };

                // Give the imported profile its own isolated folder, copying any
                // existing mods from the launcher's game directory across.
                EnsureIsolatedLayout(imported, gameDir);

                _installations.Add(imported);
                added++;
            }

            if (added > 0)
            {
                Persist();
                InstallationsChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Launcher import failed: {ex.Message}");
        }

        return Task.FromResult(added);
    }

    private static LoaderType DetectLoader(string lastVersionId)
    {
        var id = lastVersionId.ToLowerInvariant();
        if (id.Contains("neoforge")) return LoaderType.NeoForge;
        if (id.Contains("forge")) return LoaderType.Forge;
        if (id.Contains("fabric")) return LoaderType.Fabric;
        if (id.Contains("quilt")) return LoaderType.Quilt;
        return LoaderType.Vanilla;
    }

    private static string ExtractMinecraftVersion(string lastVersionId, LoaderType loader)
    {
        var matches = Regex.Matches(lastVersionId, @"\d+\.\d+(?:\.\d+)?");
        if (matches.Count == 0)
            return lastVersionId;

        // Fabric/Quilt encode the MC version last (fabric-loader-x.y.z-<mc>),
        // while Forge/NeoForge encode it first (<mc>-forge-...).
        return loader is LoaderType.Fabric or LoaderType.Quilt
            ? matches[^1].Value
            : matches[0].Value;
    }
}
