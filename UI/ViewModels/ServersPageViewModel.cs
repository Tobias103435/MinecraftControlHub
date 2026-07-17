using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using MinecraftControlHub.Core.Models;
using MinecraftControlHub.Core.Services;

namespace MinecraftControlHub.UI.ViewModels;

public enum ServerAccountMode
{
    OfflineMode,
    OnlineMode
}

public class ServersPageViewModel : ViewModelBase
{
    private readonly IServerService _serverService;
    private readonly ILoaderService _loaderService;
    private readonly IJavaService _javaService;
    private string _javaAdvice = string.Empty;
    private ObservableCollection<Server> _servers = new();
    private bool _isLoading;
    private bool _isCreatingServer;
    private bool _isWizardVisible;
    private int _wizardStep = 1;
    private string _newServerName = string.Empty;
    private ServerType _newServerType = ServerType.Vanilla;
    private string _newServerVersion = "1.21.6";
    private int _newServerMaxMemory = 2048;
    private string? _creationError;
    private ServerAccountMode _newServerAccountMode = ServerAccountMode.OfflineMode;
    private List<string> _allMinecraftVersions = new();
    private List<string> _availableServerVersions = new List<string> { "1.21.6", "1.21.5", "1.21.4", "1.20.4", "1.19.4", "1.18.2" };

    public const int MaxWizardStep = 6;

    public ObservableCollection<Server> Servers
    {
        get => _servers;
        set => SetProperty(ref _servers, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public bool IsCreatingServer
    {
        get => _isCreatingServer;
        set => SetProperty(ref _isCreatingServer, value);
    }

    public bool IsWizardVisible
    {
        get => _isWizardVisible;
        set => SetProperty(ref _isWizardVisible, value);
    }

    public int WizardStep
    {
        get => _wizardStep;
        set
        {
            if (SetProperty(ref _wizardStep, value))
            {
                NotifyWizardStepProperties();
            }
        }
    }

    public string NewServerName
    {
        get => _newServerName;
        set
        {
            if (SetProperty(ref _newServerName, value))
                OnPropertyChanged(nameof(CanGoNext));
        }
    }

    public ServerType NewServerType
    {
        get => _newServerType;
        set
        {
            if (SetProperty(ref _newServerType, value))
                RefreshAvailableServerVersions();
        }
    }

    public List<string> AvailableServerVersions
    {
        get => _availableServerVersions;
        private set => SetProperty(ref _availableServerVersions, value);
    }

    public string NewServerVersion
    {
        get => _newServerVersion;
        set
        {
            if (SetProperty(ref _newServerVersion, value))
            {
                OnPropertyChanged(nameof(CanGoNext));
                _ = RefreshJavaAdviceAsync();
            }
        }
    }

    public string JavaAdvice
    {
        get => _javaAdvice;
        private set
        {
            if (SetProperty(ref _javaAdvice, value))
                OnPropertyChanged(nameof(HasJavaAdvice));
        }
    }

    public bool HasJavaAdvice => !string.IsNullOrEmpty(_javaAdvice);

    public int NewServerMaxMemory
    {
        get => _newServerMaxMemory;
        set
        {
            if (SetProperty(ref _newServerMaxMemory, value))
                OnPropertyChanged(nameof(CanGoNext));
        }
    }

    public ServerAccountMode NewServerAccountMode
    {
        get => _newServerAccountMode;
        set => SetProperty(ref _newServerAccountMode, value);
    }

    public IEnumerable<ServerAccountMode> ServerAccountModes => Enum.GetValues<ServerAccountMode>();

    public string? CreationError
    {
        get => _creationError;
        set => SetProperty(ref _creationError, value);
    }

    public bool HasServers => Servers.Count > 0;

    public IEnumerable<ServerType> ServerTypes => Enum.GetValues<ServerType>();

   public bool IsWizardStep1 => WizardStep == 1;
   public bool IsWizardStep2 => WizardStep == 2;
   public bool IsWizardStep3 => WizardStep == 3;
   public bool IsWizardStep4 => WizardStep == 4;
   public bool IsWizardStep5 => WizardStep == 5;
   public bool IsWizardStep6 => WizardStep == 6;

   public bool CanGoNext => WizardStep switch
   {
       1 => !string.IsNullOrWhiteSpace(NewServerName),
       2 => true,
       3 => !string.IsNullOrWhiteSpace(NewServerVersion),
       4 => NewServerMaxMemory >= 512,
       5 => true,
       6 => true,
       _ => false
   };

   public bool CanGoBack => WizardStep > 1;

   public string WizardStepTitle => WizardStep switch
   {
       1 => "Step 1 of 6 — Server Name",
       2 => "Step 2 of 6 — Server Type",
       3 => "Step 3 of 6 — Minecraft Version",
       4 => "Step 4 of 6 — RAM Allocation",
       5 => "Step 5 of 6 — Account Mode",
       6 => "Step 6 of 6 — Review & Create",
       _ => string.Empty
   };
    public ServersPageViewModel(IServerService serverService, ILoaderService loaderService, IJavaService javaService)
    {
        _serverService = serverService;
        _loaderService = loaderService;
        _javaService = javaService;
        _ = LoadServersAsync();
        _ = LoadAvailableVersionsAsync();

        // Reload whenever a server is created or deleted externally (e.g. by the AI terminal).
        _serverService.ServersChanged += (_, _) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(
                () => _ = LoadServersAsync());
    }

    public void StartWizard()
    {
        ResetWizardForm();
        IsWizardVisible = true;
        WizardStep = 1;
        CreationError = null;
        // Eagerly load Java advice for the default version so it shows immediately on step 3
        _ = RefreshJavaAdviceAsync();
    }

    public void CancelWizard()
    {
        IsWizardVisible = false;
        ResetWizardForm();
        CreationError = null;
    }

    public void NextWizardStep()
    {
        if (!CanGoNext || WizardStep >= MaxWizardStep)
            return;

        WizardStep++;

        // Refresh Java advice whenever the user reaches or re-enters the version step
        if (WizardStep == 3)
            _ = RefreshJavaAdviceAsync();
    }

    public void PreviousWizardStep()
    {
        if (!CanGoBack)
            return;

        WizardStep--;
    }

    public async Task CreateServerAsync()
    {
        if (string.IsNullOrWhiteSpace(NewServerName) || string.IsNullOrWhiteSpace(NewServerVersion))
            return;

        IsCreatingServer = true;
        CreationError = null;

        try
        {
            var server = new Server
            {
                Name = NewServerName.Trim(),
                Type = NewServerType,
                MinecraftVersion = NewServerVersion.Trim(),
                MaxMemoryMB = NewServerMaxMemory,
                MinMemoryMB = Math.Max(512, NewServerMaxMemory / 2),
                AllowOnlineMode = NewServerAccountMode == ServerAccountMode.OnlineMode,
                MaxPlayers = 10,
                CurrentPlayers = 0,
                LoadPercent = 0,
                Gamemode = "survival",
                Difficulty = "normal",
                AllowCheats = false,
                WhiteListEnabled = false,
                Motd = NewServerName.Trim()
            };

            await _serverService.CreateServerAsync(server);
            await LoadServersAsync();

            IsWizardVisible = false;
            ResetWizardForm();
        }
        catch (Exception ex)
        {
            CreationError = $"Failed to create server: {ex.Message}";
        }
        finally
        {
            IsCreatingServer = false;
        }
    }

    public async Task StartServerAsync(Server server)
    {
        await _serverService.StartServerAsync(server.Id);
        await LoadServersAsync();
    }

    public async Task StopServerAsync(Server server)
    {
        await _serverService.StopServerAsync(server.Id);
        await LoadServersAsync();
    }

    public async Task DeleteServerAsync(Server server)
    {
        await _serverService.DeleteServerAsync(server.Id);
        await LoadServersAsync();
    }

    private async Task LoadServersAsync()
    {
        IsLoading = true;
        try
        {
            Servers = new ObservableCollection<Server>(await _serverService.GetAllServersAsync());
            OnPropertyChanged(nameof(HasServers));
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ResetWizardForm()
    {
        NewServerName = string.Empty;
        NewServerType = ServerType.Vanilla;
        NewServerVersion = "1.21.6";
        NewServerMaxMemory = 2048;
        NewServerAccountMode = ServerAccountMode.OfflineMode;
        WizardStep = 1;
    }

    private async Task LoadAvailableVersionsAsync()
    {
        try
        {
            var versions = await _loaderService.GetMinecraftVersionsAsync();
            _allMinecraftVersions = versions
                .Where(v => v.IsRelease)
                .Select(v => v.Id)
                .ToList();
        }
        catch
        {
            _allMinecraftVersions = DefaultMinecraftVersions.ToList();
        }

        RefreshAvailableServerVersions();
    }

    private void RefreshAvailableServerVersions()
    {
        var versions = _allMinecraftVersions.Any() ? _allMinecraftVersions : DefaultMinecraftVersions.ToList();

        AvailableServerVersions = versions;

        if (!AvailableServerVersions.Contains(NewServerVersion))
        {
            NewServerVersion = AvailableServerVersions.FirstOrDefault() ?? NewServerVersion;
        }
    }

    private static readonly List<string> DefaultMinecraftVersions = new()
    {
        "1.21.6",
        "1.21.5",
        "1.21.4",
        "1.20.4",
        "1.19.4",
        "1.18.2"
    };

    private async Task RefreshJavaAdviceAsync()
    {
        if (string.IsNullOrWhiteSpace(NewServerVersion))
        {
            JavaAdvice = string.Empty;
            return;
        }

        try
        {
            var recommendation = await _javaService.GetRecommendationAsync(NewServerVersion);
            JavaAdvice = recommendation.Summary;
        }
        catch
        {
            JavaAdvice = string.Empty;
        }
    }

    private void NotifyWizardStepProperties()
    {
        OnPropertyChanged(nameof(IsWizardStep1));
        OnPropertyChanged(nameof(IsWizardStep2));
        OnPropertyChanged(nameof(IsWizardStep3));
        OnPropertyChanged(nameof(IsWizardStep4));
        OnPropertyChanged(nameof(IsWizardStep5));
        OnPropertyChanged(nameof(IsWizardStep6));
        OnPropertyChanged(nameof(WizardStepTitle));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(CanGoBack));
    }
}
