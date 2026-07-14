using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using MinecraftControlHub.Core.Models;
using MinecraftControlHub.UI.ViewModels;
using MinecraftControlHub.UI.Windows;

namespace MinecraftControlHub.UI.Pages;

public partial class HomePage : UserControl
{
    private HomePageViewModel? _viewModel;

    public HomePage()
    {
        InitializeComponent();
        var serviceProvider = (Application.Current as App)?.ServiceProvider;
        if (serviceProvider != null)
        {
            _viewModel = serviceProvider.GetRequiredService<HomePageViewModel>();
            DataContext = _viewModel;
        }
    }

    // ── Installation cards ───────────────────────────────────────────────────

    private async void Play_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        // DataContext is now InstallationCardViewModel
        var card = (sender as Button)?.DataContext as InstallationCardViewModel;
        var installation = card?.Installation
            ?? (sender as Button)?.DataContext as Installation;
        if (installation != null)
            await _viewModel.PlayInstallationAsync(installation);
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        var card = (sender as Button)?.DataContext as InstallationCardViewModel;
        var installation = card?.Installation
            ?? (sender as Button)?.DataContext as Installation;
        if (installation == null) return;

        await _viewModel.OpenInstallationDetailAsync(installation);

        var win = new InstallationSettingsWindow(_viewModel)
        {
            Owner = Window.GetWindow(this)
        };
        win.ShowDialog();

        // Reload mods on the card after settings close (user might have added/removed mods)
        card?.InvalidateMods();
        _viewModel.CloseInstallationDetail();
    }

    private async void DeleteInstallation_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        var card = (sender as Button)?.DataContext as InstallationCardViewModel;
        var installation = card?.Installation
            ?? (sender as Button)?.DataContext as Installation;
        if (installation == null) return;

        var confirm = MessageBox.Show(
            $"Delete \"{installation.Name}\"?\n\nThis also removes its game folder. This cannot be undone.",
            "Delete installation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm == MessageBoxResult.Yes)
            await _viewModel.DeleteInstallationAsync(installation);
    }

    private void ExpandMods_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is InstallationCardViewModel card)
            card.ToggleExpand();
    }

    private async void ModToggle_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as System.Windows.Controls.Primitives.ToggleButton)?.DataContext is ModRowViewModel row)
            await row.ToggleAsync();
    }

    // ── New installation dialog ──────────────────────────────────────────────

    private void NewInstallation_Click(object sender, RoutedEventArgs e)
        => _viewModel?.OpenCreateDialog();

    private void CancelCreate_Click(object sender, RoutedEventArgs e)
        => _viewModel?.CloseCreateDialog();

    private async void CreateInstallation_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
            await _viewModel.CreateInstallationAsync();
    }

    private void MinecraftVersionCombo_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (MinecraftVersionCombo.ItemsSource == null) return;
        if (MinecraftVersionCombo.HasItems && !MinecraftVersionCombo.IsDropDownOpen)
            MinecraftVersionCombo.IsDropDownOpen = true;
        else if (!MinecraftVersionCombo.HasItems && MinecraftVersionCombo.IsDropDownOpen)
            MinecraftVersionCombo.IsDropDownOpen = false;
    }

    // ── Import dropdown ──────────────────────────────────────────────────────

    private void ImportBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
        {
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            btn.ContextMenu.IsOpen = true;
        }
    }

    private async void ImportMrpack_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        var dialog = new OpenFileDialog
        {
            Filter = "Modrinth / Prism modpack (*.mrpack;*.zip)|*.mrpack;*.zip|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
            Title = "Import a modpack (.mrpack)"
        };
        if (dialog.ShowDialog() == true)
            await _viewModel.ImportInstallationAsync(dialog.FileName);
    }

    private async void ImportPrismZip_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        var dialog = new OpenFileDialog
        {
            Filter = "Prism Launcher instance zip (*.zip)|*.zip|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
            Title = "Import a Prism Launcher instance zip"
        };
        if (dialog.ShowDialog() == true)
            await _viewModel.ImportInstallationAsync(dialog.FileName);
    }

    // ── Sign-in overlay ──────────────────────────────────────────────────────

    private async void SignIn_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
            await _viewModel.SignInAndPlayAsync();
    }

    private async void PlayOffline_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
            await _viewModel.PlayOfflineAsync();
    }

    private void CancelLogin_Click(object sender, RoutedEventArgs e)
        => _viewModel?.CloseLoginOverlay();

    private void CopyCode_Click(object sender, RoutedEventArgs e)
    {
        // DeviceCode is a property on the ViewModel — copy it ourselves
        var code = _viewModel?.DeviceCode;
        if (!string.IsNullOrEmpty(code))
        {
            try { Clipboard.SetText(code); }
            catch { /* clipboard locked */ }
        }
    }

    // ── Launch overlay ───────────────────────────────────────────────────────

    private void FinishLaunch_Click(object sender, RoutedEventArgs e)
        => _viewModel?.CloseLaunchOverlay();

    private void CopyLaunchLog_Click(object sender, RoutedEventArgs e)
    {
        var log = _viewModel?.LaunchMessage;
        if (!string.IsNullOrEmpty(log))
        {
            try { Clipboard.SetText(log); }
            catch { /* clipboard locked */ }
        }
    }

    // ── Share overlay handlers ────────────────────────────────────────────────

    private void ShareInstallation_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        var card = (sender as Button)?.DataContext as InstallationCardViewModel;
        if (card?.Installation == null) return;

        var window = new ShareInstanceWindow(card.Installation, _viewModel)
        {
            Owner = Window.GetWindow(this)
        };
        window.ShowDialog();
    }

    private void CloseShare_Click(object sender, RoutedEventArgs e)
        => _viewModel?.CloseShareOverlay();

    private void ShareTabFriends_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null) _viewModel.ShareTab = "Friends";
    }

    private void ShareTabCode_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null) _viewModel.ShareTab = "Code";
    }

    private async void ShareWithFriends_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null) await _viewModel.ShareWithFriendsAsync();
    }

    private async void GenerateCode_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null) await _viewModel.GenerateShareCodeAsync();
    }

    private void CopyShareCode_Click(object sender, RoutedEventArgs e)
        => _viewModel?.CopyShareCode();

    private async void ImportFromCode_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null) await _viewModel.ImportFromCodeAsync();
    }

    private async void AcceptShareNotif_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        if ((sender as Button)?.Tag is InstanceShareNotification notif)
            await _viewModel.AcceptShareNotificationAsync(notif);
    }

    private async void DeclineShareNotif_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        if ((sender as Button)?.Tag is InstanceShareNotification notif)
            await _viewModel.DeclineShareNotificationAsync(notif);
    }

    private void ImportWithCode_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;

        var popup = new Window
        {
            Title                 = "Import with code",
            Width                 = 420,
            SizeToContent         = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner                 = Window.GetWindow(this),
            ResizeMode            = ResizeMode.NoResize,
            ShowInTaskbar         = false,
            Background            = (System.Windows.Media.Brush)FindResource("BrushAppBackground"),
            WindowStyle           = WindowStyle.SingleBorderWindow
        };

        var outer = new System.Windows.Controls.Border
        {
            Padding         = new Thickness(28, 24, 28, 24),
            Background      = (System.Windows.Media.Brush)FindResource("BrushAppBackground"),
        };

        var grid = new System.Windows.Controls.Grid();
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });

        // Title
        var title = new System.Windows.Controls.TextBlock
        {
            Text            = "Import with code",
            FontSize        = 18,
            FontFamily      = (System.Windows.Media.FontFamily)FindResource("FontDisplay"),
            Foreground      = (System.Windows.Media.Brush)FindResource("BrushTextPrimary"),
            Margin          = new Thickness(0, 0, 0, 6)
        };
        System.Windows.Controls.Grid.SetRow(title, 0);

        // Subtitle
        var sub = new System.Windows.Controls.TextBlock
        {
            Text            = "Paste a share code from someone else to import their instance.",
            FontSize        = 12,
            FontFamily      = (System.Windows.Media.FontFamily)FindResource("FontBody"),
            Foreground      = (System.Windows.Media.Brush)FindResource("BrushTextSecondary"),
            TextWrapping    = TextWrapping.Wrap,
            Margin          = new Thickness(0, 0, 0, 16)
        };
        System.Windows.Controls.Grid.SetRow(sub, 1);

        // Code box
        var codeBox = new System.Windows.Controls.TextBox
        {
            FontSize        = 20,
            FontFamily      = new System.Windows.Media.FontFamily("Consolas"),
            Padding         = new Thickness(14, 10, 14, 10),
            Background      = (System.Windows.Media.Brush)FindResource("BrushPanelElevated"),
            Foreground      = (System.Windows.Media.Brush)FindResource("BrushTextPrimary"),
            BorderBrush     = (System.Windows.Media.Brush)FindResource("BrushBorder"),
            BorderThickness = new Thickness(1),
            Margin          = new Thickness(0, 0, 0, 16),
            CharacterCasing = System.Windows.Controls.CharacterCasing.Upper,
            MaxLength       = 8
        };
        System.Windows.Controls.Grid.SetRow(codeBox, 2);

        // Buttons
        var btnRow = new System.Windows.Controls.StackPanel
        {
            Orientation         = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        System.Windows.Controls.Grid.SetRow(btnRow, 3);

        var cancelBtn = new System.Windows.Controls.Button
        {
            Content = "Cancel",
            Padding = new Thickness(18, 8, 18, 8),
            Margin  = new Thickness(0, 0, 8, 0),
            Style   = (Style)FindResource("SecondaryButtonStyle")
        };
        cancelBtn.Click += (_, _) => popup.Close();

        var importBtn = new System.Windows.Controls.Button
        {
            Content = "Import",
            Padding = new Thickness(18, 8, 18, 8),
            Style   = (Style)FindResource("PrimaryButtonStyle")
        };
        importBtn.Click += async (_, _) =>
        {
            var code = codeBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(code)) return;
            importBtn.IsEnabled  = false;
            cancelBtn.IsEnabled  = false;
            _viewModel.ImportCode = code;
            popup.Close();
            await _viewModel.ImportFromCodeAsync();
        };

        btnRow.Children.Add(cancelBtn);
        btnRow.Children.Add(importBtn);

        grid.Children.Add(title);
        grid.Children.Add(sub);
        grid.Children.Add(codeBox);
        grid.Children.Add(btnRow);
        outer.Child  = grid;
        popup.Content = outer;

        popup.Loaded += (_, _) => codeBox.Focus();
        popup.ShowDialog();
    }
}
