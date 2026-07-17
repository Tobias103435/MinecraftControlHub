using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using MinecraftControlHub.Core.Models;
using MinecraftControlHub.Core.Services;

namespace MinecraftControlHub.UI.Windows;

/// <summary>
/// Friend-selection dialog for sharing a tunnel address.
/// Loads the friend list from Nexora and lets the user select one or more.
/// </summary>
public partial class ShareTunnelWindow : Window, INotifyPropertyChanged
{
    // ── Dependencies ──────────────────────────────────────────────────────────
    private readonly INexoraApiService     _api;
    private readonly INexoraAccountService _nexoraService;
    private readonly ITunnelShareService   _shareService;

    // ── Tunnel data ───────────────────────────────────────────────────────────
    private readonly string  _ip;
    private readonly int     _port;
    private readonly string? _serverName;

    // ── State ─────────────────────────────────────────────────────────────────
    private readonly ObservableCollection<SelectableFriend> _friends = new();
    private bool   _hasStatusMessage;
    private bool   _hasServerName;
    private bool   _hasSelection;
    private string _statusMessage = string.Empty;

    // ── INotifyPropertyChanged ────────────────────────────────────────────────
    public new event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public bool HasStatusMessage
    {
        get => _hasStatusMessage;
        private set { _hasStatusMessage = value; OnPropertyChanged(); }
    }

    public bool HasServerName
    {
        get => _hasServerName;
        private set { _hasServerName = value; OnPropertyChanged(); }
    }

    public bool HasSelection
    {
        get => _hasSelection;
        private set { _hasSelection = value; OnPropertyChanged(); }
    }

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <param name="publicAddress">The full "ip:port" or just IP string shown in the tunnel panel.</param>
    /// <param name="serverName">Optional server name.</param>
    public ShareTunnelWindow(string publicAddress, string? serverName = null)
    {
        InitializeComponent();
        DataContext = this;

        // Parse ip:port from the public address string
        var parts = publicAddress.Split(':', 2);
        _ip   = parts[0].Trim();
        _port = parts.Length > 1 && int.TryParse(parts[1].Trim(), out var p) ? p : 25565;
        _serverName = serverName;

        var app = (App)Application.Current!;
        _api           = app.ServiceProvider!.GetRequiredService<INexoraApiService>();
        _nexoraService = app.ServiceProvider!.GetRequiredService<INexoraAccountService>();
        _shareService  = app.ServiceProvider!.GetRequiredService<ITunnelShareService>();

        // Populate tunnel info card
        TunnelAddressText.Text = publicAddress;
        HasServerName = !string.IsNullOrWhiteSpace(serverName);
        if (HasServerName) ServerNameText.Text = serverName;

        FriendsList.ItemsSource = _friends;

        Loaded += async (_, _) => await LoadFriendsAsync();
    }

    // ── Load friends ──────────────────────────────────────────────────────────

    private async Task LoadFriendsAsync()
    {
        LoadingText.IsVisible       = true;
        FriendsScrollViewer.IsVisible = false;
        EmptyText.IsVisible         = false;

        var token = _nexoraService.Current?.Token;
        if (string.IsNullOrEmpty(token))
        {
            ShowStatus("You must be logged in to Nexora to share tunnels.");
            LoadingText.IsVisible = false;
            return;
        }

        var result = await _api.GetFriendsAsync(token);

        LoadingText.IsVisible = false;

        if (!result.Success || result.Data == null || result.Data.Count == 0)
        {
            EmptyText.IsVisible = true;
            return;
        }

        _friends.Clear();
        foreach (var f in result.Data)
            _friends.Add(new SelectableFriend(f));

        FriendsScrollViewer.IsVisible = true;
        UpdateSelectionState();
    }

    // ── CheckBox handler ──────────────────────────────────────────────────────

    private void FriendCheckBox_Changed(object sender, RoutedEventArgs e)
        => UpdateSelectionState();

    private void UpdateSelectionState()
    {
        var selected = _friends.Where(f => f.IsSelected).ToList();
        var count    = selected.Count;

        HasSelection = count > 0;
        ShareButton.IsEnabled = HasSelection;

        SelectedCountText.Text = count > 0
            ? $"{count} selected"
            : string.Empty;

        if (count == 0)
        {
            SelectedSummaryText.Text = string.Empty;
        }
        else
        {
            var names = string.Join(", ", selected.Select(f => f.WebsiteUsername));
            SelectedSummaryText.Text = $"Sharing with: {names}";
        }
    }

    // ── Share ─────────────────────────────────────────────────────────────────

    private async void Share_Click(object sender, RoutedEventArgs e)
    {
        var selected = _friends.Where(f => f.IsSelected).ToList();
        if (selected.Count == 0) return;

        ShareButton.IsEnabled = false;
        SendStatusText.Text   = "Sending…";

        var token    = _nexoraService.Current?.Token ?? string.Empty;
        var senderName = _nexoraService.Current?.Username ?? string.Empty;
        var names      = selected.Select(f => f.WebsiteUsername);

        var result = await _shareService.ShareTunnelAsync(
            token, names, _ip, _port, _serverName, senderName);

        if (result.Success)
        {
            SendStatusText.Text = $"✓ Shared with {selected.Count} friend{(selected.Count == 1 ? "" : "s")}!";
            await Task.Delay(900);
            Close();
        }
        else
        {
            ShareButton.IsEnabled = true;
            SendStatusText.Text   = string.Empty;
            ShowStatus(result.Error ?? "Failed to share tunnel. Please try again.");
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => Close();

    // ── Status ────────────────────────────────────────────────────────────────

    private void ShowStatus(string message)
    {
        _statusMessage          = message;
        StatusMessageText.Text  = message;
        HasStatusMessage        = !string.IsNullOrEmpty(message);
    }
}

/// <summary>
/// A <see cref="Friend"/> decorated with an IsSelected flag for the multi-select UI.
/// </summary>
public class SelectableFriend : INotifyPropertyChanged
{
    private bool _isSelected;

    public string  WebsiteUsername  { get; }
    public string? MinecraftUsername { get; }
    public string  Initial          { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public SelectableFriend(Friend f)
    {
        WebsiteUsername  = f.WebsiteUsername;
        MinecraftUsername = f.MinecraftUsername;
        Initial          = f.Initial;
    }
}
