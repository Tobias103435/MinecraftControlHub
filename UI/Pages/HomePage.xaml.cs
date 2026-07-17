using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Microsoft.Extensions.DependencyInjection;
using MinecraftControlHub.Core.Models;
using MinecraftControlHub.UI.Helpers;
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

    private async void Play_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        // DataContext is now InstallationCardViewModel
        var card = (sender as Button)?.DataContext as InstallationCardViewModel;
        var installation = card?.Installation
            ?? (sender as Button)?.DataContext as Installation;
        if (installation != null)
            await _viewModel.PlayInstallationAsync(installation);
    }

    private async void Settings_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        var card = (sender as Button)?.DataContext as InstallationCardViewModel;
        var installation = card?.Installation
            ?? (sender as Button)?.DataContext as Installation;
        if (installation == null) return;

        await _viewModel.OpenInstallationDetailAsync(installation);

        var win = new InstallationSettingsWindow(_viewModel);
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner != null)
            await win.ShowDialog(owner);
        else
            win.Show();

        // Reload mods on the card after settings close (user might have added/removed mods)
        card?.InvalidateMods();
        _viewModel.CloseInstallationDetail();
    }

    private async void DeleteInstallation_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        var card = (sender as Button)?.DataContext as InstallationCardViewModel;
        var installation = card?.Installation
            ?? (sender as Button)?.DataContext as Installation;
        if (installation == null) return;

        var owner = TopLevel.GetTopLevel(this) as Window;
        var confirm = await SimpleDialog.ConfirmAsync(owner,
            $"Delete \"{installation.Name}\"?\n\nThis also removes its game folder. This cannot be undone.",
            "Delete installation");

        if (confirm)
            await _viewModel.DeleteInstallationAsync(installation);
    }

    private void ExpandMods_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is InstallationCardViewModel card)
            card.ToggleExpand();
    }

    private async void ModToggle_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as ToggleButton)?.DataContext is ModRowViewModel row)
            await row.ToggleAsync();
    }

    // ── New installation dialog ──────────────────────────────────────────────

    private void NewInstallation_Click(object? sender, RoutedEventArgs e)
        => _viewModel?.OpenCreateDialog();

    private void CancelCreate_Click(object? sender, RoutedEventArgs e)
        => _viewModel?.CloseCreateDialog();

    private async void CreateInstallation_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
            await _viewModel.CreateInstallationAsync();
    }

    /// <summary>
    /// Opens/closes the Minecraft-version AutoCompleteBox's dropdown as the user types,
    /// based on whether the ViewModel's already-filtered version list has any matches —
    /// same intent as the original WPF handler (which checked the editable ComboBox's
    /// HasItems/ItemsSource against IsDropDownOpen).
    /// </summary>
    private void MinecraftVersionCombo_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_viewModel == null || MinecraftVersionCombo.ItemsSource == null) return;

        var hasItems = _viewModel.FilteredMinecraftVersions.Count > 0;
        if (hasItems && !MinecraftVersionCombo.IsDropDownOpen)
            MinecraftVersionCombo.IsDropDownOpen = true;
        else if (!hasItems && MinecraftVersionCombo.IsDropDownOpen)
            MinecraftVersionCombo.IsDropDownOpen = false;
    }

    // ── Import dropdown ──────────────────────────────────────────────────────

    private async void ImportMrpack_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import a modpack (.mrpack)",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Modrinth / Prism modpack") { Patterns = new[] { "*.mrpack", "*.zip" } },
                new FilePickerFileType("All files") { Patterns = new[] { "*.*" } }
            }
        });

        var path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (!string.IsNullOrEmpty(path))
            await _viewModel.ImportInstallationAsync(path);
    }

    private async void ImportPrismZip_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import a Prism Launcher instance zip",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Prism Launcher instance zip") { Patterns = new[] { "*.zip" } },
                new FilePickerFileType("All files") { Patterns = new[] { "*.*" } }
            }
        });

        var path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (!string.IsNullOrEmpty(path))
            await _viewModel.ImportInstallationAsync(path);
    }

    // ── Sign-in overlay ──────────────────────────────────────────────────────

    private async void SignIn_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
            await _viewModel.SignInAndPlayAsync();
    }

    private async void PlayOffline_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
            await _viewModel.PlayOfflineAsync();
    }

    private void CancelLogin_Click(object? sender, RoutedEventArgs e)
        => _viewModel?.CloseLoginOverlay();

    private async void CopyCode_Click(object? sender, RoutedEventArgs e)
    {
        // DeviceCode is a property on the ViewModel — copy it ourselves. Avalonia's
        // clipboard is per-TopLevel, not a static class like WPF's System.Windows.Clipboard.
        var code = _viewModel?.DeviceCode;
        if (string.IsNullOrEmpty(code)) return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
        {
            try { await clipboard.SetTextAsync(code); }
            catch { /* clipboard locked */ }
        }
    }

    // ── Launch overlay ───────────────────────────────────────────────────────

    private void FinishLaunch_Click(object? sender, RoutedEventArgs e)
        => _viewModel?.CloseLaunchOverlay();

    private async void CopyLaunchLog_Click(object? sender, RoutedEventArgs e)
    {
        var log = _viewModel?.LaunchMessage;
        if (string.IsNullOrEmpty(log)) return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
        {
            try { await clipboard.SetTextAsync(log); }
            catch { /* clipboard locked */ }
        }
    }

    // ── Share overlay handlers ────────────────────────────────────────────────

    private async void ShareInstallation_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        var card = (sender as Button)?.DataContext as InstallationCardViewModel;
        if (card?.Installation == null) return;

        var window = new ShareInstanceWindow(card.Installation, _viewModel);
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner != null)
            await window.ShowDialog(owner);
        else
            window.Show();
    }

    private void CloseShare_Click(object? sender, RoutedEventArgs e)
        => _viewModel?.CloseShareOverlay();

    private void ShareTabFriends_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel != null) _viewModel.ShareTab = "Friends";
    }

    private void ShareTabCode_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel != null) _viewModel.ShareTab = "Code";
    }

    private async void ShareWithFriends_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel != null) await _viewModel.ShareWithFriendsAsync();
    }

    private async void GenerateCode_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel != null) await _viewModel.GenerateShareCodeAsync();
    }

    private async void CopyShareCode_Click(object? sender, RoutedEventArgs e)
    {
        // Note: this handler is also wired to the device-code box's "Copy" button in the
        // sign-in overlay (same as the original XAML) — it only ever copies ShareCode.
        var text = _viewModel?.ShareCode;
        if (string.IsNullOrWhiteSpace(text)) return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
        {
            try { await clipboard.SetTextAsync(text); }
            catch { /* clipboard locked */ }
        }
    }

    private async void ImportFromCode_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel != null) await _viewModel.ImportFromCodeAsync();
    }

    private async void AcceptShareNotif_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        if ((sender as Button)?.Tag is InstanceShareNotification notif)
            await _viewModel.AcceptShareNotificationAsync(notif);
    }

    private async void DeclineShareNotif_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        if ((sender as Button)?.Tag is InstanceShareNotification notif)
            await _viewModel.DeclineShareNotificationAsync(notif);
    }

    private async void ImportWithCode_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;

        var owner = TopLevel.GetTopLevel(this) as Window;

        var popup = new Window
        {
            Title = "Import with code",
            Width = 420,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
            Background = this.TryFindResource("BrushAppBackground", out var bg) ? bg as Avalonia.Media.IBrush : null
        };

        var outer = new Border
        {
            Padding = new Thickness(28, 24, 28, 24),
            Background = this.TryFindResource("BrushAppBackground", out var bg2) ? bg2 as Avalonia.Media.IBrush : null
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Title
        var title = new TextBlock
        {
            Text = "Import with code",
            FontSize = 18,
            FontFamily = this.TryFindResource("FontDisplay", out var fontDisplay) ? (Avalonia.Media.FontFamily)fontDisplay! : Avalonia.Media.FontFamily.Default,
            Foreground = this.TryFindResource("BrushTextPrimary", out var fg) ? fg as Avalonia.Media.IBrush : null,
            Margin = new Thickness(0, 0, 0, 6)
        };
        Grid.SetRow(title, 0);

        // Subtitle
        var sub = new TextBlock
        {
            Text = "Paste a share code from someone else to import their instance.",
            FontSize = 12,
            FontFamily = this.TryFindResource("FontBody", out var fontBody) ? (Avalonia.Media.FontFamily)fontBody! : Avalonia.Media.FontFamily.Default,
            Foreground = this.TryFindResource("BrushTextSecondary", out var fg2) ? fg2 as Avalonia.Media.IBrush : null,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        };
        Grid.SetRow(sub, 1);

        // Code box
        var codeBox = new TextBox
        {
            FontSize = 20,
            FontFamily = new Avalonia.Media.FontFamily("Consolas"),
            Padding = new Thickness(14, 10, 14, 10),
            Background = this.TryFindResource("BrushPanelElevated", out var panelBg) ? panelBg as Avalonia.Media.IBrush : null,
            Foreground = this.TryFindResource("BrushTextPrimary", out var fg3) ? fg3 as Avalonia.Media.IBrush : null,
            BorderBrush = this.TryFindResource("BrushBorder", out var border) ? border as Avalonia.Media.IBrush : null,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 16),
            MaxLength = 8
        };
        Grid.SetRow(codeBox, 2);

        // Buttons
        var btnRow = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right
        };
        Grid.SetRow(btnRow, 3);

        var cancelBtn = new Button
        {
            Content = "Cancel",
            Padding = new Thickness(18, 8, 18, 8),
            Margin = new Thickness(0, 0, 8, 0),
            Theme = this.TryFindResource("SecondaryButtonStyle", out var secondaryStyle) ? secondaryStyle as ControlTheme : null
        };
        cancelBtn.Click += (_, _) => popup.Close();

        var importBtn = new Button
        {
            Content = "Import",
            Padding = new Thickness(18, 8, 18, 8),
            Theme = this.TryFindResource("PrimaryButtonStyle", out var primaryStyle) ? primaryStyle as ControlTheme : null
        };
        importBtn.Click += async (_, _) =>
        {
            var code = codeBox.Text?.Trim().ToUpperInvariant() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(code)) return;
            importBtn.IsEnabled = false;
            cancelBtn.IsEnabled = false;
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
        outer.Child = grid;
        popup.Content = outer;

        popup.Opened += (_, _) => codeBox.Focus();

        if (owner != null)
            await popup.ShowDialog(owner);
        else
            popup.Show();
    }
}
