namespace MinecraftControlHub.Core.Models;

/// <summary>Type of content that can live in an installation's game directory.</summary>
public enum ContentType
{
    ResourcePack,
    ShaderPack,
    World,
    Modpack
}

/// <summary>Represents an installed resourcepack, shaderpack, or world save.</summary>
public class ContentItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public ContentType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Author { get; set; }
    public string? IconUrl { get; set; }
    public string? ModrinthId { get; set; }
    public string? Version { get; set; }
    public string? FileName { get; set; }
    public string? FilePath { get; set; }
    public long? FileSizeBytes { get; set; }
    public DateTime InstalledAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When false the file is renamed to &lt;name&gt;.disabled on disk so the game ignores it.
    /// For zip/jar files: name.zip.disabled. Computed from FileName on load.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Modrinth version id currently installed (if from Modrinth).</summary>
    public string? ModrinthVersionId { get; set; }

    /// <summary>Human-readable file size, e.g. "2.4 MB".</summary>
    public string FileSizeLabel => FileSizeBytes.HasValue
        ? FileSizeBytes.Value >= 1_048_576
            ? $"{FileSizeBytes.Value / 1_048_576.0:F1} MB"
            : $"{FileSizeBytes.Value / 1024.0:F0} KB"
        : string.Empty;
}

/// <summary>A search result from Modrinth for resourcepacks, shaderpacks, or worlds.</summary>
public class ContentSearchResult
{
    public string ModrinthId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Author { get; set; }
    public string? IconUrl { get; set; }
    public int Downloads { get; set; }
    public DateTime? DateModified { get; set; }
    public List<string> Categories { get; set; } = new();
    public ContentType ContentType { get; set; }
    /// <summary>True if already installed in the current installation.</summary>
    public bool IsInstalled { get; set; }
}

public class ContentSearchPage
{
    public List<ContentSearchResult> Hits { get; set; } = new();
    public int Offset { get; set; }
    public int Limit { get; set; }
    public int TotalHits { get; set; }
}

/// <summary>A mod dependency required (or recommended) by a shaderpack or resourcepack.</summary>
public class ContentDependencyInfo
{
    public string ProjectId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsInstalled { get; set; }
    /// <summary>True = must have this; False = nice to have.</summary>
    public bool IsRequired { get; set; }
}

public class ContentInstallResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<string> Warnings { get; set; } = new();
}
