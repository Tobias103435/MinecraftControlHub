using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using MinecraftControlHub.Core.Services;
using MinecraftControlHub.UI.ViewModels;

namespace MinecraftControlHub.UI.Pages;

public partial class AccountPage : UserControl
{
    private readonly AccountPageViewModel? _viewModel;

    public AccountPage()
    {
        InitializeComponent();
        var serviceProvider = (Application.Current as App)?.ServiceProvider;
        if (serviceProvider != null)
        {
            _viewModel = serviceProvider.GetRequiredService<AccountPageViewModel>();
            DataContext = _viewModel;
        }
    }

    private async void SignIn_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
            await _viewModel.SignInAsync();
    }

    private async void CopyCode_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null || string.IsNullOrEmpty(_viewModel.DeviceCode))
            return;

        // Avalonia's clipboard is per-TopLevel, not a static class like WPF's
        // System.Windows.Clipboard, so the copy is done here in the view instead
        // of inside AccountPageViewModel.
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
            await clipboard.SetTextAsync(_viewModel.DeviceCode);
    }

    private void SignOut_Click(object? sender, RoutedEventArgs e)
    {
        _viewModel?.SignOut();
    }


    private async void ApplyUrlSkin_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
            await _viewModel.ApplyUrlSkinAsync();
    }

    private async void UploadSkin_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null)
            return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a skin PNG",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("PNG images") { Patterns = new[] { "*.png" } }
            }
        });

        var path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (!string.IsNullOrEmpty(path))
            await _viewModel.ChangeSkinFromFileAsync(path);
    }

    private async void UseCape_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel != null && (sender as Button)?.DataContext is AppearanceItem cape)
        {
            await _viewModel.UseCapeAsync(cape);
        }
    }

    private async void HideCape_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
            await _viewModel.HideCapeAsync();
    }

    // Avalonia has no Hyperlink inline control, so the "microsoft.com/link" text
    // in AccountPage.axaml is a plain, underlined TextBlock with its target URL
    // stashed in Tag; this handles the click instead of RequestNavigateEventArgs.
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
}
