using System.Windows;
using Microsoft.Win32;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using MinecraftControlHub.UI.ViewModels;
using MinecraftControlHub.UI.Windows;

namespace MinecraftControlHub.UI.Pages;

public partial class SettingsPage : UserControl
{
    private readonly SettingsPageViewModel? _viewModel;

    public SettingsPage()
    {
        InitializeComponent();
        var serviceProvider = (Application.Current as App)?.ServiceProvider;
        if (serviceProvider != null)
        {
            _viewModel = serviceProvider.GetRequiredService<SettingsPageViewModel>();
            DataContext = _viewModel;

            // Pre-fill the PasswordBox with the saved API key so the user can
            // see that a key exists and edit/replace it directly.
            // We also sync _aiApiKey so a Save without re-typing preserves the key.
            var savedKey = _viewModel.LoadedAiApiKey;
            if (!string.IsNullOrEmpty(savedKey))
            {
                AiApiKeyBox.Password = savedKey;
                _viewModel.AiApiKey  = savedKey;
            }

            // Set initial theme button label
            Loaded += (_, _) => UpdateThemeButtonLabel();
        }
    }

    private async void CreateInstallation_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
            await _viewModel.CreateInstallationAsync();
    }

    private void DownloadJava_Click(object sender, RoutedEventArgs e)
    {
        _viewModel?.OpenJavaDownload();
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
            await _viewModel.ImportInstallationsAsync();
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
            await _viewModel.CheckForUpdatesAsync();
    }

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

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        _viewModel?.OpenDataFolder();
    }

    private void OpenDiagnosticsLog_Click(object sender, RoutedEventArgs e)
    {
        _viewModel?.OpenDiagnosticsLog();
    }

    // ── Nexora account actions ────────────────────────────────────────────

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

    // -----------------------------------------------------------------------
    // Tunnel settings handlers
    // -----------------------------------------------------------------------

    private void NgrokApiKey_Changed(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        if (sender is System.Windows.Controls.PasswordBox pb)
            _viewModel.NgrokApiKey = pb.Password;
    }

    private void FrpApiKey_Changed(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        if (sender is System.Windows.Controls.PasswordBox pb)
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

    private void AiApiKey_Changed(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        if (sender is System.Windows.Controls.PasswordBox pb)
            _viewModel.AiApiKey = pb.Password;
    }

    private void SaveAiSettings_Click(object sender, RoutedEventArgs e)
    {
        _viewModel?.SaveAiSettings();
        MessageBox.Show("AI settings saved.", "Saved",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

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
            ? "🌙 Switch to Dark mode"
            : "☀ Switch to Light mode";
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
}
