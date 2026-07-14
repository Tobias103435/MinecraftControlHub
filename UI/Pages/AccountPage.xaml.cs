using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
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

    private async void SignIn_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
            await _viewModel.SignInAsync();
    }

    private void CopyCode_Click(object sender, RoutedEventArgs e)
    {
        _viewModel?.CopyDeviceCode();
    }

    private void SignOut_Click(object sender, RoutedEventArgs e)
    {
        _viewModel?.SignOut();
    }


    private async void ApplyUrlSkin_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
            await _viewModel.ApplyUrlSkinAsync();
    }

    private async void UploadSkin_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null)
            return;

        var dialog = new OpenFileDialog
        {
            Filter = "PNG images|*.png",
            CheckFileExists = true,
            Multiselect = false,
            Title = "Choose a skin PNG"
        };

        if (dialog.ShowDialog() == true)
            await _viewModel.ChangeSkinFromFileAsync(dialog.FileName);
    }

    private async void UseCape_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null && (sender as Button)?.DataContext is AppearanceItem cape)
        {
            await _viewModel.UseCapeAsync(cape);
        }
    }

    private async void HideCape_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
            await _viewModel.HideCapeAsync();
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