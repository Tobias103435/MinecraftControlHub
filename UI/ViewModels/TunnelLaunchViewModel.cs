using System.Collections.ObjectModel;
using System.Windows.Input;
using MinecraftControlHub.Core.Models;
using MinecraftControlHub.Core.Services;
using MinecraftControlHub.Networking;

namespace MinecraftControlHub.UI.ViewModels;

/// <summary>
/// Wraps a <see cref="TunnelSession"/> for display in the Launch panel.
/// Implements INotifyPropertyChanged via ViewModelBase → updates live.
/// </summary>
public class TunnelSessionViewModel : ViewModelBase
{
    private TunnelSession        _session;
    private string               _logText = string.Empty;

    public TunnelSessionViewModel(TunnelSession session)
    {
        _session = session;
        RefreshFromSession();
    }

    // -----------------------------------------------------------------------
    // Bindings
    // -----------------------------------------------------------------------

    public int    Port        => _session.Assignment.Port.Port;
    public string Protocol    => _session.Assignment.Port.Protocol.ToString();
    public string Purpose     => _session.Assignment.Port.Purpose;
    public string ProviderName => _session.Assignment.SelectedProvider?.DisplayName ?? "—";

    public TunnelSessionState State => _session.State;

    public string? PublicAddress => _session.PublicAddress;

    public string? ErrorMessage  => _session.ErrorMessage;

    public string LogText
    {
        get => _logText;
        private set => SetProperty(ref _logText, value);
    }

    public bool IsStarting => _session.State == TunnelSessionState.Starting;
    public bool IsRunning  => _session.State == TunnelSessionState.Running;
    public bool IsError    => _session.State == TunnelSessionState.Error;
    public bool IsStopped  => _session.State == TunnelSessionState.Stopped
                           || _session.State == TunnelSessionState.Idle;

    public bool HasAddress  => !string.IsNullOrEmpty(PublicAddress);
    public bool HasError    => !string.IsNullOrEmpty(ErrorMessage);
    public string? ClaimUrl  => _session.ClaimUrl;
    public bool HasClaimUrl  => _session.HasClaimUrl;

    /// <summary>
    /// True for providers that don't run their own process (e.g. playit.gg
    /// via its official Windows wizard, run separately as a background
    /// service) — the UI shows a text field + button instead of a log/CMD.
    /// </summary>
    public bool RequiresManualAddress =>
        _session.Assignment.SelectedProvider?.AddressSource == TunnelAddressSource.Manual
        && !HasAddress;

    private string _manualAddressInput = string.Empty;
    public string ManualAddressInput
    {
        get => _manualAddressInput;
        set
        {
            if (SetProperty(ref _manualAddressInput, value))
            {
                // Notify the button so it enables/disables as the user types
                (_submitManualAddressCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    private ICommand? _submitManualAddressCommand;
    public ICommand SubmitManualAddressCommand => _submitManualAddressCommand ??= new RelayCommand(
        _ =>
        {
            _session.ProvideManualAddress(ManualAddressInput);
            ManualAddressInput = string.Empty;
        },
        _ => !string.IsNullOrWhiteSpace(ManualAddressInput));

    /// <summary>
    /// True when the tunnel is still in Starting state but no claim URL has
    /// appeared yet — i.e. playit.gg is running as a tray agent but hasn't
    /// printed a tunnel address.  We use this to show an explanatory banner
    /// instead of leaving the user staring at a purple dot with no context.
    /// </summary>
    public bool IsWaitingForPlayit =>
        IsStarting &&
        !HasClaimUrl &&
        (_session.Assignment.SelectedProvider?.Id is "playit" or "playit-premium");

    /// <summary>
    /// True when Starting but no tunnel address has come in yet and there is
    /// no claim URL — generic "waiting" state for non-playit providers.
    /// </summary>
    public bool IsWaitingGeneric =>
        IsStarting &&
        !HasClaimUrl &&
        !IsWaitingForPlayit;

    public string StateLabel => _session.State switch
    {
        TunnelSessionState.Starting when HasClaimUrl      => "Action required",
        TunnelSessionState.Starting when IsWaitingForPlayit => "Waiting for playit.gg…",
        TunnelSessionState.Starting                        => "Starting…",
        TunnelSessionState.Running                         => "Running",
        TunnelSessionState.Error                           => "Error",
        TunnelSessionState.Stopped                         => "Stopped",
        _                                                  => "Idle"
    };

    // -----------------------------------------------------------------------
    // Refresh
    // -----------------------------------------------------------------------

    public void RefreshFromSession()
    {
        RefreshFromSession(_session);
    }

    public void RefreshFromSession(TunnelSession session)
    {
        _session = session;
        LogText  = string.Join(Environment.NewLine, session.Log);

        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(PublicAddress));
        OnPropertyChanged(nameof(ErrorMessage));
        OnPropertyChanged(nameof(HasAddress));
        OnPropertyChanged(nameof(RequiresManualAddress));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(ClaimUrl));
        OnPropertyChanged(nameof(HasClaimUrl));
        OnPropertyChanged(nameof(StateLabel));
        OnPropertyChanged(nameof(IsStarting));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsError));
        OnPropertyChanged(nameof(IsStopped));
        OnPropertyChanged(nameof(IsWaitingForPlayit));
        OnPropertyChanged(nameof(IsWaitingGeneric));
        OnPropertyChanged(nameof(ProviderName));
    }
}

/// <summary>
/// Extension of TunnelPageViewModel that adds tunnel launch/stop logic.
/// Registered as the same <see cref="TunnelPageViewModel"/> via partial class.
/// </summary>
public partial class TunnelPageViewModel
{
    // Injected via the constructor overload in TunnelPageViewModel.Launch.cs
    private ITunnelService? _tunnelService;

    private bool _isLaunching;
    private ObservableCollection<TunnelSessionViewModel> _sessionViewModels = new();

    // -----------------------------------------------------------------------
    // Properties
    // -----------------------------------------------------------------------

    public ObservableCollection<TunnelSessionViewModel> SessionViewModels
    {
        get => _sessionViewModels;
        set => SetProperty(ref _sessionViewModels, value);
    }

    public bool IsLaunching
    {
        get => _isLaunching;
        set
        {
            if (SetProperty(ref _isLaunching, value))
                OnPropertyChanged(nameof(LaunchButtonLabel));
        }
    }

    /// <summary>
    /// Avalonia has no DataTrigger to swap a Button's Content based on a bound
    /// bool, so the label is computed here instead.
    /// </summary>
    public string LaunchButtonLabel => IsLaunching ? "Starting…" : "▶  Launch All Tunnels";

    public bool HasActiveSessions => _sessionViewModels.Count > 0 &&
        _sessionViewModels.Any(s => s.IsRunning || s.IsStarting);

    public bool HasAnySessions => _sessionViewModels.Count > 0;

    // -----------------------------------------------------------------------
    // Commands (wired up in InitLaunch)
    // -----------------------------------------------------------------------

    public ICommand? LaunchAllTunnelsCommand { get; private set; }
    public ICommand? StopAllTunnelsCommand   { get; private set; }
    public ICommand? StopOneTunnelCommand    { get; private set; }

    // -----------------------------------------------------------------------
    // Init (called from the partial constructor hook)
    // -----------------------------------------------------------------------

    public void InitLaunch(ITunnelService tunnelService, ISettingsService settingsService)
    {
        _tunnelService   = tunnelService;
        _settingsService = settingsService;

        LaunchAllTunnelsCommand = new RelayCommand(
            _ => _ = LaunchAllAsync(),
            _ => Assignments.Count > 0 && !IsLaunching && !HasActiveSessions);

        StopAllTunnelsCommand = new RelayCommand(
            _ => StopAll(),
            _ => HasActiveSessions);

        StopOneTunnelCommand = new RelayCommand(obj =>
        {
            if (obj is TunnelSessionViewModel svm)
                StopOne(svm.Port);
        });

        tunnelService.SessionChanged += OnSessionChanged;
    }

    // -----------------------------------------------------------------------
    // Launch all
    // -----------------------------------------------------------------------

    private async Task LaunchAllAsync()
    {
        if (_tunnelService is null) return;
        if (Assignments.Count == 0) return;

        IsLaunching = true;

        // Seed session view models immediately so the UI updates
        SessionViewModels = new ObservableCollection<TunnelSessionViewModel>(
            Assignments.Select(a => new TunnelSessionViewModel(
                new TunnelSession(a))));   // placeholder sessions for visual only

        OnPropertyChanged(nameof(HasActiveSessions));
        OnPropertyChanged(nameof(HasAnySessions));

        try
        {
            await _tunnelService.StartAllAsync(
                Assignments,
                provider => GetExePath(provider),
                provider => GetApiKey(provider)
            );
        }
        finally
        {
            IsLaunching = false;
            // Refresh displayed sessions from the service
            RebuildSessionViewModels();
        }
    }

    // -----------------------------------------------------------------------
    // Stop
    // -----------------------------------------------------------------------

    private void StopAll()
    {
        _tunnelService?.StopAll();
        SessionViewModels = new ObservableCollection<TunnelSessionViewModel>();
        OnPropertyChanged(nameof(HasActiveSessions));
        OnPropertyChanged(nameof(HasAnySessions));
        (LaunchAllTunnelsCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (StopAllTunnelsCommand   as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void StopOne(int port)
    {
        _tunnelService?.StopOne(port);
        RebuildSessionViewModels();
    }

    // -----------------------------------------------------------------------
    // Session changed (from TunnelService)
    // -----------------------------------------------------------------------

    private void OnSessionChanged(object? sender, TunnelSessionChangedEventArgs e)
    {
        // Marshal to UI thread
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var svm = _sessionViewModels.FirstOrDefault(
                s => s.Port == e.Session.Assignment.Port.Port);

            if (svm != null)
                svm.RefreshFromSession(e.Session);
            else
                RebuildSessionViewModels();

            OnPropertyChanged(nameof(HasActiveSessions));
            OnPropertyChanged(nameof(HasAnySessions));
            (LaunchAllTunnelsCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (StopAllTunnelsCommand   as RelayCommand)?.RaiseCanExecuteChanged();
        });
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void RebuildSessionViewModels()
    {
        var sessions = _tunnelService?.Sessions ?? new List<TunnelSession>();
        SessionViewModels = new ObservableCollection<TunnelSessionViewModel>(
            sessions.Select(s => new TunnelSessionViewModel(s)));
        OnPropertyChanged(nameof(HasActiveSessions));
        OnPropertyChanged(nameof(HasAnySessions));
    }

    private string GetExePath(TunnelProvider provider)
    {
        // Look up the exe path from settings (stored as "tunnel.{id}.exe" key)
        // Fall back to just the provider id (assumes it's on PATH)
        var settings = _settingsService?.GetSettings();
        if (settings?.TunnelExePaths != null &&
            settings.TunnelExePaths.TryGetValue(provider.Id, out var path) &&
            !string.IsNullOrEmpty(path))
            return path;

        // Default: assume the binary name is the provider id and is on PATH
        return provider.Id switch
        {
            "playit"        => "playit",
            "playit-premium"=> "playit",
            "ngrok"         => "ngrok",
            "ngrok-pro"     => "ngrok",
            "bore"          => "bore",
            "serveo"        => "ssh",
            "frp"           => "frpc",
            _               => provider.Id
        };
    }

    private string? GetApiKey(TunnelProvider provider)
    {
        if (!provider.RequiresApiKey) return null;
        var settings = _settingsService?.GetSettings();
        if (settings?.TunnelApiKeys != null &&
            settings.TunnelApiKeys.TryGetValue(provider.Id, out var key))
            return key;
        return null;
    }

    // Settings service stored as a field (set in InitLaunch)
    private ISettingsService? _settingsService;
}