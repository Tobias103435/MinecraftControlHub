using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using MinecraftControlHub.Core.Services;
using MinecraftControlHub.UI;
using MinecraftControlHub.UI.Pages;
using MinecraftControlHub.UI.Windows;

namespace MinecraftControlHub;

public partial class MainWindow : Window
{
    private readonly INexoraAccountService       _nexoraService;
    private readonly ITunnelNotificationManager  _notifManager;
    private IInstanceNotificationManager?        _instanceNotifManager;

    // Page cache — keeps the same instance alive so state (e.g. launch progress) survives navigation
    private readonly Dictionary<AppPage, System.Windows.Controls.UserControl> _pageCache = new();

    public MainWindow()
    {
        InitializeComponent();

        var app = (App)Application.Current;
        _nexoraService        = app.ServiceProvider?.GetService<INexoraAccountService>()!;
        _notifManager         = app.ServiceProvider?.GetService<ITunnelNotificationManager>()!;
        _instanceNotifManager = app.ServiceProvider?.GetService<IInstanceNotificationManager>();

        SidebarControl.PageSelected += (_, page) => ShowPage(page);

        Loaded += async (s, e) => await CheckNexoraLoginAsync();

        ShowPage(AppPage.Home);
    }

    private async Task CheckNexoraLoginAsync()
    {
        bool loggedIn = false;
        bool continuedWithoutAccount = false;

        if (_nexoraService.Current == null)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                var loginWindow = new NexoraLoginWindow { Owner = this };
                loginWindow.ShowDialog();
                loggedIn = loginWindow.LoginSuccessful;
                continuedWithoutAccount = loginWindow.ContinuedWithoutAccount;
            });
        }
        else
        {
            var validAccount = await _nexoraService.ValidateStoredTokenAsync();
            if (validAccount == null)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    var loginWindow = new NexoraLoginWindow { Owner = this };
                    loginWindow.ShowDialog();
                    loggedIn = loginWindow.LoginSuccessful;
                    continuedWithoutAccount = loginWindow.ContinuedWithoutAccount;
                });
            }
            else
            {
                loggedIn = true;
            }
        }

        await Dispatcher.InvokeAsync(() =>
        {
            SidebarControl.IsNexoraLoggedIn = loggedIn;
            if (loggedIn)
            {
                SidebarControl.RefreshNexoraProfile();
                _notifManager?.StartPolling();
                _instanceNotifManager?.StartPolling();
            }
        });

        if (!loggedIn && !continuedWithoutAccount)
        {
            await Dispatcher.InvokeAsync(() => Application.Current.Shutdown());
        }
    }

    /// <summary>Called by Sidebar or FriendsPage overlay after a successful login.</summary>
    public void OnNexoraLoginSuccessful()
    {
        SidebarControl.IsNexoraLoggedIn = true;
        SidebarControl.RefreshNexoraProfile();
        _notifManager?.StartPolling();
        _instanceNotifManager?.StartPolling();
    }

    /// <summary>Called by SettingsPage after the user logs out of Nexora.</summary>
    public void OnNexoraLogout()
    {
        SidebarControl.IsNexoraLoggedIn = false;
        _notifManager?.StopAndClear();
    }

    internal void ShowPage(AppPage page)
    {
        if (!_pageCache.TryGetValue(page, out var pageInstance))
        {
            pageInstance = page switch
            {
                AppPage.Home     => new HomePage(),
                AppPage.Account  => new AccountPage(),
                AppPage.Servers  => new ServersPage(),
                AppPage.Mods     => new ModsPage(),
                AppPage.Friends  => new FriendsPage(),
                AppPage.Tunnel   => CreateTunnelPage(),
                AppPage.Ai       => new AiPage(),
                AppPage.Settings => new SettingsPage(),
                _                => new HomePage()
            };
            _pageCache[page] = pageInstance;
        }

        MainContent.Content = pageInstance;
    }


    private TunnelPage CreateTunnelPage()
    {
        var page = new TunnelPage();
        // Wire the "Open Settings" button in TunnelPage to actually navigate to Settings
        if (page.DataContext is MinecraftControlHub.UI.ViewModels.TunnelPageViewModel vm)
        {
            vm.NavigateToSettings += (_, _) => ShowPage(AppPage.Settings);
        }
        return page;
    }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void CloseButton_Click(object sender, RoutedEventArgs e)
        => Close();

    private void MainWindow_StateChanged(object? sender, EventArgs e)
        => RootBorder.Margin = WindowState == WindowState.Maximized
            ? new Thickness(7)
            : new Thickness(0);
}
