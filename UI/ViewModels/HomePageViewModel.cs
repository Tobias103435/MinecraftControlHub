using System.Diagnostics;
using MinecraftControlHub.Core.Models;
using MinecraftControlHub.Core.Services;

namespace MinecraftControlHub.UI.ViewModels;

public class HomePageViewModel : ViewModelBase
{
    private readonly IInstallationService _installationService;
    private readonly IJavaService _javaService;
    private readonly IMinecraftLauncherService _launcherService;
    private readonly ISettingsService _settingsService;
    private readonly IMinecraftAccountService _accountService;
    private readonly ILoaderService _loaderService;
    private readonly IModService _modService;
    private readonly IModpackExportImportService _modpackService;
    private readonly INexoraAccountService       _nexoraService;
    private readonly IInstanceShareService       _instanceShare;
    private readonly IInstanceNotificationManager _instanceNotifManager;
    private readonly HealthCheckService _healthCheckService;
    private List<Installation> _installations;
    private bool _isLoading;

    /// <summary>Sentinel shown/selected in the loader-version dropdown meaning "always resolve the newest compatible build at launch" (previous default behavior) rather than a pinned exact version.</summary>
    public const string LatestLoaderVersionLabel = "Latest (recommended)";

    private List<McVersionEntry> _allMcVersions = new();
    private bool _mcVersionsLoaded;
    private bool _showSnapshots;
    private List<string> _filteredMinecraftVersions = new();
    private List<string> _availableLoaderVersions = new() { LatestLoaderVersionLabel };
    private string _newLoaderVersion = LatestLoaderVersionLabel;

    private bool _isLaunchVisible;
    private string _launchStage = string.Empty;
    private double _launchPercent;
    private bool _launchIndeterminate = true;
    private bool _launchFinished;
    private string _launchMessage = string.Empty;

    private bool _isCreateVisible;
    private bool _isBusy;

    // ---- Installation detail / settings overlay ----
    private bool _isDetailVisible;
    private bool _isDetailBusy;
    private Installation? _detailInstallation;
    private string _detailName = string.Empty;
    private string _detailMinecraftVersion = string.Empty;
    private LoaderType _detailLoader;
    private string _detailLoaderVersion = string.Empty;
    private string _detailMaxMemoryMB = string.Empty;
    private string _detailMinMemoryMB = string.Empty;
    private string _detailCustomJvmArgs = string.Empty;
    private bool   _detailPinMinecraftVersion;
    private bool   _detailJavaOk = true;
    private string _detailJavaHealth = string.Empty;
    private string _detailJavaPath   = string.Empty;

    // ---- Instance sharing overlay ----
    private bool         _isShareVisible;
    private bool         _isShareBusy;
    private string       _shareStatus       = string.Empty;
    private string       _shareCode         = string.Empty;
    private string       _importCode        = string.Empty;
    private string       _shareTab          = "Friends"; // "Friends" | "Code"
    private Installation? _sharingInstallation;
    private System.Collections.ObjectModel.ObservableCollection<FriendShareRow>
        _shareableFriends = new();
    private string _detailStatus = string.Empty;
    private List<Mod> _detailMods = new();
    // ---- RAM calculator (Advanced tab) ----
    private string _detailRenderDistance = RamCalculatorService.BaselineRenderDistance.ToString();
    private string _detailRamRecommendation = string.Empty;
    // ---- Settings overlay right-hand nav (Home / Mods / Advanced) ----
    private string _detailSection = "Home";
    // ---- Mods tab: version-picker rows + update/dependency check results ----
    private List<InstalledModRowViewModel> _detailModRows = new();
    private bool _isCheckingModUpdates;
    private string _detailUpdateStatus = string.Empty;
    private bool _isCheckingDependencies;
    private List<DependencyIssue> _detailDependencyIssues = new();
    private string _detailDependencyStatus = string.Empty;
    // ---- Export to .mrpack (Prism Launcher / Modrinth compatible) ----
    private bool _isExportingDetail;
    private string _detailExportStatus = string.Empty;
    // ---- Export to Prism Launcher native zip ----
    private bool _isExportingPrismDetail;
    private string _detailPrismExportStatus = string.Empty;

    // ---- Screenshots ----
    private List<string> _detailScreenshots = new();

    // ---- Health check ----
    private HealthCheckResult? _detailHealthCheck;
    private bool _isLoadingHealth;

    private bool _isLoginVisible;
    private bool _isSigningIn;
    private string _deviceCode = string.Empty;
    private string _deviceVerificationUri = string.Empty;
    private string _loginStatus = string.Empty;
    private Installation? _pendingInstallation;
    private string _newInstallationName = string.Empty;
    private string _newMinecraftVersion = string.Empty;
    private LoaderType _newLoader = LoaderType.Fabric;
    private string _javaAdvice = string.Empty;
    private string _createStatus = string.Empty;
    private bool _isImportingCreate;
    private List<InstallationCardViewModel> _installationCards = new();

    public List<Installation> Installations
    {
        get => _installations;
        set => SetProperty(ref _installations, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public bool HasInstallations => Installations.Count > 0;

    public List<InstallationCardViewModel> InstallationCards
    {
        get => _installationCards;
        private set => SetProperty(ref _installationCards, value);
    }

    // ---- New-installation dialog ----

    public List<LoaderType> Loaders { get; } = Enum.GetValues<LoaderType>().ToList();

    public bool IsCreateVisible
    {
        get => _isCreateVisible;
        set => SetProperty(ref _isCreateVisible, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
                OnPropertyChanged(nameof(IsCreateDialogBusy));
        }
    }

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
            {
                _ = RefreshJavaAdviceAsync();
                RefreshFilteredMinecraftVersions();
                _ = RefreshLoaderVersionsAsync();
            }
        }
    }

    public LoaderType NewLoader
    {
        get => _newLoader;
        set
        {
            if (SetProperty(ref _newLoader, value))
                _ = RefreshLoaderVersionsAsync();
        }
    }

    /// <summary>When true, the Minecraft version picker also includes snapshots (not just full releases).</summary>
    public bool ShowSnapshots
    {
        get => _showSnapshots;
        set
        {
            if (SetProperty(ref _showSnapshots, value))
                RefreshFilteredMinecraftVersions();
        }
    }

    /// <summary>The version picker's dropdown contents — filtered by the current typed text and by <see cref="ShowSnapshots"/>, newest first.</summary>
    public List<string> FilteredMinecraftVersions
    {
        get => _filteredMinecraftVersions;
        private set => SetProperty(ref _filteredMinecraftVersions, value);
    }

    /// <summary>Available builds of the currently selected loader for the currently entered Minecraft version, newest first, with "Latest (recommended)" always first.</summary>
    public List<string> AvailableLoaderVersions
    {
        get => _availableLoaderVersions;
        private set => SetProperty(ref _availableLoaderVersions, value);
    }

    /// <summary>The picked loader build, or <see cref="LatestLoaderVersionLabel"/> to always use the newest compatible one at launch time.</summary>
    public string NewLoaderVersion
    {
        get => _newLoaderVersion;
        set => SetProperty(ref _newLoaderVersion, value);
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

    public string CreateStatus
    {
        get => _createStatus;
        set
        {
            if (SetProperty(ref _createStatus, value))
                OnPropertyChanged(nameof(HasCreateStatus));
        }
    }

    public bool HasCreateStatus => !string.IsNullOrEmpty(CreateStatus);

    /// <summary>True while a .mrpack is being downloaded/unpacked into a new installation.</summary>
    public bool IsImportingCreate
    {
        get => _isImportingCreate;
        private set
        {
            if (SetProperty(ref _isImportingCreate, value))
                OnPropertyChanged(nameof(IsCreateDialogBusy));
        }
    }

    /// <summary>True while either creating or importing — used to disable the dialog's buttons so the two flows can't overlap.</summary>
    public bool IsCreateDialogBusy => IsBusy || IsImportingCreate;

    // ---- Installation detail / settings overlay ----

    public bool IsDetailVisible
    {
        get => _isDetailVisible;
        set => SetProperty(ref _isDetailVisible, value);
    }

    public bool IsDetailBusy
    {
        get => _isDetailBusy;
        set => SetProperty(ref _isDetailBusy, value);
    }

    public Installation? DetailInstallation
    {
        get => _detailInstallation;
        private set => SetProperty(ref _detailInstallation, value);
    }

    public string DetailName
    {
        get => _detailName;
        set => SetProperty(ref _detailName, value);
    }

    public string DetailMinecraftVersion
    {
        get => _detailMinecraftVersion;
        set => SetProperty(ref _detailMinecraftVersion, value);
    }

    public LoaderType DetailLoader
    {
        get => _detailLoader;
        set => SetProperty(ref _detailLoader, value);
    }

    /// <summary>Pinned exact loader build, or blank to always use the newest compatible build at launch.</summary>
    public string DetailLoaderVersion
    {
        get => _detailLoaderVersion;
        set => SetProperty(ref _detailLoaderVersion, value);
    }

    public string DetailMaxMemoryMB
    {
        get => _detailMaxMemoryMB;
        set => SetProperty(ref _detailMaxMemoryMB, value);
    }

    public string DetailMinMemoryMB
    {
        get => _detailMinMemoryMB;
        set => SetProperty(ref _detailMinMemoryMB, value);
    }

    public string DetailCustomJvmArgs
    {
        get => _detailCustomJvmArgs;
        set => SetProperty(ref _detailCustomJvmArgs, value);
    }

    public bool DetailPinMinecraftVersion
    {
        get => _detailPinMinecraftVersion;
        set => SetProperty(ref _detailPinMinecraftVersion, value);
    }

    public bool DetailJavaOk
    {
        get => _detailJavaOk;
        set => SetProperty(ref _detailJavaOk, value);
    }

    public string DetailJavaHealth
    {
        get => _detailJavaHealth;
        set => SetProperty(ref _detailJavaHealth, value);
    }

    public string DetailJavaPath
    {
        get => _detailJavaPath;
        set => SetProperty(ref _detailJavaPath, value);
    }

    /// <summary>Render distance (chunks) fed into the RAM calculator — this app doesn't
    /// manage in-game video settings, it's purely an input for the estimate below.</summary>
    public string DetailRenderDistance
    {
        get => _detailRenderDistance;
        set => SetProperty(ref _detailRenderDistance, value);
    }

    /// <summary>Human-readable "recommended Min/Max RAM + why" text shown under the RAM calculator button.</summary>
    public string DetailRamRecommendation
    {
        get => _detailRamRecommendation;
        set
        {
            if (SetProperty(ref _detailRamRecommendation, value))
                OnPropertyChanged(nameof(HasDetailRamRecommendation));
        }
    }

    public bool HasDetailRamRecommendation => !string.IsNullOrEmpty(DetailRamRecommendation);

    public string DetailStatus
    {
        get => _detailStatus;
        set
        {
            if (SetProperty(ref _detailStatus, value))
                OnPropertyChanged(nameof(HasDetailStatus));
        }
    }

    public bool HasDetailStatus => !string.IsNullOrEmpty(DetailStatus);

    public List<Mod> DetailMods
    {
        get => _detailMods;
        private set
        {
            if (SetProperty(ref _detailMods, value))
                OnPropertyChanged(nameof(HasDetailMods));
        }
    }

    public bool HasDetailMods => DetailMods.Count > 0;

    // ---- Settings overlay right-hand nav (Home / Mods / Advanced) ----

    /// <summary>Which section of the Settings overlay is currently shown: "Home", "Mods", or "Advanced".</summary>
    public string DetailSection
    {
        get => _detailSection;
        set
        {
            if (SetProperty(ref _detailSection, value))
            {
                OnPropertyChanged(nameof(IsHomeSection));
                OnPropertyChanged(nameof(IsModsSection));
                OnPropertyChanged(nameof(IsAdvancedSection));
                OnPropertyChanged(nameof(IsContentSection));
                OnPropertyChanged(nameof(IsServerSyncSection));
                OnPropertyChanged(nameof(IsScreenshotsSection));
                OnPropertyChanged(nameof(IsHealthSection));
            }
        }
    }

    public bool IsHomeSection        => DetailSection == "Home";
    public bool IsModsSection        => DetailSection == "Mods";
    public bool IsAdvancedSection    => DetailSection == "Advanced";
    public bool IsContentSection     => DetailSection == "Content";
    public bool IsServerSyncSection  => DetailSection == "ServerSync";
    public bool IsScreenshotsSection => DetailSection == "Screenshots";
    public bool IsHealthSection      => DetailSection == "Health";

    // ---- Mods tab: per-mod rows (name/version/loader + version-picker dropdown) ----

    public List<InstalledModRowViewModel> DetailModRows
    {
        get => _detailModRows;
        private set => SetProperty(ref _detailModRows, value);
    }

    public List<string> DetailScreenshots
    {
        get => _detailScreenshots;
        private set => SetProperty(ref _detailScreenshots, value);
    }

    public HealthCheckResult? DetailHealthCheck
    {
        get => _detailHealthCheck;
        private set => SetProperty(ref _detailHealthCheck, value);
    }

    public bool IsLoadingHealth
    {
        get => _isLoadingHealth;
        private set => SetProperty(ref _isLoadingHealth, value);
    }

    public bool IsCheckingModUpdates
    {
        get => _isCheckingModUpdates;
        private set => SetProperty(ref _isCheckingModUpdates, value);
    }

    public string DetailUpdateStatus
    {
        get => _detailUpdateStatus;
        private set
        {
            if (SetProperty(ref _detailUpdateStatus, value))
                OnPropertyChanged(nameof(HasDetailUpdateStatus));
        }
    }

    public bool HasDetailUpdateStatus => !string.IsNullOrEmpty(DetailUpdateStatus);

    public bool IsCheckingDependencies
    {
        get => _isCheckingDependencies;
        private set => SetProperty(ref _isCheckingDependencies, value);
    }

    public List<DependencyIssue> DetailDependencyIssues
    {
        get => _detailDependencyIssues;
        private set
        {
            if (SetProperty(ref _detailDependencyIssues, value))
                OnPropertyChanged(nameof(HasDetailDependencyIssues));
        }
    }

    public bool HasDetailDependencyIssues => DetailDependencyIssues.Count > 0;

    public string DetailDependencyStatus
    {
        get => _detailDependencyStatus;
        private set
        {
            if (SetProperty(ref _detailDependencyStatus, value))
                OnPropertyChanged(nameof(HasDetailDependencyStatus));
        }
    }

    public bool HasDetailDependencyStatus => !string.IsNullOrEmpty(DetailDependencyStatus);

    // ---- Export to .mrpack (Prism Launcher / Modrinth compatible) ----

    /// <summary>True while this installation is being packed into a .mrpack file.</summary>
    public bool IsExportingDetail
    {
        get => _isExportingDetail;
        private set => SetProperty(ref _isExportingDetail, value);
    }

    public string DetailExportStatus
    {
        get => _detailExportStatus;
        private set
        {
            if (SetProperty(ref _detailExportStatus, value))
                OnPropertyChanged(nameof(HasDetailExportStatus));
        }
    }

    public bool HasDetailExportStatus => !string.IsNullOrEmpty(DetailExportStatus);

    public bool IsExportingPrismDetail
    {
        get => _isExportingPrismDetail;
        private set => SetProperty(ref _isExportingPrismDetail, value);
    }

    public string DetailPrismExportStatus
    {
        get => _detailPrismExportStatus;
        private set
        {
            if (SetProperty(ref _detailPrismExportStatus, value))
                OnPropertyChanged(nameof(HasDetailPrismExportStatus));
        }
    }

    public bool HasDetailPrismExportStatus => !string.IsNullOrEmpty(DetailPrismExportStatus);

    // ---- Launch progress overlay ----

    public bool IsLaunchVisible
    {
        get => _isLaunchVisible;
        set => SetProperty(ref _isLaunchVisible, value);
    }

    public string LaunchStage
    {
        get => _launchStage;
        set => SetProperty(ref _launchStage, value);
    }

    public double LaunchPercent
    {
        get => _launchPercent;
        set => SetProperty(ref _launchPercent, value);
    }

    public bool LaunchIndeterminate
    {
        get => _launchIndeterminate;
        set => SetProperty(ref _launchIndeterminate, value);
    }

    public bool LaunchFinished
    {
        get => _launchFinished;
        set => SetProperty(ref _launchFinished, value);
    }

    public string LaunchMessage
    {
        get => _launchMessage;
        set
        {
            if (SetProperty(ref _launchMessage, value))
                OnPropertyChanged(nameof(HasLaunchMessage));
        }
    }

    public bool HasLaunchMessage => !string.IsNullOrEmpty(LaunchMessage);

    public HomePageViewModel(
        IInstallationService installationService,
        IJavaService javaService,
        IMinecraftLauncherService launcherService,
        ISettingsService settingsService,
        IMinecraftAccountService accountService,
        ILoaderService loaderService,
        IModService modService,
        IModpackExportImportService modpackService,
        INexoraAccountService nexoraService,
        IInstanceShareService instanceShare,
        IInstanceNotificationManager instanceNotifManager,
        HealthCheckService healthCheckService)
    {
        _installationService  = installationService;
        _javaService          = javaService;
        _launcherService      = launcherService;
        _settingsService      = settingsService;
        _accountService       = accountService;
        _loaderService        = loaderService;
        _modService           = modService;
        _modpackService       = modpackService;
        _nexoraService        = nexoraService;
        _instanceShare        = instanceShare;
        _instanceNotifManager = instanceNotifManager;
        _healthCheckService   = healthCheckService;
        _installations        = new List<Installation>();
        _ = LoadInstallationsAsync();

        _installationService.InstallationsChanged += (_, _) =>
            System.Windows.Application.Current.Dispatcher.InvokeAsync(
                () => _ = LoadInstallationsAsync());

        // React to incoming share notifications
        _instanceNotifManager.Changed += (_, _) =>
        {
            OnPropertyChanged(nameof(InstanceShareUnreadCount));
            OnPropertyChanged(nameof(HasInstanceShareNotifications));
            OnPropertyChanged(nameof(InstanceShareNotifications));
        };
    }

    public void CloseLaunchOverlay()
    {
        IsLaunchVisible = false;
    }

    // ---- Login overlay (shown when Play is pressed while not signed in) ----

    public bool IsLoginVisible
    {
        get => _isLoginVisible;
        set => SetProperty(ref _isLoginVisible, value);
    }

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

    public string LoginStatus
    {
        get => _loginStatus;
        set
        {
            if (SetProperty(ref _loginStatus, value))
                OnPropertyChanged(nameof(HasLoginStatus));
        }
    }

    public bool HasLoginStatus => !string.IsNullOrEmpty(LoginStatus);

    /// <summary>Offline player name, persisted to settings. Used when launching offline.</summary>
    public string OfflinePlayerName
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

    public void CloseLoginOverlay()
    {
        IsLoginVisible = false;
    }

    /// <summary>Starts the Microsoft device-code sign-in from the login overlay,
    /// then launches the pending installation online when it succeeds.</summary>
    public async Task SignInAndPlayAsync()
    {
        if (IsSigningIn) return;

        IsSigningIn = true;
        LoginStatus = "Starting Microsoft sign-in…";
        DeviceCode = string.Empty;
        DeviceVerificationUri = string.Empty;

        var progress = new Progress<DeviceCodeInfo>(info =>
        {
            DeviceCode = info.UserCode;
            DeviceVerificationUri = info.VerificationUri;
            LoginStatus = $"Open {info.VerificationUri} and enter the code below to sign in.";
            try
            {
                Process.Start(new ProcessStartInfo(info.VerificationUri) { UseShellExecute = true });
            }
            catch { /* user can open it manually */ }
        });

        AccountResult result;
        try
        {
            result = await _accountService.SignInAsync(progress);
        }
        catch (Exception ex)
        {
            IsSigningIn = false;
            DeviceCode = string.Empty;
            DeviceVerificationUri = string.Empty;
            LoginStatus = $"❌ Sign-in failed: {ex.Message}";
            return;
        }

        IsSigningIn = false;
        DeviceCode = string.Empty;
        DeviceVerificationUri = string.Empty;

        if (result.Success)
        {
            // Clear success feedback, then auto-start the pending installation online.
            var account = await _accountService.GetValidAccountAsync();
            LoginStatus = $"✔ Signed in as {account?.Username ?? "your account"} — starting Minecraft…";
            IsLoginVisible = false;
            LoginStatus = string.Empty;
            if (_pendingInstallation != null)
                await LaunchAsync(_pendingInstallation, account);
        }
        else
        {
            LoginStatus = $"❌ {result.Error ?? "Sign-in failed. Please try again."}";
        }
    }

    /// <summary>Launches the pending installation in offline mode with the entered player name.</summary>
    public async Task PlayOfflineAsync()
    {
        IsLoginVisible = false;
        if (_pendingInstallation != null)
            await LaunchAsync(_pendingInstallation, null);
    }

    public void OpenCreateDialog()
    {
        NewInstallationName = string.Empty;
        ShowSnapshots = false;
        NewMinecraftVersion = string.Empty;
        NewLoader = LoaderType.Fabric;
        JavaAdvice = string.Empty;
        CreateStatus = string.Empty;
        IsCreateVisible = true;
        _ = EnsureMinecraftVersionsLoadedAsync();
    }

    /// <summary>Loads the full Mojang version list once (cached for the rest of the session) and refreshes the filtered picker.</summary>
    private async Task EnsureMinecraftVersionsLoadedAsync()
    {
        if (!_mcVersionsLoaded)
        {
            try
            {
                _allMcVersions = await _loaderService.GetMinecraftVersionsAsync();
                _mcVersionsLoaded = true;
            }
            catch
            {
                _allMcVersions = new List<McVersionEntry>();
            }
        }
        RefreshFilteredMinecraftVersions();
    }

    /// <summary>Recomputes <see cref="FilteredMinecraftVersions"/> from the cached version list, the release/snapshot toggle, and whatever's currently typed — so the dropdown narrows down and re-sorts as you type.</summary>
    private void RefreshFilteredMinecraftVersions()
    {
        IEnumerable<McVersionEntry> pool = _allMcVersions;
        if (!ShowSnapshots)
            pool = pool.Where(v => v.IsRelease);

        var filter = NewMinecraftVersion?.Trim() ?? string.Empty;
        if (!string.IsNullOrEmpty(filter))
            pool = pool.Where(v => v.Id.Contains(filter, StringComparison.OrdinalIgnoreCase));

        // Mojang's manifest is already newest-first, so this stays newest-first too.
        FilteredMinecraftVersions = pool.Select(v => v.Id).ToList();
    }

    /// <summary>Refreshes the loader-version picker for the currently selected loader + Minecraft version. Vanilla has no loader versions.</summary>
    private async Task RefreshLoaderVersionsAsync()
    {
        var mc = NewMinecraftVersion?.Trim() ?? string.Empty;
        if (NewLoader == LoaderType.Vanilla || string.IsNullOrWhiteSpace(mc))
        {
            AvailableLoaderVersions = new List<string> { LatestLoaderVersionLabel };
            NewLoaderVersion = LatestLoaderVersionLabel;
            return;
        }

        try
        {
            var versions = await _loaderService.GetAvailableLoaderVersionsAsync(NewLoader, mc);
            var list = new List<string> { LatestLoaderVersionLabel };
            list.AddRange(versions);
            AvailableLoaderVersions = list;
        }
        catch
        {
            AvailableLoaderVersions = new List<string> { LatestLoaderVersionLabel };
        }
        // Always reset to "Latest" when the loader/version combo changes — a previously
        // pinned build could be meaningless (or simply absent) for the new selection.
        NewLoaderVersion = LatestLoaderVersionLabel;
    }

    public void CloseCreateDialog()
    {
        IsCreateVisible = false;
    }

    private async Task RefreshJavaAdviceAsync()
    {
        if (string.IsNullOrWhiteSpace(NewMinecraftVersion))
        {
            JavaAdvice = string.Empty;
            return;
        }

        try
        {
            var recommendation = await _javaService.GetRecommendationAsync(NewMinecraftVersion);
            JavaAdvice = recommendation.Summary;
        }
        catch (Exception ex)
        {
            JavaAdvice = $"Could not determine the Java version: {ex.Message}";
        }
    }

    public async Task CreateInstallationAsync()
    {
        if (IsBusy || IsImportingCreate) return;

        if (string.IsNullOrWhiteSpace(NewInstallationName) || string.IsNullOrWhiteSpace(NewMinecraftVersion))
        {
            CreateStatus = "Please enter a name and a Minecraft version.";
            return;
        }

        IsBusy = true;
        CreateStatus = "Creating installation…";
        try
        {
            var recommendation = await _javaService.GetRecommendationAsync(NewMinecraftVersion);

            var installation = new Installation
            {
                Name = NewInstallationName.Trim(),
                MinecraftVersion = NewMinecraftVersion.Trim(),
                Loader = NewLoader,
                LoaderVersion = NewLoaderVersion == LatestLoaderVersionLabel ? null : NewLoaderVersion,
                JavaPath = recommendation.MatchingJava?.Path
            };

            await _installationService.CreateInstallationAsync(installation);
            await LoadInstallationsAsync();

            IsCreateVisible = false;
        }
        catch (Exception ex)
        {
            CreateStatus = $"Could not create installation: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Imports a .mrpack file (a Prism Launcher / Modrinth App compatible modpack —
    /// including one this app itself exported) as a brand-new installation: creates
    /// it, downloads every declared mod, and applies any bundled overrides
    /// (config/resourcepacks/shaderpacks/side-loaded mods). Reuses the New
    /// Installation dialog's busy/status fields since it's the same dialog.
    /// </summary>
    public async Task ImportInstallationAsync(string mrpackFilePath)
    {
        if (IsBusy || IsImportingCreate) return;

        IsImportingCreate = true;
        IsCreateVisible = true;         // open de overlay zodat progress zichtbaar is
        CreateStatus = "Importing modpack…";
        try
        {
            var progress = new Progress<string>(stage => CreateStatus = stage);
            var result = await _modpackService.ImportAsync(mrpackFilePath, progress);

            if (!result.Success)
            {
                CreateStatus = result.Error ?? "Could not import that file.";
                return;
            }

            await LoadInstallationsAsync();
            IsCreateVisible = false;
        }
        catch (Exception ex)
        {
            CreateStatus = $"Could not import modpack: {ex.Message}";
        }
        finally
        {
            IsImportingCreate = false;
        }
    }

    private async Task LoadInstallationsAsync()
    {
        IsLoading = true;
        try
        {
            Installations = await _installationService.GetAllInstallationsAsync();
            InstallationCards = Installations
                .Select(inst => new InstallationCardViewModel(inst, _modService, _javaService))
                .ToList();
            OnPropertyChanged(nameof(HasInstallations));
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task PlayInstallationAsync(Installation installation)
    {
        if (IsLaunchVisible && !LaunchFinished)
            return;

        // If a Microsoft account is available (online mode) launch straight away;
        // otherwise show the login overlay so the user can sign in or play offline.
        var account = await _accountService.GetValidAccountAsync();
        if (account == null)
        {
            _pendingInstallation = installation;
            DeviceCode = string.Empty;
            DeviceVerificationUri = string.Empty;
            LoginStatus = string.Empty;
            IsSigningIn = false;
            OnPropertyChanged(nameof(OfflinePlayerName));
            IsLoginVisible = true;
            return;
        }

        await LaunchAsync(installation, account);
    }

    private async Task LaunchAsync(Installation installation, MinecraftAccount? account)
    {
        if (IsLaunchVisible && !LaunchFinished)
            return;

        LaunchFinished = false;
        LaunchIndeterminate = true;
        LaunchPercent = 0;
        LaunchStage = "Preparing…";
        LaunchMessage = string.Empty;
        IsLaunchVisible = true;

        var progress = new Progress<LaunchProgress>(p =>
        {
            LaunchStage = p.Stage;
            if (p.Percent.HasValue)
            {
                LaunchIndeterminate = false;
                LaunchPercent = p.Percent.Value;
            }
            else
            {
                LaunchIndeterminate = true;
            }
        });

        var username = _settingsService.Settings.OfflineUsername;
        var result = await _launcherService.LaunchAsync(installation, username, account, progress);

        LaunchFinished = true;
        LaunchIndeterminate = false;
        LaunchPercent = 100;

        if (result.Success)
        {
            LaunchStage = "Minecraft is starting!";
            LaunchMessage = $"{installation.Name} launched successfully. You can close this window.";
            installation.LastPlayed = DateTime.UtcNow;
            await _installationService.UpdateInstallationAsync(installation);
            await LoadInstallationsAsync();
        }
        else
        {
            LaunchStage = "Launch failed";
            LaunchMessage = result.Error ?? "An unknown error occurred while launching.";
        }
    }

    public async Task DeleteInstallationAsync(Installation installation)
    {
        await _installationService.DeleteInstallationAsync(installation.Id);
        await LoadInstallationsAsync();
    }

    // ---- Installation detail / settings overlay ----

    /// <summary>Opens the per-installation settings overlay (General settings, Mods, Advanced) for the given installation.</summary>
    public async Task OpenInstallationDetailAsync(Installation installation)
    {
        DetailInstallation = installation;
        DetailName = installation.Name;
        DetailMinecraftVersion = installation.MinecraftVersion;
        DetailLoader = installation.Loader;
        DetailLoaderVersion = installation.LoaderVersion ?? string.Empty;
        DetailMaxMemoryMB = installation.MaxMemoryMB?.ToString() ?? string.Empty;
        DetailMinMemoryMB = installation.MinMemoryMB?.ToString() ?? string.Empty;
        DetailCustomJvmArgs       = installation.CustomJvmArgs ?? string.Empty;
        DetailJavaPath            = installation.JavaPath ?? string.Empty;
        DetailPinMinecraftVersion = installation.PinMinecraftVersion;
        DetailJavaOk     = true;
        DetailJavaHealth = string.Empty;
        _ = RefreshDetailJavaHealthAsync(installation.MinecraftVersion);
        DetailStatus = string.Empty;
        DetailRenderDistance = RamCalculatorService.BaselineRenderDistance.ToString();
        DetailRamRecommendation = string.Empty;
        DetailSection = "Home";
        DetailUpdateStatus = string.Empty;
        DetailDependencyStatus = string.Empty;
        DetailDependencyIssues = new List<DependencyIssue>();
        DetailExportStatus = string.Empty;
        DetailPrismExportStatus = string.Empty;
        IsDetailVisible = true;

        await LoadDetailModsAsync();
    }

    /// <summary>
    /// Estimates recommended Min/Max RAM from the installation's version, loader,
    /// currently-installed mods, and the render distance typed above the button,
    /// and fills the Min/Max RAM fields with it (the user can still tweak them
    /// afterwards — this is a starting point, not a hard rule).
    /// </summary>
    public void CalculateRecommendedRam()
    {
        if (DetailInstallation == null)
            return;

        var renderDistance = int.TryParse(DetailRenderDistance, out var rd)
            ? rd
            : RamCalculatorService.BaselineRenderDistance;

        var estimate = RamCalculatorService.Estimate(DetailMinecraftVersion, DetailLoader, DetailMods, renderDistance);

        DetailMinMemoryMB = estimate.RecommendedMinMB.ToString();
        DetailMaxMemoryMB = estimate.RecommendedMaxMB.ToString();
        DetailRamRecommendation = $"Recommended: {estimate.RecommendedMinMB} MB min / {estimate.RecommendedMaxMB} MB max — {estimate.Breakdown}";
    }

    public async Task RefreshDetailModsPublicAsync() => await LoadDetailModsAsync();

    private async Task LoadDetailModsAsync()
    {
        if (DetailInstallation == null)
            return;

        try
        {
            DetailMods = await _modService.GetInstalledModsAsync(DetailInstallation.Id);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Loading installed mods failed: {ex.Message}");
            DetailMods = new List<Mod>();
        }

        DetailModRows = DetailMods
            .Select(m => new InstalledModRowViewModel(m, DetailInstallation, _modService))
            .ToList();
    }

    /// <summary>Checks every installed mod of this installation for a newer compatible
    /// Modrinth version (report only — doesn't download anything) and shows the result.</summary>
    public async Task CheckModUpdatesAsync()
    {
        if (DetailInstallation == null || IsCheckingModUpdates)
            return;

        IsCheckingModUpdates = true;
        DetailUpdateStatus = "Checking for updates…";
        try
        {
            await _modService.RefreshUpdateStatusAsync(DetailInstallation);
            var withUpdate = DetailMods.Count(m => m.UpdateAvailable);
            DetailUpdateStatus = withUpdate == 0
                ? $"All {DetailMods.Count} mod(s) are up to date."
                : $"{withUpdate} of {DetailMods.Count} mod(s) have an update available — see the \"Update\" button next to each.";

            // Rebuild the rows so their UpdateAvailable/UpdateLabel bindings refresh.
            DetailModRows = DetailMods
                .Select(m => new InstalledModRowViewModel(m, DetailInstallation, _modService))
                .ToList();
        }
        catch (Exception ex)
        {
            DetailUpdateStatus = $"Could not check for updates: {ex.Message}";
        }
        finally
        {
            IsCheckingModUpdates = false;
        }
    }

    /// <summary>Opens this installation's mods folder in Windows Explorer so the user
    /// can manually drag mod jars in/out — a direct escape hatch alongside the search
    /// and dependency tooling above.</summary>
    public void OpenModsFolder()
    {
        if (DetailInstallation == null)
            return;

        try
        {
            var path = _modService.GetModsFolderPath(DetailInstallation);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            DetailUpdateStatus = $"Could not open the mods folder: {ex.Message}";
        }
    }

    /// <summary>Downloads and installs the newest compatible version for a single mod row.</summary>
    public async Task UpdateModRowAsync(InstalledModRowViewModel row)
    {
        if (DetailInstallation == null)
            return;

        try
        {
            var updated = await _modService.UpdateModAsync(DetailInstallation, row.Mod.Id);
            DetailUpdateStatus = updated ? $"Updated {row.Name}." : $"{row.Name} is already up to date.";
            await LoadDetailModsAsync();
        }
        catch (Exception ex)
        {
            DetailUpdateStatus = $"Could not update {row.Name}: {ex.Message}";
        }
    }

    /// <summary>Scans installed mods' required dependencies for missing/incompatible-version
    /// problems (not too old, not too new — must match this installation's MC version + loader).</summary>
    public async Task CheckDependenciesAsync()
    {
        if (DetailInstallation == null || IsCheckingDependencies)
            return;

        IsCheckingDependencies = true;
        DetailDependencyStatus = "Checking dependencies…";
        try
        {
            var result = await _modService.CheckDependencyCompatibilityAsync(DetailInstallation);
            DetailDependencyIssues = result.Issues;
            DetailDependencyStatus = result.Summary;
        }
        catch (Exception ex)
        {
            DetailDependencyStatus = $"Could not check dependencies: {ex.Message}";
        }
        finally
        {
            IsCheckingDependencies = false;
        }
    }

    /// <summary>Installs the missing dependency, or swaps an incompatible one to a
    /// compatible version, then re-runs the dependency check to refresh the list.</summary>
    public async Task FixDependencyIssueAsync(DependencyIssue issue)
    {
        if (DetailInstallation == null)
            return;

        DetailDependencyStatus = $"Fixing {issue.DependencyName}…";
        try
        {
            var fixedOk = await _modService.FixDependencyIssueAsync(DetailInstallation, issue);
            if (!fixedOk)
            {
                DetailDependencyStatus = $"Could not fix {issue.DependencyName} automatically.";
                return;
            }

            await LoadDetailModsAsync();
            await CheckDependenciesAsync();
        }
        catch (Exception ex)
        {
            DetailDependencyStatus = $"Could not fix {issue.DependencyName}: {ex.Message}";
        }
    }

    public void CloseInstallationDetail()
    {
        IsDetailVisible = false;
        DetailInstallation = null;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Instance sharing overlay
    // ═════════════════════════════════════════════════════════════════════════

    public bool IsShareVisible
    {
        get => _isShareVisible;
        set => SetProperty(ref _isShareVisible, value);
    }

    public bool IsShareBusy
    {
        get => _isShareBusy;
        set => SetProperty(ref _isShareBusy, value);
    }

    public string ShareStatus
    {
        get => _shareStatus;
        set
        {
            if (SetProperty(ref _shareStatus, value))
                OnPropertyChanged(nameof(HasShareStatus));
        }
    }

    public bool HasShareStatus => !string.IsNullOrEmpty(_shareStatus);

    public string ShareCode
    {
        get => _shareCode;
        set => SetProperty(ref _shareCode, value);
    }

    public bool HasShareCode => !string.IsNullOrWhiteSpace(_shareCode);

    public string ImportCode
    {
        get => _importCode;
        set => SetProperty(ref _importCode, value);
    }

    public string ShareTab
    {
        get => _shareTab;
        set
        {
            if (SetProperty(ref _shareTab, value))
            {
                OnPropertyChanged(nameof(IsFriendsTab));
                OnPropertyChanged(nameof(IsCodeTab));
            }
        }
    }

    public bool IsFriendsTab => ShareTab == "Friends";
    public bool IsCodeTab    => ShareTab == "Code";

    public System.Collections.ObjectModel.ObservableCollection<FriendShareRow> ShareableFriends
    {
        get => _shareableFriends;
        set => SetProperty(ref _shareableFriends, value);
    }

    public System.Collections.ObjectModel.ObservableCollection<InstanceShareNotification>
        InstanceShareNotifications => _instanceNotifManager.Notifications;

    public int  InstanceShareUnreadCount      => _instanceNotifManager.UnreadCount;
    public bool HasInstanceShareNotifications => _instanceNotifManager.UnreadCount > 0;

    public async Task OpenShareOverlayAsync(Installation installation)
    {
        _sharingInstallation = installation;
        ShareCode   = string.Empty;
        ShareStatus = string.Empty;
        ShareTab    = "Friends";
        ImportCode  = string.Empty;

        var token = _nexoraService.Current?.Token;
        if (!string.IsNullOrWhiteSpace(token))
        {
            try
            {
                var resp = await _nexoraService.GetApiService().GetFriendsAsync(token);
                var rows = (resp.Success ? resp.Data : null) ?? new List<Friend>();
                ShareableFriends = new System.Collections.ObjectModel.ObservableCollection<FriendShareRow>(
                    rows.Select(f => new FriendShareRow(f)));
            }
            catch { ShareableFriends = new(); }
        }
        else
        {
            ShareStatus      = "Sign in to your Nexora account to share with friends.";
            ShareableFriends = new();
        }

        IsShareVisible = true;
    }

    public void CloseShareOverlay()
    {
        IsShareVisible       = false;
        ShareCode            = string.Empty;
        ShareStatus          = string.Empty;
        _sharingInstallation = null;
    }

    public async Task ShareWithFriendsAsync()
    {
        if (_sharingInstallation == null || IsShareBusy) return;
        var selected = ShareableFriends.Where(r => r.IsSelected).ToList();
        if (selected.Count == 0) { ShareStatus = "Select at least one friend."; return; }

        var token    = _nexoraService.Current?.Token;
        var username = _nexoraService.Current?.Username ?? string.Empty;
        if (string.IsNullOrWhiteSpace(token)) { ShareStatus = "Sign in to your Nexora account first."; return; }

        IsShareBusy = true;
        ShareStatus = "Sharing…";
        try
        {
            var progress   = new Progress<string>(msg => ShareStatus = msg);
            var recipients = selected.Select(r => r.Friend.WebsiteUsername ?? string.Empty);
            var result     = await _instanceShare.ShareWithFriendsAsync(
                token, username, _sharingInstallation, recipients, progress);
            ShareStatus = result.Success
                ? $"Shared with {selected.Count} friend(s) ✓"
                : $"Could not share: {result.Error}";
        }
        catch (Exception ex) { ShareStatus = $"Error: {ex.Message}"; }
        finally { IsShareBusy = false; }
    }

    public async Task GenerateShareCodeAsync()
    {
        if (_sharingInstallation == null || IsShareBusy) return;
        var token = _nexoraService.Current?.Token;
        if (string.IsNullOrWhiteSpace(token)) { ShareStatus = "Sign in to your Nexora account first."; return; }

        IsShareBusy = true;
        ShareStatus = "Generating code…";
        ShareCode   = string.Empty;
        try
        {
            var progress = new Progress<string>(msg => ShareStatus = msg);
            var result   = await _instanceShare.GenerateShareCodeAsync(
                token, _sharingInstallation, progress);
            if (result.Success)
            {
                ShareCode   = result.Data ?? string.Empty;
                ShareStatus = "Code generated — share it with anyone!";
                OnPropertyChanged(nameof(HasShareCode));
            }
            else
            {
                ShareStatus = $"Could not generate code: {result.Error}";
            }
        }
        catch (Exception ex) { ShareStatus = $"Error: {ex.Message}"; }
        finally { IsShareBusy = false; }
    }

    public void CopyShareCode()
    {
        if (!string.IsNullOrWhiteSpace(ShareCode))
            try { System.Windows.Clipboard.SetText(ShareCode); } catch { }
    }

    public async Task ImportFromCodeAsync()
    {
        var code = ImportCode?.Trim();
        if (string.IsNullOrWhiteSpace(code)) return;
        if (IsShareBusy || IsBusy) return;

        IsShareBusy = true;
        ShareStatus = "Downloading instance…";
        try
        {
            var result = await _instanceShare.RedeemCodeAsync(code);
            if (!result.Success || result.Data == null)
            {
                ShareStatus = $"Invalid or expired code: {result.Error}";
                return;
            }
            var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mch-import-{Guid.NewGuid():N}.mrpack");
            await System.IO.File.WriteAllBytesAsync(tempPath, result.Data);
            ShareStatus = "Importing…";
            try
            {
                await ImportInstallationAsync(tempPath);
                ShareStatus = "Imported successfully ✓";
                CloseShareOverlay();
            }
            finally { try { System.IO.File.Delete(tempPath); } catch { } }
        }
        catch (Exception ex) { ShareStatus = $"Error: {ex.Message}"; }
        finally { IsShareBusy = false; }
    }

    public async Task AcceptShareNotificationAsync(InstanceShareNotification notification)
    {
        await _instanceNotifManager.MarkReadAsync(notification);
        ImportCode = notification.ShareCode;
        ShareTab   = "Code";
        if (!IsShareVisible)
        {
            _sharingInstallation = null;
            IsShareVisible = true;
        }
        await ImportFromCodeAsync();
    }

    public async Task DeclineShareNotificationAsync(InstanceShareNotification notification)
        => await _instanceNotifManager.MarkReadAsync(notification);

    private async Task RefreshDetailJavaHealthAsync(string minecraftVersion)
    {
        try
        {
            var rec      = await _javaService.GetRecommendationAsync(minecraftVersion);
            DetailJavaOk = rec.IsSatisfied;
            DetailJavaHealth = rec.IsSatisfied
                ? $"Java {rec.RecommendedMajor} found ✓"
                : $"Java {rec.RecommendedMajor} not found — click to download automatically";
        }
        catch { /* non-critical */ }
    }

    public async Task DownloadJavaForDetailAsync()
    {
        if (DetailInstallation == null || IsDetailBusy) return;
        IsDetailBusy = true;
        DetailStatus = "Downloading Java…";
        try
        {
            var progress = new Progress<string>(msg => DetailStatus = msg);
            var rec      = await _javaService.GetRecommendationAsync(DetailInstallation.MinecraftVersion);
            var javaExe  = await _javaService.DownloadJavaAsync(rec.RecommendedMajor, progress);

            DetailJavaPath   = javaExe;
            DetailJavaOk     = true;
            DetailJavaHealth = $"Java {rec.RecommendedMajor} installed ✓";
            DetailStatus     = $"Java {rec.RecommendedMajor} downloaded and set as Java path.";
        }
        catch (Exception ex)
        {
            DetailStatus = $"Java download failed: {ex.Message}";
        }
        finally
        {
            IsDetailBusy = false;
        }
    }

    /// <summary>Saves the General-settings + Advanced fields back onto the installation.</summary>
    public async Task SaveInstallationDetailAsync()
    {
        if (DetailInstallation == null || IsDetailBusy)
            return;
    
        if (string.IsNullOrWhiteSpace(DetailName) || string.IsNullOrWhiteSpace(DetailMinecraftVersion))
        {
            DetailStatus = "Please enter a name and a Minecraft version.";
            return;
        }
    
        // Detect whether the Minecraft version is being changed
        var oldVersion = DetailInstallation.MinecraftVersion;
        var newVersion = DetailMinecraftVersion.Trim();
        var versionChanged = !string.Equals(oldVersion, newVersion, StringComparison.OrdinalIgnoreCase);
    
        IsDetailBusy = true;
        DetailStatus = "Saving\u2026";
        try
        {
            DetailInstallation.Name = DetailName.Trim();
            DetailInstallation.MinecraftVersion = newVersion;
            DetailInstallation.Loader = DetailLoader;
            DetailInstallation.LoaderVersion = string.IsNullOrWhiteSpace(DetailLoaderVersion) ? null : DetailLoaderVersion.Trim();
            DetailInstallation.MaxMemoryMB = int.TryParse(DetailMaxMemoryMB, out var maxMb) ? maxMb : null;
            DetailInstallation.MinMemoryMB = int.TryParse(DetailMinMemoryMB, out var minMb) ? minMb : null;
            DetailInstallation.CustomJvmArgs       = string.IsNullOrWhiteSpace(DetailCustomJvmArgs) ? null : DetailCustomJvmArgs.Trim();
            DetailInstallation.JavaPath            = string.IsNullOrWhiteSpace(DetailJavaPath) ? null : DetailJavaPath.Trim();
            DetailInstallation.PinMinecraftVersion = DetailPinMinecraftVersion;
    
            await _installationService.UpdateInstallationAsync(DetailInstallation);
            await LoadInstallationsAsync();
    
            DetailStatus = "Saved.";
        }
        catch (Exception ex)
        {
            DetailStatus = $"Could not save: {ex.Message}";
            return;
        }
        finally
        {
            IsDetailBusy = false;
        }
    
        // If the Minecraft version changed, offer to check mod compatibility
        if (versionChanged)
            await PromptModCompatibilityCheckAsync(oldVersion, newVersion);
    }

    /// <summary>Uninstalls a mod from the installation currently shown in the detail overlay.</summary>
    public async Task UninstallDetailModAsync(Mod mod)
    {
        if (DetailInstallation == null || IsDetailBusy)
            return;

        IsDetailBusy = true;
        try
        {
            await _modService.UninstallModAsync(DetailInstallation.Id, mod.Id);
            await LoadDetailModsAsync();
        }
        catch (Exception ex)
        {
            DetailStatus = $"Could not uninstall {mod.Name}: {ex.Message}";
        }
        finally
        {
            IsDetailBusy = false;
        }
    }

    /// <summary>
    /// Exports the installation currently shown in the detail overlay to a .mrpack
    /// file — the same Modrinth Modpack Format Prism Launcher uses for its own
    /// "Export Instance → Modrinth pack format", so the result opens straight in
    /// Prism's "Import Instance" (or Modrinth App) with no conversion needed.
    /// </summary>

    public async Task ExportInstallationDetailPrismAsync(string outputFilePath)
    {
        if (DetailInstallation == null || IsExportingPrismDetail)
            return;

        IsExportingPrismDetail = true;
        DetailPrismExportStatus = "Preparing Prism export…";
        try
        {
            var progress = new Progress<string>(stage => DetailPrismExportStatus = stage);
            var result = await _modpackService.ExportPrismNativeAsync(DetailInstallation, outputFilePath, progress);
            DetailPrismExportStatus = result.Summary;
        }
        catch (Exception ex)
        {
            DetailPrismExportStatus = $"Could not export: {ex.Message}";
        }
        finally
        {
            IsExportingPrismDetail = false;
        }
    }

    public async Task ExportInstallationDetailAsync(string outputFilePath)
    {
        if (DetailInstallation == null || IsExportingDetail)
            return;

        IsExportingDetail = true;
        DetailExportStatus = "Preparing export…";
        try
        {
            var progress = new Progress<string>(stage => DetailExportStatus = stage);
            var result = await _modpackService.ExportAsync(DetailInstallation, outputFilePath, progress);
            DetailExportStatus = result.Summary;
        }
        catch (Exception ex)
        {
            DetailExportStatus = $"Could not export: {ex.Message}";
        }
        finally
        {
            IsExportingDetail = false;
        }
    }

    /// <summary>
    /// Run a full health check on the currently selected installation.
    /// Loads and displays Java, RAM, Mods, and Stability scores.
    /// </summary>
    public async Task LoadDetailHealthCheckAsync()
    {
        if (DetailInstallation == null) return;

        IsLoadingHealth = true;
        try
        {
            DetailHealthCheck = await _healthCheckService.CheckInstallationHealthAsync(
                DetailInstallation,
                DetailMods,
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Health check failed: {ex.Message}");
        }
        finally
        {
            IsLoadingHealth = false;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Mod compatibility check after Minecraft version change
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// After the Minecraft version changes, asks the user whether to check mod
    /// compatibility. Opens a dedicated window that scans all mods at once.
    /// </summary>
    private async Task PromptModCompatibilityCheckAsync(string oldVersion, string newVersion)
    {
        if (DetailInstallation == null) return;

        var mods = await _modService.GetInstalledModsAsync(DetailInstallation.Id);
        if (mods.Count == 0) return;

        var answer = System.Windows.MessageBox.Show(
            $"Minecraft version changed from {oldVersion} to {newVersion}.\n\n" +
            $"Would you like to check if your {mods.Count} mod(s) are compatible?\n" +
            "Compatible mods will be auto-updated. For incompatible mods you can choose to disable or uninstall them.",
            "Mod Compatibility Check",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);

        if (answer != System.Windows.MessageBoxResult.Yes)
            return;

        // Open the compatibility window
        var window = new UI.Windows.ModCompatibilityWindow(
            DetailInstallation, _modService, oldVersion, newVersion)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };

        var dialogResult = window.ShowDialog();

        if (dialogResult == true && window.Result.Applied)
        {
            // Apply the user's choices for incompatible mods
            IsDetailBusy = true;
            DetailStatus = "Applying mod changes\u2026";

            var disabled = 0;
            var uninstalled = 0;

            try
            {
                foreach (var row in window.Result.Rows.Where(r => !r.IsCompatible))
                {
                    switch (row.SelectedActionIndex)
                    {
                        case 1: // Disable
                            if (row.Mod.IsEnabled)
                                await _modService.ToggleModEnabledAsync(DetailInstallation, row.Mod.Id);
                            disabled++;
                            break;
                        case 2: // Uninstall
                            await _modService.UninstallModAsync(DetailInstallation.Id, row.Mod.Id);
                            uninstalled++;
                            break;
                        // 0 = Skip, do nothing
                    }
                }

                var parts = new List<string>();
                var updatedCount = window.Result.Rows.Count(r => r.WasAutoUpdated);
                if (updatedCount > 0)  parts.Add($"{updatedCount} updated");
                if (disabled > 0)      parts.Add($"{disabled} disabled");
                if (uninstalled > 0)   parts.Add($"{uninstalled} uninstalled");

                DetailStatus = parts.Count > 0
                    ? $"Compatibility check done: {string.Join(", ", parts)}."
                    : "Compatibility check done.";
            }
            catch (Exception ex)
            {
                DetailStatus = $"Error applying changes: {ex.Message}";
            }
            finally
            {
                IsDetailBusy = false;
                await LoadDetailModsAsync();
            }
        }
        else
        {
            // User cancelled — still refresh since auto-updates may have been applied
            await LoadDetailModsAsync();
        }
    }
}
