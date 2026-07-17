using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using MinecraftControlHub.Core.Services;
using MinecraftControlHub.UI.Windows;

namespace MinecraftControlHub.UI.Components;

public partial class Sidebar : UserControl
{
    public static readonly StyledProperty<bool> IsNexoraLoggedInProperty =
        AvaloniaProperty.Register<Sidebar, bool>(nameof(IsNexoraLoggedIn));

    public bool IsNexoraLoggedIn
    {
        get => GetValue(IsNexoraLoggedInProperty);
        set => SetValue(IsNexoraLoggedInProperty, value);
    }

    public event EventHandler<AppPage>? PageSelected;

    static Sidebar()
    {
        IsNexoraLoggedInProperty.Changed.AddClassHandler<Sidebar>((sidebar, e) => sidebar.OnLoginStateChanged(e));
    }

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
        Dispatcher.UIThread.Post(() =>
        {
            ServerNotifBadge.Tag       = tooltip;
            ServerNotifBadge.IsVisible = true;
        });
    }

    // Clear the badge when the user navigates to the Servers page
    public void ClearServerBadge()
    {
        ServerNotifBadge.IsVisible = false;
        ServerNotifBadge.Tag       = null;
    }

    private void OnLoginStateChanged(AvaloniaPropertyChangedEventArgs e)
    {
        if (e.GetNewValue<bool>())
            RefreshNexoraProfile();
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

    private void NavItem_Checked(object? sender, RoutedEventArgs e)
    {
        if (e.Source is RadioButton { IsChecked: true, Tag: string tag } &&
            Enum.TryParse<AppPage>(tag, out var page))
        {
            if (page == AppPage.Servers)
                ClearServerBadge();

            PageSelected?.Invoke(this, page);
        }
    }

    private async void SidebarLogin_Click(object? sender, RoutedEventArgs e)
    {
        var window = TopLevel.GetTopLevel(this) as Window;
        var loginWindow = new NexoraLoginWindow();

        if (window != null)
            await loginWindow.ShowDialog(window);
        else
            loginWindow.Show();

        if (loginWindow.LoginSuccessful)
        {
            IsNexoraLoggedIn = true;
            RefreshNexoraProfile();
            // Notify MainWindow so it can also update its state
            (window as MainWindow)?.OnNexoraLoginSuccessful();
        }
    }
}
