using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace MinecraftControlHub.Core.Services;

/// <summary>
/// A Java runtime that was detected on this PC.
/// </summary>
public class DetectedJava
{
    public string Path { get; set; } = string.Empty;
    public int MajorVersion { get; set; }
    public string RawVersion { get; set; } = string.Empty;
}

/// <summary>
/// Advice about which Java version an installation needs and whether a suitable
/// runtime is already available on this machine.
/// </summary>
public class JavaRecommendation
{
    /// <summary>The Java major version Mojang requires for this Minecraft version (e.g. 21).</summary>
    public int RecommendedMajor { get; set; }

    /// <summary>A matching (or newer) Java runtime found locally, if any.</summary>
    public DetectedJava? MatchingJava { get; set; }

    /// <summary>Any Java runtime found locally, even if it is the wrong version.</summary>
    public DetectedJava? DetectedJava { get; set; }

    public bool IsSatisfied => MatchingJava != null;

    /// <summary>A ready-to-open Adoptium download page for the recommended major version.</summary>
    public string DownloadUrl => $"https://adoptium.net/temurin/releases/?version={RecommendedMajor}";

    /// <summary>A short, human-readable summary for the UI.</summary>
    public string Summary { get; set; } = string.Empty;
}

public interface IJavaService
{
    /// <summary>
    /// Returns the Java major version Mojang requires for the given Minecraft version.
    /// </summary>
    int RecommendJavaMajor(string minecraftVersion);

    /// <summary>
    /// Builds a full recommendation for a Minecraft version, including whether a
    /// suitable Java runtime is already installed locally.
    /// </summary>
    Task<JavaRecommendation> GetRecommendationAsync(string minecraftVersion);

    /// <summary>Attempts to locate a Java executable on this PC (via JAVA_HOME / PATH).</summary>
    Task<DetectedJava?> DetectJavaAsync();

    /// <summary>
    /// Downloads and unpacks an Eclipse Temurin JDK for the given major version into
    /// the app's data folder. Returns the path to the java executable on success.
    /// </summary>
    Task<string> DownloadJavaAsync(int majorVersion,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}

public class JavaService : IJavaService
{
    public int RecommendJavaMajor(string minecraftVersion)
    {
        var (major, minor, _) = ParseVersion(minecraftVersion);

        // Mojang's official runtime requirements per Minecraft version.
        // 1.20.5+  -> Java 21
        // 1.18 - 1.20.4 -> Java 17
        // 1.17.x   -> Java 16
        // <= 1.16.x -> Java 8
        if (major != 1)
            return 21; // Unknown / future scheme: assume latest LTS.

        if (minor >= 21)
            return 21;
        if (minor == 20)
            return PatchAtLeast(minecraftVersion, 5) ? 21 : 17;
        if (minor >= 18)
            return 17;
        if (minor == 17)
            return 16;
        return 8;
    }

    public async Task<JavaRecommendation> GetRecommendationAsync(string minecraftVersion)
    {
        var recommendedMajor = RecommendJavaMajor(minecraftVersion);
        var detected = await DetectJavaAsync();

        var recommendation = new JavaRecommendation
        {
            RecommendedMajor = recommendedMajor,
            DetectedJava = detected,
            MatchingJava = detected != null && detected.MajorVersion >= recommendedMajor ? detected : null
        };

        if (recommendation.IsSatisfied)
        {
            recommendation.Summary =
                $"Minecraft {minecraftVersion} needs Java {recommendedMajor}. " +
                $"Found compatible Java {recommendation.MatchingJava!.MajorVersion} on this PC — it will be used automatically.";
        }
        else if (detected != null)
        {
            recommendation.Summary =
                $"Minecraft {minecraftVersion} needs Java {recommendedMajor}, but only Java {detected.MajorVersion} was found. " +
                $"Install Java {recommendedMajor} from {recommendation.DownloadUrl}.";
        }
        else
        {
            recommendation.Summary =
                $"Minecraft {minecraftVersion} needs Java {recommendedMajor}. No Java runtime was found on this PC — " +
                $"download it from {recommendation.DownloadUrl}.";
        }

        return recommendation;
    }

    public async Task<DetectedJava?> DetectJavaAsync()
    {
        // Prefer JAVA_HOME, then fall back to whatever "java" resolves to on PATH.
        var candidates = new List<string>();

        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome))
            candidates.Add(System.IO.Path.Combine(javaHome, "bin", "java.exe"));

        candidates.Add("java");

        foreach (var candidate in candidates)
        {
            var detected = await ProbeJavaAsync(candidate);
            if (detected != null)
                return detected;
        }

        return null;
    }

    private static async Task<DetectedJava?> ProbeJavaAsync(string javaPath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = javaPath,
                Arguments = "-version",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
                return null;

            // "java -version" prints to stderr.
            var output = await process.StandardError.ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(output))
                output = await process.StandardOutput.ReadToEndAsync();
            process.WaitForExit(5000);

            var major = ParseJavaMajor(output);
            if (major <= 0)
                return null;

            return new DetectedJava
            {
                Path = javaPath,
                MajorVersion = major,
                RawVersion = output.Split('\n').FirstOrDefault()?.Trim() ?? output.Trim()
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Java probe failed for '{javaPath}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Parses the major version from output like: version "21.0.2" or version "1.8.0_401".
    /// </summary>
    private static int ParseJavaMajor(string versionOutput)
    {
        var match = Regex.Match(versionOutput, "version \"([0-9._]+)\"");
        if (!match.Success)
            return 0;

        var parts = match.Groups[1].Value.Split('.');
        if (parts.Length == 0)
            return 0;

        // Legacy scheme "1.8.0" -> Java 8; modern scheme "21.0.2" -> Java 21.
        if (parts[0] == "1" && parts.Length > 1 && int.TryParse(parts[1], out var legacy))
            return legacy;

        return int.TryParse(parts[0], out var major) ? major : 0;
    }

    private static (int major, int minor, int patch) ParseVersion(string minecraftVersion)
    {
        if (string.IsNullOrWhiteSpace(minecraftVersion))
            return (1, 0, 0);

        // Keep only the leading numeric-dotted part (handles things like "1.20.1-pre1").
        var match = Regex.Match(minecraftVersion.Trim(), @"^(\d+)(?:\.(\d+))?(?:\.(\d+))?");
        if (!match.Success)
            return (1, 0, 0);

        var major = int.Parse(match.Groups[1].Value);
        var minor = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 0;
        var patch = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0;
        return (major, minor, patch);
    }

    private static bool PatchAtLeast(string minecraftVersion, int patch)
    {
        var (_, _, p) = ParseVersion(minecraftVersion);
        return p >= patch;
    }

    public async Task<string> DownloadJavaAsync(
        int majorVersion,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Download Eclipse Temurin (Adoptium) from the official API
        var os   = "windows";
        var arch = Environment.Is64BitOperatingSystem ? "x64" : "x86";
        var imageType = "jdk";

        progress?.Report($"Looking up Temurin Java {majorVersion} download…");

        using var http = new System.Net.Http.HttpClient();
        http.DefaultRequestHeaders.Add("User-Agent", "MinecraftControlHub/1.0");

        // Adoptium API: latest release for the given major
        var apiUrl = $"https://api.adoptium.net/v3/assets/latest/{majorVersion}/hotspot" +
                     $"?os={os}&architecture={arch}&image_type={imageType}";

        var json     = await http.GetStringAsync(apiUrl, cancellationToken);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root      = doc.RootElement;

        if (root.ValueKind != System.Text.Json.JsonValueKind.Array || root.GetArrayLength() == 0)
            throw new InvalidOperationException(
                $"No Temurin Java {majorVersion} build found for {os}/{arch}.");

        var asset       = root[0];
        var downloadUrl = asset.GetProperty("binary")
                              .GetProperty("package")
                              .GetProperty("link")
                              .GetString()
                          ?? throw new InvalidOperationException("Download URL not found in API response.");

        var fileName = asset.GetProperty("binary")
                           .GetProperty("package")
                           .GetProperty("name")
                           .GetString() ?? $"java-{majorVersion}.zip";

        // Download to a temp file
        var javaDir  = System.IO.Path.Combine(AppPaths.DataRoot, "java");
        System.IO.Directory.CreateDirectory(javaDir);
        var zipPath  = System.IO.Path.Combine(javaDir, fileName);

        progress?.Report($"Downloading Java {majorVersion} ({fileName})…");

        var response = await http.GetAsync(downloadUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        await System.IO.File.WriteAllBytesAsync(zipPath, bytes, cancellationToken);

        progress?.Report("Extracting…");

        var extractDir = System.IO.Path.Combine(javaDir, $"java-{majorVersion}");
        if (System.IO.Directory.Exists(extractDir))
            System.IO.Directory.Delete(extractDir, recursive: true);

        System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractDir);
        System.IO.File.Delete(zipPath);

        // Find the java.exe inside the extracted folder (it's nested one level deep)
        var javaExe = System.IO.Directory
            .GetFiles(extractDir, "java.exe", System.IO.SearchOption.AllDirectories)
            .FirstOrDefault()
            ?? throw new InvalidOperationException(
                "java.exe not found after extracting the JDK.");

        progress?.Report($"Java {majorVersion} installed at: {javaExe}");
        return javaExe;
    }
}
