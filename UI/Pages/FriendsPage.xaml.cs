using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using MinecraftControlHub.UI.ViewModels;
using MinecraftControlHub.UI.Windows;

namespace MinecraftControlHub.UI.Pages;

public partial class FriendsPage : UserControl
{
    private readonly FriendsPageViewModel? _viewModel;

    public FriendsPage()
    {
        InitializeComponent();
        var serviceProvider = (Application.Current as App)?.ServiceProvider;
        if (serviceProvider != null)
        {
            _viewModel = serviceProvider.GetRequiredService<FriendsPageViewModel>();
            DataContext = _viewModel;
        }
    }

    private async void OverlayLogin_Click(object? sender, RoutedEventArgs e)
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
            _viewModel?.NotifyLoginStateChanged();
        }
    }
}
