using System.Windows;
using Microsoft.Win32;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.Extensions.DependencyInjection;
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

            // Pre-fill the PasswordBox with the saved API key
            var savedKey = _viewModel.LoadedAiApiKey;
            if (!string.IsNullOrEmpty(savedKey))
            {
                AiApiKeyBox.Password = savedKey;
                _viewModel.AiApiKey  = savedKey;
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

    private void NavRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb) return;
        var tag = rb.Tag as string;
        if (string.IsNullOrEmpty(tag) || _pages.Length == 0) return;
        ShowPage(tag);
    }

    private void ShowPage(string pageName)
    {
        foreach (var page in _pages)
        {
            page.Visibility = page.Name == pageName
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    // ── Search ──────────────────────────────────────────────────────────

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = SearchBox.Text.Trim().ToLowerInvariant();
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(query)
            ? Visibility.Visible
            : Visibility.Collapsed;

        // Get all nav radio buttons
        var navButtons = new RadioButton[] { NavAccount, NavJava, NavLaunch, NavMods, NavTunnel, NavAi, NavAppearance, NavStorage };

        if (string.IsNullOrEmpty(query))
        {
            // Show all nav items
            foreach (var btn in navButtons)
                btn.Visibility = Visibility.Visible;
            return;
        }

        // Filter nav items based on keywords
        bool anyMatch = false;
        RadioButton? firstMatch = null;

        for (int i = 0; i < navButtons.Length; i++)
        {
            bool matches = SearchKeywords[i].Any(kw => kw.Contains(query)) ||
                           (navButtons[i].Content?.ToString()?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);

            navButtons[i].Visibility = matches ? Visibility.Visible : Visibility.Collapsed;

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

    private async void SignIn_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
            await _viewModel.SignInAsync();
    }

    private void SignOut_Click(object sender, RoutedEventArgs e)
    {
        _viewModel?.SignOut();
    }

    private void CopyCode_Click(object sender, RoutedEventArgs e)
    {
        _viewModel?.CopyDeviceCode();
    }

    // ── Nexora account ──────────────────────────────────────────────────

    private void NexoraLogin_Click(object sender, RoutedEventArgs e)
    {
        var window = Window.GetWindow(this);
        var loginWindow = new NexoraLoginWindow { Owner = window };
        loginWindow.ShowDialog();
        if (loginWindow.LoginSuccessful)
        {
            (window as MainWindow)?.OnNexoraLoginSuccessful();
            _viewModel?.NotifyNexoraStateChanged();
        }
    }

    private async void NexoraUnlink_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        var confirm = MessageBox.Show(
            "This will unlink your Minecraft account from Nexora.\n\nContinue?",
            "Unlink Minecraft account",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;
        await _viewModel.NexoraUnlinkMinecraftAsync();
    }

    private void NexoraLogout_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        var confirm = MessageBox.Show(
            "Log out of your Nexora account?\n\nSocial features will be unavailable until you log in again.",
            "Log out of Nexora",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;
        _viewModel.NexoraLogout();
        (Window.GetWindow(this) as MainWindow)?.OnNexoraLogout();
    }

    private void NexoraSettings_Click(object sender, RoutedEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName        = "https://nexoragames.nl/settings",
            UseShellExecute = true
        });
    }

    // ── Mod updates ─────────────────────────────────────────────────────

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
            await _viewModel.CheckForUpdatesAsync();
    }

    // ── Storage & Data ──────────────────────────────────────────────────

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        _viewModel?.OpenDataFolder();
    }

    private void OpenDiagnosticsLog_Click(object sender, RoutedEventArgs e)
    {
        _viewModel?.OpenDiagnosticsLog();
    }

    private void ClearCache_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;

        var confirm = MessageBox.Show(
            "This deletes ALL Minecraft Control Hub data (settings, installations, installed mods, your account and every cached game file) and restarts the app.\n\nThis cannot be undone. Continue?",
            "Reset Minecraft Control Hub",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
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

        Application.Current.Shutdown();
    }

    // ── Tunnel settings ─────────────────────────────────────────────────

    private void NgrokApiKey_Changed(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        if (sender is PasswordBox pb)
            _viewModel.NgrokApiKey = pb.Password;
    }

    private void FrpApiKey_Changed(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        if (sender is PasswordBox pb)
            _viewModel.FrpApiKey = pb.Password;
    }

    private void BrowseExe_Click(object sender, RoutedEventArgs e)
    {
        var tag = (sender as Button)?.Tag as string ?? string.Empty;

        var dlg = new OpenFileDialog
        {
            Title            = "Select exe",
            Filter           = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists  = true,
            CheckPathExists  = true
        };

        if (dlg.ShowDialog() != true) return;
        if (_viewModel is null) return;

        switch (tag)
        {
            case "playit": _viewModel.PlayitExePath = dlg.FileName; break;
            case "ngrok":  _viewModel.NgrokExePath  = dlg.FileName; break;
            case "bore":   _viewModel.BoreExePath   = dlg.FileName; break;
            case "frp":    _viewModel.FrpExePath    = dlg.FileName; break;
        }
    }

    private void SaveTunnelSettings_Click(object sender, RoutedEventArgs e)
    {
        _viewModel?.SaveTunnelSettings();
        MessageBox.Show("Tunnel settings saved.", "Saved",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ── AI settings ─────────────────────────────────────────────────────

    private void AiApiKey_Changed(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        if (sender is PasswordBox pb)
            _viewModel.AiApiKey = pb.Password;
    }

    private void SaveAiSettings_Click(object sender, RoutedEventArgs e)
    {
        _viewModel?.SaveAiSettings();
        MessageBox.Show("AI settings saved.", "Saved",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ── Appearance ──────────────────────────────────────────────────────

    private async void ToggleTheme_Click(object sender, RoutedEventArgs e)
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

    private void OpenLink_Click(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = e.Uri.AbsoluteUri,
            UseShellExecute = true
        });
        e.Handled = true;
    }

    private void KoFi_Click(object sender, RoutedEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName        = "https://ko-fi.com/nexoralauncher",
            UseShellExecute = true
        });
    }
}
