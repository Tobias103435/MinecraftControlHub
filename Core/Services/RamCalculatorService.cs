using MinecraftControlHub.Core.Models;

namespace MinecraftControlHub.Core.Services;

/// <summary>
/// Result of a RAM estimate: the recommended -Xms/-Xmx values plus a short,
/// human-readable breakdown explaining how the number was reached.
/// </summary>
public class RamEstimate
{
    public int RecommendedMinMB { get; set; }
    public int RecommendedMaxMB { get; set; }
    public string Breakdown { get; set; } = string.Empty;
}

/// <summary>
/// Rough (but reasoned) estimate of how much RAM an installation needs, based on
/// its Minecraft version, mod loader, installed mods, and desired render distance.
/// This is deliberately conservative/approximate — there's no way to know exactly
/// what a given modpack needs without profiling it — but it gives a sane starting
/// point instead of the usual "just try 4GB" guesswork.
/// </summary>
public static class RamCalculatorService
{
    /// <summary>Modern Minecraft's own default render distance (chunks), used as the "no extra cost" baseline.</summary>
    public const int BaselineRenderDistance = 12;

    public static RamEstimate Estimate(string? minecraftVersion, LoaderType loader, IReadOnlyCollection<Mod> mods, int renderDistance)
    {
        var notes = new List<string>();

        // ---- Base vanilla footprint ----
        // Newer versions carry more client-side data (biome/lighting engine changes
        // in 1.17+, wider block/data-driven registries in 1.20+) so the plain client
        // itself wants more headroom even before any mods are involved.
        var baseMb = VanillaBaseMb(minecraftVersion, out var baseNote);
        notes.Add(baseNote);

        // ---- Loader overhead ----
        // Fabric/Quilt are thin, near-zero-overhead loaders. Forge/NeoForge load a
        // much heavier stack up front (ModLauncher/BootstrapLauncher, mixin, access
        // transformers, coremods) before a single mod class even runs.
        var loaderMb = loader switch
        {
            LoaderType.Forge => 512,
            LoaderType.NeoForge => 512,
            LoaderType.Fabric => 192,
            LoaderType.Quilt => 192,
            _ => 0
        };
        if (loaderMb > 0)
            notes.Add($"{loader} loader overhead: +{loaderMb} MB");

        // ---- Mods ----
        // Two components: a flat per-mod cost (classloading/metadata, mixins, its
        // own small caches — roughly constant regardless of jar size) plus a size-
        // based cost (a mod's jar size is a decent proxy for how much it actually
        // allocates at runtime — textures, models, registries, config, etc. — an
        // installed mod commonly ends up using several times its file size in
        // live heap once its assets/data are unpacked and indexed).
        const int perModFlatMb = 12;
        const double sizeMultiplier = 2.5;
        const long defaultAssumedModBytes = 5L * 1024 * 1024; // 5 MB, used when a mod's file size isn't known yet.

        var modCount = mods.Count;
        long totalModBytes = 0;
        foreach (var mod in mods)
            totalModBytes += mod.FileSize ?? defaultAssumedModBytes;

        var modsFlatMb = modCount * perModFlatMb;
        var modsSizeMb = (int)Math.Ceiling(totalModBytes / 1024.0 / 1024.0 * sizeMultiplier);
        var modsMb = modsFlatMb + modsSizeMb;

        if (modCount > 0)
            notes.Add($"{modCount} mod{(modCount == 1 ? "" : "s")}: +{modsMb} MB");
        else
            notes.Add("No mods installed");

        // ---- Render distance ----
        // Chunk memory roughly scales with the AREA in view, i.e. quadratically
        // with radius, not linearly — going from 12 to 24 chunks is a much bigger
        // jump than 8 to 12. Model it as extra chunks-loaded (a rough (2r+1)^2
        // chunk-column count) relative to the baseline, at ~0.6 MB/loaded chunk
        // column (built terrain + entities + lighting data), which lines up
        // reasonably with what real modpacks/launcher guides recommend per extra
        // render-distance notch.
        renderDistance = Math.Clamp(renderDistance, 2, 48);
        var baselineChunks = ChunkColumns(BaselineRenderDistance);
        var actualChunks = ChunkColumns(renderDistance);
        var extraChunks = Math.Max(0, actualChunks - baselineChunks);
        const double mbPerChunkColumn = 0.6;
        var renderMb = (int)Math.Ceiling(extraChunks * mbPerChunkColumn);

        if (renderDistance > BaselineRenderDistance)
            notes.Add($"Render distance {renderDistance} (vs default {BaselineRenderDistance}): +{renderMb} MB");
        else
            notes.Add($"Render distance {renderDistance}: no extra cost (at/below default {BaselineRenderDistance})");

        // ---- Total ----
        var totalMb = baseMb + loaderMb + modsMb + renderMb;

        // A little slack on top so the game isn't running right up against -Xmx
        // (GC headroom, texture streaming spikes, alt-tabbing, etc.) — 15%,
        // rounded up to the nearest 512 MB for a clean number.
        var withHeadroom = (int)Math.Ceiling(totalMb * 1.15);
        var recommendedMax = RoundUpTo(withHeadroom, 512);
        var recommendedMin = RoundUpTo(recommendedMax / 2, 256);
        recommendedMin = Math.Max(recommendedMin, 1024);
        recommendedMax = Math.Max(recommendedMax, recommendedMin + 512);

        notes.Add("+15% headroom, rounded to a clean number");

        return new RamEstimate
        {
            RecommendedMinMB = recommendedMin,
            RecommendedMaxMB = recommendedMax,
            Breakdown = string.Join(" · ", notes)
        };
    }

    private static int VanillaBaseMb(string? minecraftVersion, out string note)
    {
        var (major, minor) = ParseVersion(minecraftVersion);

        // major here is always "1" for any modern MC version; minor is the real
        // generation number (1.17, 1.18, 1.20, ...).
        if (major != 1)
        {
            note = "Base client: 2048 MB (unknown version, assuming modern)";
            return 2048;
        }

        if (minor >= 20)
        {
            note = $"Base client ({minecraftVersion}): 2048 MB";
            return 2048;
        }
        if (minor >= 17)
        {
            note = $"Base client ({minecraftVersion}): 1792 MB";
            return 1792;
        }
        if (minor >= 13)
        {
            note = $"Base client ({minecraftVersion}): 1536 MB";
            return 1536;
        }

        note = $"Base client ({minecraftVersion}): 1024 MB";
        return 1024;
    }

    private static (int Major, int Minor) ParseVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return (1, 21);

        var parts = version.Trim().Split('.', '-', '_');
        var major = parts.Length > 0 && int.TryParse(parts[0], out var maj) ? maj : 1;
        var minor = parts.Length > 1 && int.TryParse(parts[1], out var min) ? min : 21;
        return (major, minor);
    }

    private static int ChunkColumns(int renderDistance) => (2 * renderDistance + 1) * (2 * renderDistance + 1);

    private static int RoundUpTo(int value, int step) => (int)(Math.Ceiling(value / (double)step) * step);
}
