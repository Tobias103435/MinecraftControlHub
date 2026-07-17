using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using MinecraftControlHub.UI.Helpers;
using MinecraftControlHub.UI.ViewModels;
using MinecraftControlHub.UI.Windows;

namespace MinecraftControlHub.UI.Pages;

public partial class SettingsPage : UserControl
{
    private readonly SettingsPageViewModel? _viewModel;

    // All page panels (order matches nav items)
    private ScrollViewer[] _pages = [];

    // Search keywords per nav item (index maps to _pages)
    private static readonly string[][] SearchKeywords =
    [
        ["account", "nexora", "microsoft", "minecraft", "login", "sign in", "offline", "player name", "linked"],
        ["java", "performance", "ram", "memory", "jvm", "arguments"],
        ["launch", "close", "resolution", "window", "game"],
        ["mod", "update", "backup", "notify", "curseforge"],
        ["tunnel", "network", "ngrok", "playit", "bore", "frp", "port", "server"],
        ["ai", "terminal", "api", "key", "model", "provider", "endpoint"],
        ["appearance", "theme", "dark", "light", "mode"],
        ["storage", "data", "folder", "log", "reset", "cache", "diagnostics"]
    ];

    public SettingsPage()
    {
        InitializeComponent();
        var serviceProvider = (Application.Current as App)?.ServiceProvider;
        if (serviceProvider != null)
        {
            _viewModel = serviceProvider.GetRequiredService<SettingsPageViewModel>();
            DataContext = _viewModel;

            // Pre-fill the API-key box with the saved key
            var savedKey = _viewModel.LoadedAiApiKey;
            if (!string.IsNullOrEmpty(savedKey))
            {
                AiApiKeyBox.Text    = savedKey;
                _viewModel.AiApiKey = savedKey;
            }

            Loaded += async (_, _) =>
            {
                UpdateThemeButtonLabel();
                _pages = [PageAccount, PageJava, PageLaunch, PageMods, PageTunnel, PageAi, PageAppearance, PageStorage];
                ShowPage("PageAccount");
                await _viewModel.RefreshNexoraProfileAsync();
            };
        }
    }

    // ── Page switching ──────────────────────────────────────────────────

    private void NavRadio_Checked(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not RadioButton { IsChecked: true } rb) return;
        var tag = rb.Tag as string;
        if (string.IsNullOrEmpty(tag) || _pages.Length == 0) return;
        ShowPage(tag);
    }

    private void ShowPage(string pageName)
    {
        foreach (var page in _pages)
        {
            page.IsVisible = page.Name == pageName;
        }
    }

    // ── Search ──────────────────────────────────────────────────────────

    private void SearchBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        var query = (SearchBox.Text ?? string.Empty).Trim().ToLowerInvariant();
        SearchPlaceholder.IsVisible = string.IsNullOrEmpty(query);

        // Get all nav radio buttons
        var navButtons = new RadioButton[] { NavAccount, NavJava, NavLaunch, NavMods, NavTunnel, NavAi, NavAppearance, NavStorage };

        if (string.IsNullOrEmpty(query))
        {
            // Show all nav items
            foreach (var btn in navButtons)
                btn.IsVisible = true;
            return;
        }

        // Filter nav items based on keywords
        bool anyMatch = false;
        RadioButton? firstMatch = null;

        for (int i = 0; i < navButtons.Length; i++)
        {
            bool matches = SearchKeywords[i].Any(kw => kw.Contains(query)) ||
                           (navButtons[i].Content?.ToString()?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);

            navButtons[i].IsVisible = matches;

            if (matches && !anyMatch)
            {
                firstMatch = navButtons[i];
                anyMatch = true;
            }
        }

        // Auto-navigate to first matching page
        if (firstMatch != null && firstMatch.IsChecked != true)
        {
            firstMatch.IsChecked = true;
        }
    }

    // ── Microsoft account ───────────────────────────────────────────────

    private async void SignIn_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
            await _viewModel.SignInAsync();
    }

    private void SignOut_Click(object? sender, RoutedEventArgs e)
    {
        _viewModel?.SignOut();
    }

    private async void CopyCode_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null || string.IsNullOrEmpty(_viewModel.DeviceCode)) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
            await clipboard.SetTextAsync(_viewModel.DeviceCode);
    }

    // ── Nexora account ──────────────────────────────────────────────────

    private async void NexoraLogin_Click(object? sender, RoutedEventArgs e)
    {
        var window = TopLevel.GetTopLevel(this) as Window;
        var loginWindow = new NexoraLoginWindow();

        if (window != null)
            await loginWindow.ShowDialog(window);
        else
            loginWindow.Show();

        if (loginWindow.LoginSuccessful)
        {
            (window as MainWindow)?.OnNexoraLoginSuccessful();
            _viewModel?.NotifyNexoraStateChanged();
        }
    }

    private async void NexoraUnlink_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        var window = TopLevel.GetTopLevel(this) as Window;
        var confirm = await SimpleDialog.ConfirmAsync(window,
            "This will unlink your Minecraft account from Nexora.\n\nContinue?",
            "Unlink Minecraft account");
        if (!confirm) return;
        await _viewModel.NexoraUnlinkMinecraftAsync();
    }

    private async void NexoraLogout_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        var window = TopLevel.GetTopLevel(this) as Window;
        var confirm = await SimpleDialog.ConfirmAsync(window,
            "Log out of your Nexora account?\n\nSocial features will be unavailable until you log in again.",
            "Log out of Nexora");
        if (!confirm) return;
        _viewModel.NexoraLogout();
        (window as MainWindow)?.OnNexoraLogout();
    }

    private void NexoraSettings_Click(object? sender, RoutedEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName        = "https://nexoragames.nl/settings",
            UseShellExecute = true
        });
    }

    // ── Mod updates ─────────────────────────────────────────────────────

    private async void CheckUpdates_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
            await _viewModel.CheckForUpdatesAsync();
    }

    // ── Storage & Data ──────────────────────────────────────────────────

    private void OpenDataFolder_Click(object? sender, RoutedEventArgs e)
    {
        _viewModel?.OpenDataFolder();
    }

    private void OpenDiagnosticsLog_Click(object? sender, RoutedEventArgs e)
    {
        _viewModel?.OpenDiagnosticsLog();
    }

    private async void ClearCache_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;

        var window = TopLevel.GetTopLevel(this) as Window;
        var confirm = await SimpleDialog.ConfirmAsync(window,
            "This deletes ALL Minecraft Control Hub data (settings, installations, installed mods, your account and every cached game file) and restarts the app.\n\nThis cannot be undone. Continue?",
            "Reset Minecraft Control Hub");

        if (!confirm)
            return;

        _viewModel.ResetApp();

        try
        {
            var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(exePath))
                System.Diagnostics.Process.Start(exePath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Restart after reset failed: {ex.Message}");
        }

        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }

    // ── Tunnel settings ─────────────────────────────────────────────────

    private void NgrokApiKey_Changed(object? sender, TextChangedEventArgs e)
    {
        if (_viewModel is null) return;
        if (sender is TextBox tb)
            _viewModel.NgrokApiKey = tb.Text ?? string.Empty;
    }

    private void FrpApiKey_Changed(object? sender, TextChangedEventArgs e)
    {
        if (_viewModel is null) return;
        if (sender is TextBox tb)
            _viewModel.FrpApiKey = tb.Text ?? string.Empty;
    }

    private async void BrowseExe_Click(object? sender, RoutedEventArgs e)
    {
        var tag = (sender as Button)?.Tag as string ?? string.Empty;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null || _viewModel is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select exe",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Executable files") { Patterns = new[] { "*.exe" } },
                FilePickerFileTypes.All
            }
        });

        if (files.Count == 0) return;
        var path = files[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        switch (tag)
        {
            case "playit": _viewModel.PlayitExePath = path; break;
            case "ngrok":  _viewModel.NgrokExePath  = path; break;
            case "bore":   _viewModel.BoreExePath   = path; break;
            case "frp":    _viewModel.FrpExePath    = path; break;
        }
    }

    private async void SaveTunnelSettings_Click(object? sender, RoutedEventArgs e)
    {
        _viewModel?.SaveTunnelSettings();
        var window = TopLevel.GetTopLevel(this) as Window;
        await SimpleDialog.InfoAsync(window, "Tunnel settings saved.", "Saved");
    }

    // ── AI settings ─────────────────────────────────────────────────────

    private void AiApiKey_Changed(object? sender, TextChangedEventArgs e)
    {
        if (_viewModel is null) return;
        if (sender is TextBox tb)
            _viewModel.AiApiKey = tb.Text ?? string.Empty;
    }

    private async void SaveAiSettings_Click(object? sender, RoutedEventArgs e)
    {
        _viewModel?.SaveAiSettings();
        var window = TopLevel.GetTopLevel(this) as Window;
        await SimpleDialog.InfoAsync(window, "AI settings saved.", "Saved");
    }

    // ── Appearance ──────────────────────────────────────────────────────

    private async void ToggleTheme_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        await _viewModel.ToggleThemeAsync();
        UpdateThemeButtonLabel();
    }

    private void UpdateThemeButtonLabel()
    {
        if (_viewModel == null) return;
        ThemeToggleButton.Content = _viewModel.IsLightTheme
            ? "Switch to Dark mode"
            : "Switch to Light mode";
    }

    // Avalonia has no Hyperlink inline control — see AccountPage.xaml.cs for the
    // same pattern (underlined TextBlock + Tag holding the URL).
    private void OpenLink_Click(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { Tag: string uri } || string.IsNullOrWhiteSpace(uri))
            return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = uri,
                UseShellExecute = true
            });
        }
        catch { /* user can open it manually */ }
    }

    private void KoFi_Click(object? sender, RoutedEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName        = "https://ko-fi.com/nexoralauncher",
            UseShellExecute = true
        });
    }
}
