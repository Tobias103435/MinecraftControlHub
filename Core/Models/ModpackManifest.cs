using System.Text.Json.Serialization;

namespace MinecraftControlHub.Core.Models;

/// <summary>
/// C# model of the Modrinth Modpack Format (.mrpack) — the exact format Prism
/// Launcher itself uses for "Export Instance → Modrinth pack format (.mrpack)" and
/// "Import Instance". Spec: https://support.modrinth.com/en/articles/8802351-modrinth-modpack-format-mrpack
///
/// Using this format (rather than inventing a Minecraft Control Hub-only one) is
/// what makes installations genuinely portable: a .mrpack exported here opens
/// directly via Prism's "Import Instance" (and Modrinth App, and any other
/// mrpack-aware launcher) with zero conversion, and a .mrpack exported from Prism
/// imports directly here.
///
/// The manifest (modrinth.index.json) sits at the root of the .mrpack zip. Mods are
/// declared as external downloads so the .mrpack itself stays small (the actual jar
/// bytes aren't embedded); everything that can't be expressed that way — configs,
/// resourcepacks, shaderpacks, manually side-loaded mods — travels verbatim inside
/// the zip's "overrides/" folder instead.
/// </summary>
public class ModrinthModpackIndex
{
    [JsonPropertyName("formatVersion")]
    public int FormatVersion { get; set; } = 1;

    [JsonPropertyName("game")]
    public string Game { get; set; } = "minecraft";

    [JsonPropertyName("versionId")]
    public string VersionId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("files")]
    public List<ModrinthModpackFile> Files { get; set; } = new();

    /// <summary>
    /// Known keys (per the spec): "minecraft", "forge", "neoforge", "fabric-loader",
    /// "quilt-loader". A modpack with no loader key at all is vanilla.
    /// </summary>
    [JsonPropertyName("dependencies")]
    public Dictionary<string, string> Dependencies { get; set; } = new();
}

public class ModrinthModpackFile
{
    /// <summary>Destination path relative to the Minecraft instance directory, e.g. "mods/Sodium.jar".</summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    /// <summary>Must contain at least "sha1" and "sha512" per the spec.</summary>
    [JsonPropertyName("hashes")]
    public Dictionary<string, string> Hashes { get; set; } = new();

    [JsonPropertyName("env")]
    public ModrinthModpackEnv? Env { get; set; }

    /// <summary>HTTPS URL(s) this file can be downloaded from (first that works wins).</summary>
    [JsonPropertyName("downloads")]
    public List<string> Downloads { get; set; } = new();

    [JsonPropertyName("fileSize")]
    public long FileSize { get; set; }
}

public class ModrinthModpackEnv
{
    /// <summary>"required", "optional", or "unsupported".</summary>
    [JsonPropertyName("client")]
    public string Client { get; set; } = "required";

    [JsonPropertyName("server")]
    public string Server { get; set; } = "unsupported";
}

/// <summary>Outcome of exporting an installation to a .mrpack file.</summary>
public class ModpackExportResult
{
    public bool Success { get; set; }
    public string? FilePath { get; set; }
    public int ModsIncludedAsDownloads { get; set; }
    public int ModsIncludedAsOverrides { get; set; }
    public List<string> Warnings { get; set; } = new();
    public string? Error { get; set; }
    public string Summary { get; set; } = string.Empty;
}

/// <summary>Outcome of importing a .mrpack as a brand-new installation.</summary>
public class ModpackImportResult
{
    public bool Success { get; set; }
    public Installation? Installation { get; set; }
    public int ModsDownloaded { get; set; }
    public int OverrideFilesCopied { get; set; }
    public List<string> Warnings { get; set; } = new();
    public string? Error { get; set; }
    public string Summary { get; set; } = string.Empty;
}

/// <summary>
/// C# model of Prism Launcher's (and MultiMC/PolyMC's) own NATIVE instance export
/// format — a plain zip containing "mmc-pack.json" + "instance.cfg" + a "minecraft"
/// (or ".minecraft") game folder + a ".packignore" file and usually an icon file.
/// This is what Prism's "Export Instance" button produces BY DEFAULT — it is NOT the
/// Modrinth .mrpack format, so a real-world "exported from Prism" zip needs this
/// model to be imported at all. Reference examples of real mmc-pack.json files:
/// https://github.com/MultiMC/Launcher/wiki/Export-Instance
/// </summary>
public class MmcPackFile
{
    [JsonPropertyName("formatVersion")]
    public int FormatVersion { get; set; } = 1;

    [JsonPropertyName("components")]
    public List<MmcPackComponent> Components { get; set; } = new();
}

public class MmcPackComponent
{
    /// <summary>e.g. "net.minecraft", "net.fabricmc.fabric-loader", "net.minecraftforge",
    /// "org.quiltmc.quilt-loader", "net.neoforged" — plus support components like
    /// "org.lwjgl3"/"net.fabricmc.intermediary" that Prism auto-fills on first launch
    /// and this app can safely ignore.</summary>
    [JsonPropertyName("uid")]
    public string Uid { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("important")]
    public bool? Important { get; set; }

    [JsonPropertyName("cachedName")]
    public string? CachedName { get; set; }

    [JsonPropertyName("dependencyOnly")]
    public bool? DependencyOnly { get; set; }
}

/// <summary>Well-known component uids used in mmc-pack.json — the loader ones double
/// as the mapping back to this app's <see cref="LoaderType"/>.</summary>
public static class MmcComponentUid
{
    public const string Minecraft = "net.minecraft";
    public const string Forge = "net.minecraftforge";
    public const string Fabric = "net.fabricmc.fabric-loader";
    public const string Quilt = "org.quiltmc.quilt-loader";
    public const string NeoForge = "net.neoforged";
}
