using System.Text.Json.Serialization;

namespace MinecraftControlHub.Core.Models;

public enum ModSide
{
    Client,
    Server,
    Both
}

/// <summary>
/// Which platform a mod was sourced from (Modrinth or CurseForge).
/// </summary>
public enum ModSource
{
    Modrinth,
    CurseForge
}

public enum DependencyType
{
    Required,
    Optional,
    Incompatible,
    Embedded
}

public class Mod
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? ModrinthId { get; set; }
    public int? CurseForgeId { get; set; }
    public ModSource Source { get; set; } = ModSource.Modrinth;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Author { get; set; }
    public string? Version { get; set; }
    public string? MinecraftVersion { get; set; }
    public LoaderType Loader { get; set; }
    public ModSide Side { get; set; } = ModSide.Both;
    public string? DownloadUrl { get; set; }
    public string? IconUrl { get; set; }
    public DateTime InstalledAt { get; set; } = DateTime.UtcNow;
    public bool IsInstalled { get; set; }

    /// <summary>
    /// When false the .jar file is renamed to .jar.disabled on disk so the game skips it.
    /// Persisted so the state survives app restarts.
    /// </summary>
    public bool IsEnabled { get; set; } = true;
    public string? FilePath { get; set; }
    public long? FileSize { get; set; }
    public string? Sha1Hash { get; set; }
    public string? Sha512Hash { get; set; }
    public string? FileName { get; set; }

    /// <summary>
    /// The exact version ID this build was installed from (Modrinth version ID or CurseForge file ID).
    /// Recorded at install/update time (the same idea as Prism Launcher's local ".index" metadata:
    /// write down exactly which build this is WHEN you install it, instead of trying to
    /// re-guess it later from the file's hash/name). When present, dependency/compat
    /// checks can look this up directly with zero ambiguity. Mods installed before this
    /// field existed will have it null and fall back to hash/filename matching.
    /// </summary>
    public string? VersionId { get; set; }

    /// <summary>
    /// True when this mod was installed automatically as a required dependency
    /// of another mod rather than being picked directly by the user.
    /// </summary>
    public bool IsDependency { get; set; }

    /// <summary>
    /// Transient (not persisted): true when a newer compatible version exists on
    /// Modrinth for the installation this mod belongs to. Drives the Update button.
    /// </summary>
    [JsonIgnore]
    public bool UpdateAvailable { get; set; }

    /// <summary>
    /// Transient (not persisted): the newer version number available on Modrinth.
    /// </summary>
    [JsonIgnore]
    public string? LatestVersion { get; set; }

    /// <summary>Convenience flag for binding the update button's label.</summary>
    [JsonIgnore]
    public string UpdateLabel =>
        UpdateAvailable && !string.IsNullOrEmpty(LatestVersion)
            ? $"Update to {LatestVersion}"
            : "Update";
}

public class ModSearchResult
{
    public string ModrinthId { get; set; } = string.Empty;
    public int? CurseForgeId { get; set; }
    public ModSource Source { get; set; } = ModSource.Modrinth;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Author { get; set; }
    public string? IconUrl { get; set; }
    public int Downloads { get; set; }
    public DateTime? DateModified { get; set; }
    public List<string> Categories { get; set; } = new();

    /// <summary>
    /// True when this project is already installed in the currently selected
    /// installation. Drives the "Installed" / disabled state of the Install button.
    /// </summary>
    public bool IsInstalled { get; set; }

    /// <summary>
    /// When false the .jar file is renamed to .jar.disabled on disk so the game skips it.
    /// Persisted so the state survives app restarts.
    /// </summary>
    public bool IsEnabled { get; set; } = true;
}

public class ModSearchPage
{
    public List<ModSearchResult> Hits { get; set; } = new();
    public int Offset { get; set; }
    public int Limit { get; set; }
    public int TotalHits { get; set; }
}

public class ModGalleryImage
{
    public string Url { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Description { get; set; }
}

public class ModDetail
{
    public string ModrinthId { get; set; } = string.Empty;
    public int? CurseForgeId { get; set; }
    public ModSource Source { get; set; } = ModSource.Modrinth;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Body { get; set; }
    public string? Author { get; set; }
    public string? IconUrl { get; set; }
    public int Downloads { get; set; }
    public int Followers { get; set; }
    public List<string> Categories { get; set; } = new();
    public List<ModGalleryImage> Gallery { get; set; } = new();
}

public class ModVersion
{
    public string Id { get; set; } = string.Empty;
    public string ModrinthId { get; set; } = string.Empty;
    public int? CurseForgeFileId { get; set; }
    public ModSource Source { get; set; } = ModSource.Modrinth;
    public string VersionNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>The "primary" Minecraft version shown in the UI (the newest one this file supports).</summary>
    public string? MinecraftVersion { get; set; }
    /// <summary>
    /// ALL Minecraft versions this file declares support for (Modrinth's "game_versions").
    /// A single file commonly supports several versions (e.g. ["1.20", "1.20.1"]), so
    /// compatibility checks must search this whole list, not just <see cref="MinecraftVersion"/>.
    /// </summary>
    public List<string> GameVersions { get; set; } = new();
    public LoaderType Loader { get; set; }
    public string? DownloadUrl { get; set; }
    public long FileSize { get; set; }
    public DateTime DatePublished { get; set; }
    public string? Sha1Hash { get; set; }
    public string? Sha512Hash { get; set; }
    public string? FileName { get; set; }
    public List<ModDependency> Dependencies { get; set; } = new();

    /// <summary>
    /// Modrinth's own release channel for this file: "release", "beta", or "alpha".
    /// Some mods (typically right after a brand-new Minecraft release) only ever
    /// publish a "beta" build for that exact Minecraft version before a "release"
    /// build follows later — version resolution prefers "release" but falls back to
    /// "beta" for the exact Minecraft version rather than guessing a neighboring one.
    /// </summary>
    public string? VersionType { get; set; }

    /// <summary>True when this file isn't a full "release" build (i.e. "beta"/"alpha").</summary>
    public bool IsPrerelease =>
        !string.IsNullOrEmpty(VersionType) && !string.Equals(VersionType, "release", StringComparison.OrdinalIgnoreCase);

    /// <summary>Label for the version-picker dropdown — shows the Minecraft version
    /// alongside the mod's own version number so users can see at a glance which
    /// Minecraft release a build targets before switching to it.</summary>
    public string DisplayLabel =>
        (string.IsNullOrEmpty(MinecraftVersion) ? VersionNumber : $"{VersionNumber}  ({MinecraftVersion})")
        + (IsPrerelease ? $"  [{VersionType}]" : string.Empty);
}

public class ModDependency
{
    public string? ProjectId { get; set; }
    public string? VersionId { get; set; }
    public DependencyType Type { get; set; }
}

/// <summary>
/// Result of checking installed mods for newer versions on Modrinth.
/// </summary>
public class ModUpdateCheckResult
{
    public int Checked { get; set; }
    public List<string> UpdatesAvailable { get; set; } = new();
    public List<string> Updated { get; set; } = new();
    public string Summary { get; set; } = string.Empty;
}

/// <summary>
/// The kind of problem found while checking an installed mod's required dependencies.
/// </summary>
public enum DependencyIssueType
{
    /// <summary>A required dependency project isn't installed at all.</summary>
    Missing,
    /// <summary>The dependency IS installed, but its installed version isn't compatible
    /// with this installation's Minecraft version/loader — either too old or too new.</summary>
    IncompatibleVersion
}

/// <summary>A single dependency problem found by <see cref="IModService.CheckDependencyCompatibilityAsync"/>.</summary>
public class DependencyIssue
{
    public string RequiringModName { get; set; } = string.Empty;
    public string DependencyProjectId { get; set; } = string.Empty;
    public string DependencyName { get; set; } = string.Empty;
    public DependencyIssueType Type { get; set; }
    public string Description { get; set; } = string.Empty;

    /// <summary>Set only for <see cref="DependencyIssueType.IncompatibleVersion"/>: the
    /// already-installed Mod record that needs to be swapped to a compatible version.</summary>
    public Guid? InstalledModId { get; set; }

    /// <summary>Set when the missing dependency was identified on CurseForge
    /// (the integer CurseForge mod ID), so FixDependencyIssueAsync knows which
    /// platform to download from.</summary>
    public int? CurseForgeModId { get; set; }

    public string TypeLabel => Type == DependencyIssueType.Missing ? "Missing" : "Wrong version";
}

/// <summary>Result of scanning an installation's installed mods for dependency problems.</summary>
public class DependencyCheckResult
{
    public int ModsChecked { get; set; }
    public List<DependencyIssue> Issues { get; set; } = new();
    public string Summary { get; set; } = string.Empty;
}

/// <summary>
/// Outcome of installing a mod (and its required dependencies) to disk.
/// </summary>
public class ModInstallResult
{
    public bool Success { get; set; }

    /// <summary>
    /// True when the primary mod was already installed and the download was
    /// skipped entirely. Lets the caller (AI executor, UI) show "already
    /// installed" instead of "installed".
    /// </summary>
    public bool WasAlreadyInstalled { get; set; }

    public Mod? PrimaryMod { get; set; }
    public List<Mod> InstalledDependencies { get; set; } = new();
    public List<string> SkippedAlreadyInstalled { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public string? Error { get; set; }
    public string Summary { get; set; } = string.Empty;
}
