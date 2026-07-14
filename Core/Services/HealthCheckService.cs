using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MinecraftControlHub.Core.Models;

namespace MinecraftControlHub.Core.Services;

/// <summary>
/// Represents a single health check result, including score, status, and recommendation.
/// </summary>
public enum HealthStatus
{
    Healthy,    // ✔ Green
    Warning,    // ⚠ Yellow
    Critical    // ✗ Red
}

public class HealthCheckResult
{
    /// <summary>Overall health score: 0-100</summary>
    public int OverallScore { get; set; }

    /// <summary>Java version check: 0-25</summary>
    public int JavaScore { get; set; }
    public HealthStatus JavaStatus { get; set; }
    public string JavaAdvice { get; set; } = string.Empty;

    /// <summary>RAM usage check: 0-25</summary>
    public int RamScore { get; set; }
    public HealthStatus RamStatus { get; set; }
    public string RamAdvice { get; set; } = string.Empty;
    public double RamUsagePercent { get; set; }

    /// <summary>Mods: check for updates/dependencies - 0-25</summary>
    public int ModsScore { get; set; }
    public HealthStatus ModsStatus { get; set; }
    public string ModsAdvice { get; set; } = string.Empty;

    /// <summary>Crashes/stability: 0-25</summary>
    public int StabilityScore { get; set; }
    public HealthStatus StabilityStatus { get; set; }
    public string StabilityAdvice { get; set; } = string.Empty;
}

public class RamUsageStats
{
    /// <summary>Maximum heap size in MB (from -Xmx)</summary>
    public long MaxHeapMB { get; set; }

    /// <summary>Average used heap after GC in MB</summary>
    public long AverageUsedMB { get; set; }

    /// <summary>Peak used heap in MB</summary>
    public long PeakUsedMB { get; set; }

    /// <summary>Percentage of max heap used (average)</summary>
    public double UsagePercent => MaxHeapMB > 0 ? (AverageUsedMB * 100.0) / MaxHeapMB : 0;
}

/// <summary>
/// Service that analyzes installation/server health based on:
/// - Java version compatibility (25%)
/// - RAM usage patterns from GC logs (25%)
/// - Mod updates and dependency issues (25%)
/// - Recent crashes/logs (25%)
/// 
/// Returns actionable advice for each category.
/// </summary>
public class HealthCheckService
{
    private readonly IJavaService _javaService;
    private readonly IModService _modService;
    private readonly IAppLogService _log;

    public HealthCheckService(IJavaService javaService, IModService modService, IAppLogService log)
    {
        _javaService = javaService;
        _modService = modService;
        _log = log;
    }

    /// <summary>
    /// Compute a full health check for an installation/server.
    /// </summary>
    public async Task<HealthCheckResult> CheckInstallationHealthAsync(
        Installation installation,
        IReadOnlyList<Mod> installedMods,
        CancellationToken cancellationToken = default)
    {
        var result = new HealthCheckResult();

        // 1. Java version check
        var javaCheck = await CheckJavaHealthAsync(installation);
        result.JavaScore = javaCheck.score;
        result.JavaStatus = javaCheck.status;
        result.JavaAdvice = javaCheck.advice;

        // 2. RAM usage check (read GC logs)
        var ramStats = ReadRamUsageFromGcLogs(installation);
        var ramCheck = CheckRamHealth(ramStats);
        result.RamScore = ramCheck.score;
        result.RamStatus = ramCheck.status;
        result.RamAdvice = ramCheck.advice;
        result.RamUsagePercent = ramStats.UsagePercent;

        // 3. Mods check (updates, dependencies)
        var modsCheck = await CheckModsHealthAsync(installation, installedMods);
        result.ModsScore = modsCheck.score;
        result.ModsStatus = modsCheck.status;
        result.ModsAdvice = modsCheck.advice;

        // 4. Stability check (crash logs)
        var stabilityCheck = CheckStabilityHealth(installation);
        result.StabilityScore = stabilityCheck.score;
        result.StabilityStatus = stabilityCheck.status;
        result.StabilityAdvice = stabilityCheck.advice;

        // Calculate overall score
        result.OverallScore = (result.JavaScore + result.RamScore + result.ModsScore + result.StabilityScore) / 4;

        return result;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Individual health checks
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<(int score, HealthStatus status, string advice)> CheckJavaHealthAsync(Installation installation)
    {
        try
        {
            var recommendation = await _javaService.GetRecommendationAsync(installation.MinecraftVersion ?? "");
            var javaPath = installation.JavaPath ?? string.Empty;

            if (string.IsNullOrWhiteSpace(javaPath))
            {
                // If we already have a matching local java reported by recommendation, prefer that
                if (recommendation.MatchingJava != null)
                    return (25, HealthStatus.Healthy, $"✔ Using bundled Java {recommendation.MatchingJava.MajorVersion} for Minecraft {installation.MinecraftVersion}");

                return (0, HealthStatus.Critical, "⚠ No Java path set. Go to Settings to specify your Java installation.");
            }

            if (!File.Exists(javaPath))
                return (0, HealthStatus.Critical, "✗ Java executable not found at configured path.");

            // Check detected major version if possible
            var detected = await _javaService.DetectJavaAsync();
            var detectedMajor = detected?.MajorVersion ?? 0;
            if (detectedMajor >= recommendation.RecommendedMajor)
                return (25, HealthStatus.Healthy, $"✔ Java {detectedMajor} correctly configured for Minecraft {installation.MinecraftVersion}");

            return (10, HealthStatus.Warning, $"⚠ Java {detectedMajor} found but Minecraft {installation.MinecraftVersion} prefers Java {recommendation.RecommendedMajor}.");
        }
        catch (Exception ex)
        {
            _log.LogError("HealthCheck", $"Java version check failed: {ex.Message}", ex);
            return (10, HealthStatus.Warning, $"⚠ Could not verify Java: {ex.Message}");
        }
    }

    private (int score, HealthStatus status, string advice) CheckRamHealth(RamUsageStats stats)
    {
        if (stats.MaxHeapMB <= 0)
            return (0, HealthStatus.Warning, "⚠ Could not read RAM configuration.");

        var usage = stats.UsagePercent;

        // Good range: 50-85% utilization (not wasting memory, not struggling)
        if (usage >= 50 && usage <= 85)
            return (25, HealthStatus.Healthy,
                $"✔ RAM usage healthy ({usage:F1}% of {stats.MaxHeapMB} MB used)");

        // Too much: <40% — allocated more than needed
        if (usage < 40)
            return (15, HealthStatus.Warning,
                $"⚠ RAM underutilized ({usage:F1}%). Consider reducing max heap to free up system RAM.");

        // Too little: >85% — likely causing stutters, GC pressure
        if (usage > 85)
            return (10, HealthStatus.Critical,
                $"✗ RAM overutilized ({usage:F1}%). Increase max heap (-Xmx) or reduce mods/render distance.");

        return (20, HealthStatus.Warning, "⚠ RAM usage outside optimal range.");
    }

    private async Task<(int score, HealthStatus status, string advice)> CheckModsHealthAsync(
        Installation installation,
        IReadOnlyList<Mod> installedMods)
    {
        try
        {
            if (installedMods.Count == 0)
                return (25, HealthStatus.Healthy, "✔ No mods installed.");

            // Check for updates
            var outdatedCount = installedMods.Count(m => m.UpdateAvailable);
            if (outdatedCount > 0)
                return (10, HealthStatus.Warning,
                    $"⚠ {outdatedCount} mod(s) have updates available. Check Mods tab for details.");

            // Check for dependency issues
            var depResult = await _modService.CheckDependencyCompatibilityAsync(installation);
            if (depResult.Issues.Count > 0)
                return (12, HealthStatus.Warning,
                    $"⚠ {depResult.Issues.Count} dependency issue(s) found. Auto-fix available in Mods tab.");

            return (25, HealthStatus.Healthy, $"✔ All {installedMods.Count} mods up to date, no conflicts.");
        }
        catch (Exception ex)
        {
            _log.LogError("HealthCheck", $"Mod health check failed: {ex.Message}", ex);
            return (15, HealthStatus.Warning, $"⚠ Could not check mods: {ex.Message}");
        }
    }

    private (int score, HealthStatus status, string advice) CheckStabilityHealth(Installation installation)
    {
        try
        {
            var logDir = Path.Combine(AppPaths.InstanceDir(installation.Id), "logs");
            if (!Directory.Exists(logDir))
                return (25, HealthStatus.Healthy, "✔ No crash logs detected.");

            var crashes = Directory.GetFiles(logDir, "*crash*.log")
                .OrderByDescending(f => File.GetLastWriteTime(f))
                .ToList();

            if (crashes.Count == 0)
                return (25, HealthStatus.Healthy, "✔ No recent crashes.");

            var newestCrash = crashes.First();
            var crashAge = DateTime.Now - File.GetLastWriteTime(newestCrash);

            if (crashAge.TotalHours < 1)
                return (8, HealthStatus.Critical, $"✗ Recent crash detected ({crashAge.TotalMinutes:F0}m ago). Check logs.");

            if (crashAge.TotalHours < 24)
                return (15, HealthStatus.Warning, $"⚠ Crash log from {crashAge.TotalHours:F0}h ago. Review if problems persist.");

            return (20, HealthStatus.Healthy, $"✔ Last crash was {crashAge.TotalDays:F0}d ago (stable now).");
        }
        catch (Exception ex)
        {
            _log.LogError("HealthCheck", $"Stability check failed: {ex.Message}", ex);
            return (15, HealthStatus.Warning, $"⚠ Could not check stability: {ex.Message}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // RAM usage analysis from GC logs
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Read RAM usage patterns from GC log (if exists).
    /// Looks for patterns like "gc.log" created by -Xlog:gc* JVM flags.
    /// </summary>
    private RamUsageStats ReadRamUsageFromGcLogs(Installation installation)
    {
        var stats = new RamUsageStats();

        try
        {
            // Try to parse -Xmx from the last successful launch config
            // For now, use the configured MaxMemoryMB
            stats.MaxHeapMB = installation.MaxMemoryMB ?? 2048;

            var logDir = Path.Combine(AppPaths.InstanceDir(installation.Id), "logs");
            if (!Directory.Exists(logDir))
                return stats; // No logs yet

            // Look for gc.log files
            var gcLogFiles = Directory.GetFiles(logDir, "gc*.log")
                .OrderByDescending(f => File.GetLastWriteTime(f))
                .ToList();

            if (gcLogFiles.Count == 0)
                return stats; // No GC log yet

            var latestGcLog = gcLogFiles.First();
            ParseGcLog(latestGcLog, stats);
        }
        catch (Exception ex)
        {
            // If parsing fails, return default stats with max heap only
            _log.LogError("HealthCheck", $"Failed to parse GC log: {ex.Message}", ex);
        }

        return stats;
    }

    /// <summary>
    /// Parse GC log to extract heap usage statistics.
    /// Format varies by Java version, but we look for patterns like:
    /// "[0.123s][info   ][gc,heap] GC(1) Heap after GC invocations=1: ... used 256M"
    /// </summary>
    private void ParseGcLog(string gcLogPath, RamUsageStats stats)
    {
        if (!File.Exists(gcLogPath))
            return;

        var heapUsages = new List<long>();

        try
        {
            // Read last 1000 lines (most recent)
            var lines = File.ReadAllLines(gcLogPath)
                .TakeLast(1000)
                .ToList();

            // Pattern: "used XXX[MK]"
            var usedPattern = new Regex(@"used\s+(\d+(?:,\d+)?)\s*([MK])?", RegexOptions.IgnoreCase);

            foreach (var line in lines)
            {
                var match = usedPattern.Match(line);
                if (!match.Success) continue;

                if (!long.TryParse(match.Groups[1].Value.Replace(",", ""), out var value))
                    continue;

                var unit = match.Groups[2].Value?.ToUpperInvariant() ?? "M";
                var valueMB = unit == "K" ? value / 1024 : value;

                heapUsages.Add(valueMB);
            }

            if (heapUsages.Count > 0)
            {
                stats.AverageUsedMB = (long)heapUsages.Average();
                stats.PeakUsedMB = heapUsages.Max();
            }
        }
        catch { /* Ignore parsing errors */ }
    }
}
