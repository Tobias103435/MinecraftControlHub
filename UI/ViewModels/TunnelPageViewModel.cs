using System.Collections.ObjectModel;
using System.Windows.Input;
using MinecraftControlHub.Core.Models;
using MinecraftControlHub.Core.Services;
using MinecraftControlHub.Networking;

namespace MinecraftControlHub.UI.ViewModels;

public partial class TunnelPageViewModel : ViewModelBase
{
    private readonly IServerService   _serverService;
    private readonly IPortScanService _portScanService;

    private Server?                                    _selectedServer;
    private ObservableCollection<Server>               _servers     = new();
    private ObservableCollection<PortInfo>             _ports       = new();
    private ObservableCollection<PortTunnelAssignment> _assignments = new();
    private bool                                       _isLoading;

    // Custom-port add form
    private string       _newCustomPort     = string.Empty;
    private PortProtocol _newCustomProtocol = PortProtocol.TCP;
    private string       _newCustomPurpose  = string.Empty;

    // -----------------------------------------------------------------------
    // Constructor
    // -----------------------------------------------------------------------
    public TunnelPageViewModel(
        IServerService   serverService,
        IPortScanService portScanService,
        ITunnelService   tunnelService,
        ISettingsService settingsService)
    {
        _serverService   = serverService;
        _portScanService = portScanService;

        AddCustomPortCommand   = new RelayCommand(AddCustomPort, CanAddCustomPort);
        RemovePortCommand      = new RelayCommand(obj => { if (obj is PortInfo p) RemovePort(p); });
        TogglePortCommand      = new RelayCommand(obj => { if (obj is PortInfo p) TogglePort(p); });
        RefreshPortsCommand    = new RelayCommand(_ => _ = LoadPortsAsync());
        ResetProviderCommand   = new RelayCommand(obj =>
        {
            if (obj is PortTunnelAssignment a)
                a.SelectedProvider = a.RecommendedProvider;
        });

        // Wire up the launch sub-system (defined in TunnelLaunchViewModel.cs)
        InitLaunch(tunnelService, settingsService);

        _ = LoadServersAsync();
    }

    // -----------------------------------------------------------------------
    // Properties
    // -----------------------------------------------------------------------
    public ObservableCollection<Server> Servers
    {
        get => _servers;
        set => SetProperty(ref _servers, value);
    }

    public Server? SelectedServer
    {
        get => _selectedServer;
        set
        {
            if (SetProperty(ref _selectedServer, value))
            {
                OnPropertyChanged(nameof(HasSelectedServer));
                _ = LoadPortsAsync();
            }
        }
    }

    public ObservableCollection<PortInfo> Ports
    {
        get => _ports;
        set => SetProperty(ref _ports, value);
    }

    public ObservableCollection<PortTunnelAssignment> Assignments
    {
        get => _assignments;
        set => SetProperty(ref _assignments, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public string NewCustomPort
    {
        get => _newCustomPort;
        set
        {
            SetProperty(ref _newCustomPort, value);
            (AddCustomPortCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public PortProtocol NewCustomProtocol
    {
        get => _newCustomProtocol;
        set => SetProperty(ref _newCustomProtocol, value);
    }

    public string NewCustomPurpose
    {
        get => _newCustomPurpose;
        set
        {
            SetProperty(ref _newCustomPurpose, value);
            (AddCustomPortCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public bool HasServers        => Servers.Count > 0;
    public bool HasSelectedServer => SelectedServer != null;
    public bool HasPorts          => Ports.Count > 0;
    public bool HasAssignments    => Assignments.Count > 0;

    public IEnumerable<PortProtocol>     Protocols    => Enum.GetValues<PortProtocol>();
    public IReadOnlyList<TunnelProvider> AllProviders => TunnelProviderRegistry.All;

    // -----------------------------------------------------------------------
    // Commands
    // -----------------------------------------------------------------------
    public ICommand AddCustomPortCommand { get; }
    public ICommand RemovePortCommand    { get; }
    public ICommand TogglePortCommand    { get; }
    public ICommand RefreshPortsCommand  { get; }
    public ICommand ResetProviderCommand { get; }

    // Raised when the user clicks "Configure in Settings" — the MainWindow handles navigation
    public event EventHandler? NavigateToSettings;

    public ICommand GoToSettingsCommand => new RelayCommand(_ => NavigateToSettings?.Invoke(this, EventArgs.Empty));

    // -----------------------------------------------------------------------
    // Load servers list
    // -----------------------------------------------------------------------
    public async Task LoadServersAsync()
    {
        IsLoading = true;
        try
        {
            var list = await _serverService.GetAllServersAsync();
            Servers = new ObservableCollection<Server>(list);
            OnPropertyChanged(nameof(HasServers));

            if (Servers.Count > 0 && SelectedServer == null)
                SelectedServer = Servers[0];
        }
        finally
        {
            IsLoading = false;
        }
    }

    // -----------------------------------------------------------------------
    // Load / refresh ports + build provider assignments
    // -----------------------------------------------------------------------
    public async Task LoadPortsAsync()
    {
        if (SelectedServer == null)
        {
            Ports       = new ObservableCollection<PortInfo>();
            Assignments = new ObservableCollection<PortTunnelAssignment>();
            OnPropertyChanged(nameof(HasPorts));
            OnPropertyChanged(nameof(HasAssignments));
            return;
        }

        IsLoading = true;
        try
        {
            var customPorts = Ports.Where(p => p.IsCustom).ToList();
            var detected    = await _portScanService.ScanAsync(SelectedServer);

            var all = new ObservableCollection<PortInfo>(detected);
            foreach (var cp in customPorts) all.Add(cp);

            Ports = all;
            OnPropertyChanged(nameof(HasPorts));

            // Hook property changes on each port so edits auto-save
            foreach (var port in Ports)
            {
                port.PropertyChanged += OnPortPropertyChanged;
            }

            RebuildAssignments();
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Called whenever any field on a PortInfo row changes.
    /// Rebuilds assignments when IsEnabled changes, and saves to server config on every change.
    /// </summary>
    private void OnPortPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PortInfo.IsEnabled))
            RebuildAssignments();

        // Persist immediately so the config stays in sync
        if (SelectedServer != null)
            _ = _serverService.UpdateServerAsync(SelectedServer);
    }

    // -----------------------------------------------------------------------
    // RebuildAssignments
    // -----------------------------------------------------------------------
    public void RebuildAssignments()
    {
        var existing = _assignments.ToDictionary(a => a.Port.Port, a => a.SelectedProvider?.Id);

        var newAssignments = Ports
            .Where(p => p.IsEnabled)
            .Select(port =>
            {
                var recommended = TunnelProviderRegistry.GetRecommended(port.Protocol);
                var compatibles = TunnelProviderRegistry.ForProtocol(port.Protocol);

                TunnelProvider? selectedProvider = null;
                if (existing.TryGetValue(port.Port, out var prevId) && prevId != null)
                    selectedProvider = compatibles.FirstOrDefault(p => p.Id == prevId);

                selectedProvider ??= recommended;

                return new PortTunnelAssignment
                {
                    Port                = port,
                    RecommendedProvider = recommended,
                    SelectedProvider    = selectedProvider
                };
            })
            .ToList();

        Assignments = new ObservableCollection<PortTunnelAssignment>(newAssignments);
        OnPropertyChanged(nameof(HasAssignments));
        (LaunchAllTunnelsCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    public IReadOnlyList<TunnelProvider> GetCompatibleProviders(PortProtocol protocol)
        => TunnelProviderRegistry.ForProtocol(protocol);

    // -----------------------------------------------------------------------
    // Port toggle
    // -----------------------------------------------------------------------
    public void TogglePort(PortInfo port)
    {
        port.IsEnabled = !port.IsEnabled;
        RebuildAssignments();
    }

    // -----------------------------------------------------------------------
    // Custom port actions
    // -----------------------------------------------------------------------
    private bool CanAddCustomPort()
        => !IsLoading
        && int.TryParse(NewCustomPort, out var p) && p >= 1 && p <= 65535
        && !string.IsNullOrWhiteSpace(NewCustomPurpose);

    public void AddCustomPort()
    {
        if (!int.TryParse(NewCustomPort, out var port) || port < 1 || port > 65535) return;
        if (string.IsNullOrWhiteSpace(NewCustomPurpose)) return;

        var newPort = new PortInfo
        {
            Port      = port,
            Protocol  = NewCustomProtocol,
            Purpose   = NewCustomPurpose,
            IsEnabled = true,
            IsCustom  = true,
            Source    = "custom"
        };

        newPort.PropertyChanged += OnPortPropertyChanged;
        Ports.Add(newPort);

        OnPropertyChanged(nameof(HasPorts));
        RebuildAssignments();

        NewCustomPort     = string.Empty;
        NewCustomPurpose  = string.Empty;
        NewCustomProtocol = PortProtocol.TCP;
    }

    public void RemovePort(PortInfo port)
    {
        if (port.IsCustom)
        {
            port.PropertyChanged -= OnPortPropertyChanged;
            Ports.Remove(port);
            OnPropertyChanged(nameof(HasPorts));
            RebuildAssignments();
        }
    }
}
