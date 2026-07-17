namespace MinecraftControlHub.Core.Services;

/// <summary>
/// Represents the result of an update check.
/// </summary>
public record UpdateCheckResult(
    bool IsUpdateAvailable,
    string LatestVersion,
    string CurrentVersion,
    string? DownloadUrl,
    string? ReleaseNotes);

/// <summary>
/// Checks whether a newer version of Nexora Launcher is available.
/// </summary>
public interface IUpdateService
{
    /// <summary>The currently running version (read from assembly metadata).</summary>
    string CurrentVersion { get; }

    /// <summary>Cached result of the last check, or null if no check has run yet.</summary>
    UpdateCheckResult? LastResult { get; }

    /// <summary>Fires on the UI thread when an update check finishes.</summary>
    event EventHandler<UpdateCheckResult>? UpdateChecked;

    /// <summary>Perform an update check against the update.json file.</summary>
    Task<UpdateCheckResult> CheckAsync();

    /// <summary>
    /// Open the download URL in the system browser so the user can
    /// manually download and install the new version.
    /// </summary>
    void OpenDownloadPage();

    /// <summary>
    /// Downloads the installer to temp folder and starts it.
    /// </summary>
    Task DownloadAndStartUpdateAsync(IProgress<double>? progress = null);
}
