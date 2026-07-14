using System.Diagnostics;
using System.IO;
using System.Windows;
using MinecraftControlHub.AI.Services;
using MinecraftControlHub.Core.Models;
using MinecraftControlHub.Core.Services;

namespace MinecraftControlHub.UI.ViewModels;

public partial class SettingsPageViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly IInstallationService _installationService;
    private readonly IModService _modService;
    private readonly IJavaService _javaService;
    private readonly IMinecraftAccountService _accountService;
    private INexoraAccountService? _nexoraAccountService;

    private bool _isSigningIn;
    private string _deviceCode = string.Empty;
    private string _deviceVerificationUri = string.Empty;
    private string _accountStatus = string.Empty;

    private bool _isBusy;
    private string _statusMessage = string.Empty;

    private string _newInstallationName = string.Empty;
    private string _newMinecraftVersion = string.Empty;
    private LoaderType _newLoader = LoaderType.Fabric;
    private string _javaAdvice = string.Empty;
    private string? _recommendedJavaDownloadUrl;

    public SettingsPageViewModel(
        ISettingsService settingsService,
        IInstallationService installationService,
        IModService modService,
        IJavaService javaService,
        IMinecraftAccountService accountService,
        INexoraAccountService nexoraAccountService,
        IAIService aiService)
    {
        _settingsService = settingsService;
        _installationService = installationService;
        _modService = modService;
        _javaService = javaService;
        _accountService = accountService;
        _nexoraAccountService = nexoraAccountService;
        _aiService = aiService;
        _accountService.AccountChanged += (_, _) => RaiseAccountProps();
    }

    // ---- Microsoft account ----

    public bool IsSignedIn => _accountService.Current is { } a && !string.IsNullOrEmpty(a.Username);

    public bool IsSignedOut => !IsSignedIn;

    public string AccountName => _accountService.Current?.Username ?? string.Empty;

    public bool IsSigningIn
    {
        get => _isSigningIn;
        set => SetProperty(ref _isSigningIn, value);
    }

    public string DeviceCode
    {
        get => _deviceCode;
        set
        {
            if (SetProperty(ref _deviceCode, value))
                OnPropertyChanged(nameof(HasDeviceCode));
        }
    }

    public bool HasDeviceCode => !string.IsNullOrEmpty(DeviceCode);

    public string DeviceVerificationUri
    {
        get => _deviceVerificationUri;
        set => SetProperty(ref _deviceVerificationUri, value);
    }

    public string AccountStatus
    {
        get => _accountStatus;
        set
        {
            if (SetProperty(ref _accountStatus, value))
                OnPropertyChanged(nameof(HasAccountStatus));
        }
    }

    public bool HasAccountStatus => !string.IsNullOrEmpty(AccountStatus);

    public async Task SignInAsync()
    {
        if (IsSigningIn) return;

        IsSigningIn = true;
        AccountStatus = string.Empty;
        DeviceCode = string.Empty;
        DeviceVerificationUri = string.Empty;

        var progress = new Progress<DeviceCodeInfo>(info =>
        {
            DeviceCode = info.UserCode;
            DeviceVerificationUri = info.VerificationUri;
            AccountStatus = $"Open {info.VerificationUri} and enter the code below to sign in.";

            // Open the Microsoft sign-in page automatically for convenience.
            try
            {
                Process.Start(new ProcessStartInfo(info.VerificationUri) { UseShellExecute = true });
            }
            catch { /* user can open it manually */ }
        });

        var result = await _accountService.SignInAsync(progress);

        IsSigningIn = false;
        DeviceCode = string.Empty;
        DeviceVerificationUri = string.Empty;

        AccountStatus = result.Success
            ? $"Signed in as {result.Account?.Username}."
            : (result.Error ?? "Sign-in failed.");

        RaiseAccountProps();
    }

    public void SignOut()
    {
        _accountService.SignOut();
        AccountStatus = "Signed out.";
        RaiseAccountProps();
    }

    public void CopyDeviceCode()
    {
        if (!string.IsNullOrEmpty(DeviceCode))
        {
            try { Clipboard.SetText(DeviceCode); } catch { /* ignore */ }
        }
    }

    private void RaiseAccountProps()
    {
        OnPropertyChanged(nameof(IsSignedIn));
        OnPropertyChanged(nameof(IsSignedOut));
        OnPropertyChanged(nameof(AccountName));
    }

    // ---- Persisted preference toggles ----

    public bool AutoUpdateMods
    {
        get => _settingsService.Settings.AutoUpdateMods;
        set
        {
            if (_settingsService.Settings.AutoUpdateMods == value) return;
            _settingsService.Settings.AutoUpdateMods = value;
            OnPropertyChanged();
            _ = _settingsService.SaveAsync();
        }
    }

    public bool CheckForUpdatesOnStartup
    {
        get => _settingsService.Settings.CheckForUpdatesOnStartup;
        set
        {
            if (_settingsService.Settings.CheckForUpdatesOnStartup == value) return;
            _settingsService.Settings.CheckForUpdatesOnStartup = value;
            OnPropertyChanged();
            _ = _settingsService.SaveAsync();
        }
    }

    public bool KeepModsBackup
    {
        get => _settingsService.Settings.KeepModsBackup;
        set
        {
            if (_settingsService.Settings.KeepModsBackup == value) return;
            _settingsService.Settings.KeepModsBackup = value;
            OnPropertyChanged();
            _ = _settingsService.SaveAsync();
        }
    }
 
    /// <summary>
    /// In-game player name used when launching in offline mode (no Microsoft login).
    /// </summary>
    public string OfflineUsername
    {
        get => _settingsService.Settings.OfflineUsername;
        set
        {
            if (_settingsService.Settings.OfflineUsername == value) return;
            _settingsService.Settings.OfflineUsername = value;
            OnPropertyChanged();
            _ = _settingsService.SaveAsync();
        }
    }


    // ---- New-installation fields ----

    public List<LoaderType> Loaders { get; } = Enum.GetValues<LoaderType>().ToList();

    public string NewInstallationName
    {
        get => _newInstallationName;
        set => SetProperty(ref _newInstallationName, value);
    }

    public string NewMinecraftVersion
    {
        get => _newMinecraftVersion;
        set
        {
            if (SetProperty(ref _newMinecraftVersion, value))
                _ = RefreshJavaAdviceAsync();
        }
    }

    public LoaderType NewLoader
    {
        get => _newLoader;
        set => SetProperty(ref _newLoader, value);
    }

    public string JavaAdvice
    {
        get => _javaAdvice;
        set
        {
            if (SetProperty(ref _javaAdvice, value))
                OnPropertyChanged(nameof(HasJavaAdvice));
        }
    }

    public bool HasJavaAdvice => !string.IsNullOrEmpty(JavaAdvice);

    // ---- State ----

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            if (SetProperty(ref _statusMessage, value))
                OnPropertyChanged(nameof(HasStatus));
        }
    }

    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);

    public bool LauncherDetected => _installationService.LauncherDetected;

    // ---- Actions ----

    /// <summary>
    /// Recomputes which Java version the entered Minecraft version needs and whether a
    /// compatible runtime is already present on this PC.
    /// </summary>
    public async Task RefreshJavaAdviceAsync()
    {
        if (string.IsNullOrWhiteSpace(NewMinecraftVersion))
        {
            JavaAdvice = string.Empty;
            _recommendedJavaDownloadUrl = null;
            return;
        }

        try
        {
            var recommendation = await _javaService.GetRecommendationAsync(NewMinecraftVersion);
            JavaAdvice = recommendation.Summary;
            _recommendedJavaDownloadUrl = recommendation.IsSatisfied ? null : recommendation.DownloadUrl;
        }
        catch (Exception ex)
        {
            JavaAdvice = $"Could not determine the Java version: {ex.Message}";
            _recommendedJavaDownloadUrl = null;
        }
    }

    public async Task CreateInstallationAsync()
    {
        if (IsBusy) return;

        if (string.IsNullOrWhiteSpace(NewInstallationName) || string.IsNullOrWhiteSpace(NewMinecraftVersion))
        {
            StatusMessage = "Please enter a name and a Minecraft version for the new installation.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Creating installation…";
        try
        {
            var recommendation = await _javaService.GetRecommendationAsync(NewMinecraftVersion);

            var installation = new Installation
            {
                Name = NewInstallationName.Trim(),
                MinecraftVersion = NewMinecraftVersion.Trim(),
                Loader = NewLoader,
                // Use the auto-detected compatible Java if we found one.
                JavaPath = recommendation.MatchingJava?.Path
            };

            await _installationService.CreateInstallationAsync(installation);

            StatusMessage = $"Created \"{installation.Name}\". {recommendation.Summary}";

            // Clear the form for the next entry.
            NewInstallationName = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not create installation: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Opens the recommended Java download page, or Adoptium's home as a fallback.</summary>
    public void OpenJavaDownload()
    {
        TryOpenUrl(_recommendedJavaDownloadUrl ?? "https://adoptium.net/");
    }

    /// <summary>Opens the app's root data folder (installations, cached game files,
    /// settings) in Windows Explorer, so mods/resourcepacks/etc. can be dropped in by hand.</summary>
    public void OpenDataFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo(AppPaths.DataRoot) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not open the data folder: {ex.Message}";
        }
    }

    /// <summary>Opens the diagnostics log (install/update/dependency-check decisions,
    /// including exactly what each "Fix" button did) in the default text viewer. Creates
    /// an empty file first if nothing has been logged yet this run, so the open never
    /// just silently fails on a missing file.</summary>
    public void OpenDiagnosticsLog()
    {
        try
        {
            var path = AppPaths.DiagnosticsLogFile;
            if (!File.Exists(path))
                File.WriteAllText(path, string.Empty);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not open the diagnostics log: {ex.Message}";
        }
    }

    public async Task ImportInstallationsAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = "Importing installations from the Minecraft launcher…";
        try
        {
            var count = await _installationService.ImportLauncherProfilesAsync();
            StatusMessage = count > 0
                ? $"Imported {count} installation(s) from the Minecraft launcher."
                : LauncherDetected
                    ? "No new installations found (they may already be imported)."
                    : "No Minecraft launcher was found on this PC.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Import failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task CheckForUpdatesAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = "Checking installed mods for updates…";
        try
        {
            var installations = await _installationService.GetAllInstallationsAsync();
            var result = await _modService.CheckForUpdatesAsync(installations, AutoUpdateMods);
            StatusMessage = result.Summary;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Update check failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Wipes every stored setting, installation, mod, account and cached game file,
    /// returning the app to a first-run state. The caller is expected to restart the
    /// app afterwards because services keep their state in memory.
    /// </summary>
    public void ResetApp()
    {
        _accountService.SignOut();
        AppPaths.ClearAllData();
    }

    private static void TryOpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Open URL failed: {ex.Message}");
        }
    }
}
