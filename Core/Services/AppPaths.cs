using System.IO;
using System.Linq;

namespace MinecraftControlHub.Core.Services;

/// <summary>
/// Central place for the application's on-disk locations so every service
/// stores its data under the same, predictable folder.
/// </summary>
public static class AppPaths
{
    /// <summary>
    /// Root data folder: %LocalAppData%\MinecraftControlHub. Created on first use.
    /// </summary>
    public static string DataRoot
    {
        get
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MinecraftControlHub");
            Directory.CreateDirectory(root);
            return root;
        }
    }

    public static string SettingsFile => Path.Combine(DataRoot, "settings.json");
    public static string InstallationsFile => Path.Combine(DataRoot, "installations.json");
    public static string InstalledModsFile => Path.Combine(DataRoot, "installed-mods.json");
    public static string AccountFile       => Path.Combine(DataRoot, "account.json");
    public static string NexoraAccountFile => Path.Combine(DataRoot, "nexora_account.json");

    /// <summary>
    /// Shared Minecraft runtime cache (versions, libraries, assets). These files are
    /// large and version-based, so they are shared across installations to avoid
    /// downloading the same client/libraries/assets multiple times.
    /// </summary>
    public static string MinecraftRoot
    {
        get
        {
            var root = Path.Combine(DataRoot, "minecraft");
            Directory.CreateDirectory(root);
            return root;
        }
    }

    public static string VersionsDir => EnsureDir(Path.Combine(MinecraftRoot, "versions"));
    public static string LibrariesDir => EnsureDir(Path.Combine(MinecraftRoot, "libraries"));
    public static string AssetsDir => EnsureDir(Path.Combine(MinecraftRoot, "assets"));

    /// <summary>
    /// Root folder that holds one isolated game directory per installation
    /// (its own mods, resourcepacks, saves, config, options.txt, ...).
    /// </summary>
    public static string InstancesRoot => EnsureDir(Path.Combine(DataRoot, "instances"));

    /// <summary>The root folder where server folders are created.</summary>
    public static string ServersRoot => EnsureDir(Path.Combine(DataRoot, "Servers"));

    /// <summary>The isolated game directory for a single installation.</summary>
    public static string InstanceDir(Guid installationId) =>
        EnsureDir(Path.Combine(InstancesRoot, installationId.ToString()));

    /// <summary>
    /// Folder holding per-installation launch logs (JVM args + the game's own
    /// stdout/stderr), so a crash on startup can actually be diagnosed afterwards
    /// instead of just disappearing with the closed console window.
    /// </summary>
    public static string LogsDir => EnsureDir(Path.Combine(DataRoot, "logs"));

    /// <summary>
    /// Path of the latest launch log for a given installation. Overwritten on every
    /// launch attempt (only the most recent one is kept per installation).
    /// </summary>
    public static string GameLogFile(Guid installationId, string installationName)
    {
        var safeName = string.Concat(installationName.Select(c => char.IsLetterOrDigit(c) ? c : '-'));
        if (string.IsNullOrWhiteSpace(safeName)) safeName = installationId.ToString();
        return Path.Combine(LogsDir, $"{safeName}-latest.log");
    }

    /// <summary>
    /// App-level diagnostics log (mod install/update/dependency-check decisions —
    /// "Fix" button actions, version resolution, etc). Lives under the same
    /// %LocalAppData%\MinecraftControlHub\logs folder as the game launch logs, so it
    /// survives rebuilds/reinstalls of the app itself — unlike writing next to the
    /// .exe in bin\Debug, which gets wiped on every build.
    /// </summary>
    public static string DiagnosticsLogFile => Path.Combine(LogsDir, "diagnostics.log");

    private static string EnsureDir(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Deletes every file and folder under <see cref="DataRoot"/> (settings,
    /// installations, installed mods, account, and the whole Minecraft/instances
    /// cache), leaving the app as if it had never been run. A restart is required
    /// afterwards because services keep their state in memory.
    /// </summary>
    public static void ClearAllData()
    {
        var root = DataRoot;
        foreach (var file in Directory.GetFiles(root))
        {
            try { File.Delete(file); } catch { /* best effort */ }
        }
        foreach (var dir in Directory.GetDirectories(root))
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// The official Minecraft launcher directory (%APPDATA%\.minecraft) on Windows.
    /// </summary>
    public static string DefaultMinecraftDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        ".minecraft");

    public static string LauncherProfilesFile => Path.Combine(DefaultMinecraftDir, "launcher_profiles.json");
}
