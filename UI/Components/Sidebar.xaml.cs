using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using MinecraftControlHub.Core.Services;
using MinecraftControlHub.UI.Windows;

namespace MinecraftControlHub.UI.Components;

public partial class Sidebar : UserControl
{
    public static readonly DependencyProperty IsNexoraLoggedInProperty =
        DependencyProperty.Register(nameof(IsNexoraLoggedIn), typeof(bool), typeof(Sidebar),
            new PropertyMetadata(false, OnLoginStateChanged));

    public bool IsNexoraLoggedIn
    {
        get => (bool)GetValue(IsNexoraLoggedInProperty);
        set => SetValue(IsNexoraLoggedInProperty, value);
    }

    public event EventHandler<AppPage>? PageSelected;

    public Sidebar()
    {
        InitializeComponent();

        // Subscribe to server events to show the notification badge
        var app = Application.Current as App;
        var serverService = app?.ServiceProvider?.GetService<IServerService>();
        if (serverService != null)
        {
            serverService.ServerOutputReceived += OnServerOutput;
            serverService.ServerCrashed        += OnServerCrashed;
        }
    }

    private void OnServerOutput(object? sender, ServerConsoleOutputEventArgs e)
    {
        // Detect player join/leave from log output
        if (!e.Line.Contains("joined the game", StringComparison.OrdinalIgnoreCase) &&
            !e.Line.Contains("left the game", StringComparison.OrdinalIgnoreCase))
            return;

        var msg = e.Line.Contains("joined") ? "A player joined a server" : "A player left a server";
        ShowServerBadge(msg);
    }

    private void OnServerCrashed(object? sender, ServerCrashedEventArgs e)
        => ShowServerBadge($"Server '{e.Server.Name}' crashed!");

    private void ShowServerBadge(string tooltip)
    {
        Dispatcher.InvokeAsync(() =>
        {
            ServerNotifBadge.Tag        = tooltip;
            ServerNotifBadge.Visibility = Visibility.Visible;
        });
    }

    // Clear the badge when the user navigates to the Servers page
    public void ClearServerBadge()
    {
        ServerNotifBadge.Visibility = Visibility.Collapsed;
        ServerNotifBadge.Tag        = null;
    }

    private static void OnLoginStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Sidebar sidebar) return;
        if ((bool)e.NewValue)
            sidebar.RefreshNexoraProfile();
    }

    /// <summary>Updates the avatar initial + username in the bottom strip.</summary>
    public void RefreshNexoraProfile()
    {
        var app = Application.Current as App;
        var nexora = app?.ServiceProvider?.GetService<INexoraAccountService>();
        if (nexora?.Current == null) return;

        var username = nexora.Current.Username;
        SidebarUsername.Text = username;
        SidebarAvatarInitial.Text = string.IsNullOrEmpty(username)
            ? "?"
            : username[0].ToString().ToUpper();
    }

    private void NavItem_Checked(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is RadioButton { Tag: string tag } &&
            Enum.TryParse<AppPage>(tag, out var page))
        {
            if (page == AppPage.Servers)
                ClearServerBadge();

            PageSelected?.Invoke(this, page);
        }
    }

    private void SidebarLogin_Click(object sender, RoutedEventArgs e)
    {
        var window = Window.GetWindow(this);
        var loginWindow = new NexoraLoginWindow { Owner = window };
        loginWindow.ShowDialog();

        if (loginWindow.LoginSuccessful)
        {
            IsNexoraLoggedIn = true;
            RefreshNexoraProfile();
            // Notify MainWindow so it can also update its state
            (window as MainWindow)?.OnNexoraLoginSuccessful();
        }
    }
}
