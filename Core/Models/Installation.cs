namespace MinecraftControlHub.Core.Models;

public enum LoaderType
{
    Vanilla,
    Forge,
    Fabric,
    NeoForge,
    Quilt
}

public class Installation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string MinecraftVersion { get; set; } = string.Empty;
    public LoaderType Loader { get; set; }
    /// <summary>Pinned exact loader build (e.g. "47.4.20", "0.16.10"). Null/empty = always
    /// resolve the newest compatible build at launch time (previous default behavior).</summary>
    public string? LoaderVersion { get; set; }
    public string? IconPath { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastPlayed { get; set; }
    public string? GameDirectory { get; set; }
    public string? JavaPath { get; set; }
    public int? MaxMemoryMB { get; set; }
    public int? MinMemoryMB { get; set; }
    /// <summary>Extra raw JVM arguments (space-separated), appended after the memory flags
    /// on every launch — e.g. "-XX:+UseG1GC -Dfile.encoding=UTF-8". Null/empty = none.</summary>
    public string? CustomJvmArgs { get; set; }

    /// <summary>
    /// When true, mod updates are only applied if the new version targets the exact
    /// same Minecraft version as this installation. Prevents accidentally pulling in
    /// a mod version built for 1.20.4 into a 1.20.1 pack.
    /// </summary>
    public bool PinMinecraftVersion { get; set; }
}
