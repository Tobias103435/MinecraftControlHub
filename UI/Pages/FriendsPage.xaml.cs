using System.Windows;
using System.Windows.Controls;
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

    private void OverlayLogin_Click(object sender, RoutedEventArgs e)
    {
        var window = Window.GetWindow(this);
        var loginWindow = new NexoraLoginWindow { Owner = window };
        loginWindow.ShowDialog();

        if (loginWindow.LoginSuccessful)
        {
            (window as MainWindow)?.OnNexoraLoginSuccessful();
            _viewModel?.NotifyLoginStateChanged();
        }
    }
}
