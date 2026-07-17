using MinecraftControlHub.Core.Services;

namespace MinecraftControlHub.UI.ViewModels;

/// <summary>
/// Update-checker portion of SettingsPageViewModel.
/// Handles the "App Updates" section in Settings.
/// </summary>
public partial class SettingsPageViewModel
{
    // ── Fields ──────────────────────────────────────────────────────────────

    private IUpdateService? _updateService;

    private bool   _isCheckingForUpdate;
    private bool   _isDownloadingUpdate;
    private bool   _isUpdateAvailable;
    private string _updateStatusMessage  = string.Empty;
    private string _updateCurrentVersion = string.Empty;
    private string _updateLatestVersion  = string.Empty;
    private string _updateReleaseNotes   = string.Empty;
    private double _updateDownloadProgress;

    /// <summary>
    /// Called by TopBar click handler to scroll directly to the Updates panel.
    /// The view subscribes to this event and performs the navigation.
    /// </summary>
    public event EventHandler? NavigateToUpdatesRequested;

    public void NavigateToUpdates() => NavigateToUpdatesRequested?.Invoke(this, EventArgs.Empty);

    // ── Initialization (call from constructor after _updateService is set) ───

    internal void InitUpdateService(IUpdateService updateService)
    {
        _updateService = updateService;

        UpdateCurrentVersion = _updateService.CurrentVersion;

        // React to the background startup check that may already have run
        _updateService.UpdateChecked += (_, result) => ApplyUpdateResult(result);

        if (_updateService.LastResult is { } cached)
            ApplyUpdateResult(cached);
    }

    private void ApplyUpdateResult(UpdateCheckResult result)
    {
        IsUpdateAvailable    = result.IsUpdateAvailable;
        UpdateLatestVersion  = result.LatestVersion;
        UpdateReleaseNotes   = result.ReleaseNotes ?? string.Empty;

        UpdateStatusMessage = result.IsUpdateAvailable
            ? $"Version {result.LatestVersion} is available!"
            : "You are running the latest version.";
    }

    // ── Bindable properties ───────────────────────────────────────────────────

    public bool IsCheckingForUpdate
    {
        get => _isCheckingForUpdate;
        set => SetProperty(ref _isCheckingForUpdate, value);
    }

    public bool IsDownloadingUpdate
    {
        get => _isDownloadingUpdate;
        set => SetProperty(ref _isDownloadingUpdate, value);
    }

    public bool IsUpdateAvailable
    {
        get => _isUpdateAvailable;
        set
        {
            if (SetProperty(ref _isUpdateAvailable, value))
                OnPropertyChanged(nameof(IsUpToDate));
        }
    }

    /// <summary>True when checked and no update was found — drives the "up to date" badge.</summary>
    public bool IsUpToDate => !IsUpdateAvailable && !string.IsNullOrEmpty(UpdateStatusMessage);

    public string UpdateStatusMessage
    {
        get => _updateStatusMessage;
        set => SetProperty(ref _updateStatusMessage, value);
    }

    public string UpdateCurrentVersion
    {
        get => _updateCurrentVersion;
        set => SetProperty(ref _updateCurrentVersion, value);
    }

    public string UpdateLatestVersion
    {
        get => _updateLatestVersion;
        set => SetProperty(ref _updateLatestVersion, value);
    }

    public string UpdateReleaseNotes
    {
        get => _updateReleaseNotes;
        set
        {
            if (SetProperty(ref _updateReleaseNotes, value))
                OnPropertyChanged(nameof(HasUpdateReleaseNotes));
        }
    }

    public bool HasUpdateReleaseNotes => !string.IsNullOrEmpty(UpdateReleaseNotes);

    public double UpdateDownloadProgress
    {
        get => _updateDownloadProgress;
        set => SetProperty(ref _updateDownloadProgress, value);
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    /// <summary>Manual "Check for updates" button in Settings.</summary>
    public async Task CheckForAppUpdateAsync()
    {
        if (IsCheckingForUpdate || _updateService == null) return;

        IsCheckingForUpdate = true;
        UpdateStatusMessage = "Checking for updates…";

        try
        {
            var result = await _updateService.CheckAsync();
            ApplyUpdateResult(result);
        }
        catch (Exception ex)
        {
            UpdateStatusMessage = $"Update check failed: {ex.Message}";
        }
        finally
        {
            IsCheckingForUpdate = false;
        }
    }

    /// <summary>Download and install the update.</summary>
    public async Task DownloadAndInstallUpdateAsync()
    {
        if (IsDownloadingUpdate || _updateService == null) return;

        IsDownloadingUpdate = true;
        UpdateDownloadProgress = 0;
        UpdateStatusMessage = "Downloading update…";

        try
        {
            var progress = new Progress<double>(p =>
            {
                UpdateDownloadProgress = p;
            });

            await _updateService.DownloadAndStartUpdateAsync(progress);
        }
        catch (Exception ex)
        {
            UpdateStatusMessage = $"Update failed: {ex.Message}";
            IsDownloadingUpdate = false;
        }
    }

    /// <summary>Opens the download page in the system browser.</summary>
    public void DownloadUpdate() => _updateService?.OpenDownloadPage();
}
