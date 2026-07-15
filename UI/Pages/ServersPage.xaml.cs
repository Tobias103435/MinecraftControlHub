using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using MinecraftControlHub.Core.Models;
using MinecraftControlHub.UI.ViewModels;
using MinecraftControlHub.UI.Windows;

namespace MinecraftControlHub.UI.Pages;

public partial class ServersPage : UserControl
{
    private ServersPageViewModel? _viewModel;

    public ServersPage()
    {
        InitializeComponent();
        var serviceProvider = (Application.Current as App)?.ServiceProvider;
        if (serviceProvider != null)
        {
            _viewModel = serviceProvider.GetRequiredService<ServersPageViewModel>();
            DataContext = _viewModel;
        }
    }

    private void NewServer_Click(object sender, RoutedEventArgs e)
    {
        _viewModel?.StartWizard();
    }

    private void CancelWizard_Click(object sender, RoutedEventArgs e)
    {
        _viewModel?.CancelWizard();
    }

    private void BackWizard_Click(object sender, RoutedEventArgs e)
    {
        _viewModel?.PreviousWizardStep();
    }

    private void NextWizard_Click(object sender, RoutedEventArgs e)
    {
        _viewModel?.NextWizardStep();
    }

    private async void CreateServer_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
        {
            await _viewModel.CreateServerAsync();
        }
    }

    private async void StartServer_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null && (sender as Button)?.DataContext is Server server)
        {
            await _viewModel.StartServerAsync(server);
        }
    }

    private async void StopServer_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null && (sender as Button)?.DataContext is Server server)
        {
            await _viewModel.StopServerAsync(server);
        }
    }

    private async void DeleteServer_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null && (sender as Button)?.DataContext is Server server)
        {
            var confirm = MessageBox.Show(
                $"Delete \"{server.Name}\"?\n\nThis will remove the server and its associated files. This cannot be undone.",
                "Delete server",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm == MessageBoxResult.Yes)
                await _viewModel.DeleteServerAsync(server);
        }
    }


    private async void RestartServer_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null && (sender as System.Windows.Controls.Button)?.DataContext is MinecraftControlHub.Core.Models.Server server)
        {
            await _viewModel.StopServerAsync(server);
            await System.Threading.Tasks.Task.Delay(1500);
            await _viewModel.StartServerAsync(server);
        }
    }

    private void PreviewServer_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is Server server)
        {
            var window = new ServerPreviewWindow(server)
            {
                Owner = Window.GetWindow(this)
            };
            window.Show();
        }
    }

    private void ServerSettings_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is Server server)
        {
            var window = new ServerSettingsWindow(server)
            {
                Owner = Window.GetWindow(this)
            };
            window.ShowDialog();
        }
    }
}
