using System.IO;

namespace MinecraftControlHub.Core.Services;

/// <summary>
/// A simple, always-on file logger for diagnosing mod install/update/dependency
/// decisions after the fact — e.g. "why did the Fix button pick this version?".
/// Writes to <see cref="AppPaths.DiagnosticsLogFile"/> (persists across rebuilds,
/// unlike writing next to the .exe in bin\Debug). Never throws — a logging failure
/// must never take down the feature it's trying to help debug.
/// </summary>
public interface IAppLogService
{
    /// <summary>Full path of the log file currently being written to.</summary>
    string LogFilePath { get; }

    /// <summary>Appends a single timestamped line under the given category, e.g.
    /// Log("Fix", "Applied update for Sodium: 0.6.0 -> 0.6.1").</summary>
    void Log(string category, string message);

    /// <summary>Convenience overload for logging a caught exception.</summary>
    void LogError(string category, string message, Exception ex);
}

public class AppLogService : IAppLogService
{
    private static readonly object WriteLock = new();

    // A fresh log per run keeps things readable — old diagnostics from a previous
    // session don't pile up forever, but the last run before something went wrong is
    // still on disk (renamed to .previous.log) instead of overwritten mid-look.
    public string LogFilePath { get; } = AppPaths.DiagnosticsLogFile;

    public AppLogService()
    {
        try
        {
            if (File.Exists(LogFilePath))
            {
                var previousPath = Path.Combine(
                    Path.GetDirectoryName(LogFilePath)!,
                    "diagnostics.previous.log");
                File.Copy(LogFilePath, previousPath, overwrite: true);
                File.Delete(LogFilePath);
            }
        }
        catch
        {
            // Best-effort rotation only — never block startup over a log file.
        }
    }

    public void Log(string category, string message)
    {
        WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{category}] {message}");
    }

    public void LogError(string category, string message, Exception ex)
    {
        WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{category}] {message} — {ex.GetType().Name}: {ex.Message}");
    }

    private void WriteLine(string line)
    {
        try
        {
            lock (WriteLock)
            {
                File.AppendAllText(LogFilePath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must never crash the feature it's diagnosing.
        }
    }
}
