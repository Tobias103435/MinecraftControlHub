using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using MinecraftControlHub.Core.Models;
using MinecraftControlHub.Core.Services;
using MinecraftControlHub.UI.ViewModels;

namespace MinecraftControlHub.UI.Windows;

public partial class ShareInstanceWindow : Window, INotifyPropertyChanged
{
    private readonly INexoraApiService     _api;
    private readonly INexoraAccountService _nexoraService;
    private readonly IInstanceShareService _shareService;
    private readonly Installation          _installation;
    private readonly HomePageViewModel     _homeVm;

    private readonly ObservableCollection<SelectableFriend> _friends = new();

    private bool   _hasStatusMessage;
    private bool   _hasSelection;
    private bool   _isOnFriendsTab = true;
    private bool   _isOnCodeTab;

    public new event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public bool HasStatusMessage
    {
        get => _hasStatusMessage;
        private set { _hasStatusMessage = value; OnPropertyChanged(); }
    }

    public bool HasSelection
    {
        get => _hasSelection;
        private set { _hasSelection = value; OnPropertyChanged(); }
    }

    public bool IsOnFriendsTab
    {
        get => _isOnFriendsTab;
        private set { _isOnFriendsTab = value; OnPropertyChanged(); }
    }

    public bool IsOnCodeTab
    {
        get => _isOnCodeTab;
        private set { _isOnCodeTab = value; OnPropertyChanged(); }
    }

    public ShareInstanceWindow(Installation installation, HomePageViewModel homeVm)
    {
        InitializeComponent();
        DataContext = this;

        _installation = installation;
        _homeVm       = homeVm;

        var app = (App)Application.Current!;
        _api           = app.ServiceProvider!.GetRequiredService<INexoraApiService>();
        _nexoraService = app.ServiceProvider!.GetRequiredService<INexoraAccountService>();
        _shareService  = app.ServiceProvider!.GetRequiredService<IInstanceShareService>();

        InstanceNameText.Text    = installation.Name;
        InstanceVersionText.Text = $"{installation.MinecraftVersion} — {installation.Loader}";
        SubtitleText.Text        = "Select friends to share this instance with, or generate a code.";

        FriendsList.ItemsSource = _friends;
        Loaded += async (_, _) => await LoadFriendsAsync();
    }

    // ── Tabs ──────────────────────────────────────────────────────────────────

    private void TabFriends_Click(object sender, RoutedEventArgs e) => SwitchTab(friends: true);
    private void TabCode_Click(object sender, RoutedEventArgs e)    => SwitchTab(friends: false);

    private void SwitchTab(bool friends)
    {
        IsOnFriendsTab = friends;
        IsOnCodeTab    = !friends;
        FriendsTabPanel.IsVisible     = friends;
        CodeTabPanel.IsVisible        = !friends;
        SelectedSummaryBorder.IsVisible = friends && HasSelection;
        ShareButton.IsVisible         = friends;
        SendStatusText.Text            = string.Empty;
    }

    // ── Load friends ──────────────────────────────────────────────────────────

    private async Task LoadFriendsAsync()
    {
        LoadingText.IsVisible         = true;
        FriendsScrollViewer.IsVisible = false;
        EmptyText.IsVisible           = false;

        var token = _nexoraService.Current?.Token;
        if (string.IsNullOrEmpty(token))
        {
            ShowStatus("You must be signed in to your Nexora account to share instances.");
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

        HasSelection          = count > 0;
        ShareButton.IsEnabled = HasSelection;
        SelectedCountText.Text = count > 0 ? $"{count} selected" : string.Empty;

        if (count == 0)
        {
            SelectedSummaryText.Text         = string.Empty;
            SelectedSummaryBorder.IsVisible = false;
        }
        else
        {
            SelectedSummaryText.Text = $"Sharing with: {string.Join(", ", selected.Select(f => f.WebsiteUsername))}";
            SelectedSummaryBorder.IsVisible = true;
        }
    }

    // ── Share with friends ────────────────────────────────────────────────────

    private async void Share_Click(object sender, RoutedEventArgs e)
    {
        var selected = _friends.Where(f => f.IsSelected).ToList();
        if (selected.Count == 0) return;

        ShareButton.IsEnabled = false;
        SendStatusText.Text   = "Packing and sending…";

        var token      = _nexoraService.Current?.Token ?? string.Empty;
        var senderName = _nexoraService.Current?.Username ?? string.Empty;
        var recipients = selected.Select(f => f.WebsiteUsername);

        var progress = new Progress<string>(msg => Dispatcher.UIThread.Post(() => SendStatusText.Text = msg));
        var result   = await _shareService.ShareWithFriendsAsync(token, senderName, _installation, recipients, progress);

        if (result.Success)
        {
            SendStatusText.Text = $"✓ Shared with {selected.Count} friend{(selected.Count == 1 ? "" : "s")}!";
            await Task.Delay(900);
            Close();
        }
        else
        {
            ShareButton.IsEnabled = HasSelection;
            SendStatusText.Text   = string.Empty;
            ShowStatus(result.Error ?? "Failed to share. Please try again.");
        }
    }

    // ── Generate code ─────────────────────────────────────────────────────────

    private async void GenerateCode_Click(object sender, RoutedEventArgs e)
    {
        GenerateCodeBtn.IsEnabled = false;
        SendStatusText.Text       = "Generating code…";

        var token    = _nexoraService.Current?.Token ?? string.Empty;
        var progress = new Progress<string>(msg => Dispatcher.UIThread.Post(() => SendStatusText.Text = msg));
        var result   = await _shareService.GenerateShareCodeAsync(token, _installation, progress);

        if (result.Success && !string.IsNullOrWhiteSpace(result.Data))
        {
            ShareCodeText.Text          = result.Data;
            CodeDisplayBorder.IsVisible = true;
            SendStatusText.Text         = "Code ready — share it with anyone!";
        }
        else
        {
            GenerateCodeBtn.IsEnabled = true;
            SendStatusText.Text       = string.Empty;
            ShowStatus(result.Error ?? "Could not generate a code.");
        }
    }

    private async void CopyCode_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
                await clipboard.SetTextAsync(ShareCodeText.Text);
        }
        catch { }
        SendStatusText.Text = "Copied!";
    }

    // ── Import from code ──────────────────────────────────────────────────────

    private async void ImportFromCode_Click(object sender, RoutedEventArgs e)
    {
        var code = (ImportCodeBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(code)) return;

        SendStatusText.Text = "Downloading…";
        _homeVm.ImportCode  = code;
        await _homeVm.ImportFromCodeAsync();
        SendStatusText.Text = string.Empty;

        if (!string.IsNullOrEmpty(_homeVm.ShareStatus) && _homeVm.ShareStatus.Contains("✓"))
            Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    // ── Status ────────────────────────────────────────────────────────────────

    private void ShowStatus(string message)
    {
        StatusMessageText.Text = message;
        HasStatusMessage       = !string.IsNullOrEmpty(message);
    }
}
