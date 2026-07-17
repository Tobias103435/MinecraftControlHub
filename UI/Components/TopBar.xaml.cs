using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using MinecraftControlHub.Core.Models;
using MinecraftControlHub.Core.Services;
using MinecraftControlHub.UI.ViewModels;

namespace MinecraftControlHub.UI.Components;

/// <summary>
/// Unified notification row shown in the TopBar flyout.
/// Wraps either a TunnelNotification or an InstanceShareNotification.
/// </summary>
public class NotificationRow : INotifyPropertyChanged
{
    public enum RowKind { Tunnel, Instance }

    public RowKind Kind { get; }

    // Tunnel fields
    public TunnelNotification? Tunnel { get; }

    // Instance fields
    public InstanceShareNotification? Instance { get; }

    // Common display
    public string SenderUsername  => Kind == RowKind.Tunnel
        ? Tunnel!.SenderUsername : Instance!.SenderUsername;
    public string SenderInitial   => string.IsNullOrEmpty(SenderUsername) ? "?" : SenderUsername[0].ToString().ToUpper();
    public bool   IsRead          => Kind == RowKind.Tunnel ? Tunnel!.IsRead : Instance!.IsRead;
    public string TimeLabel       { get; }
    public bool   IsTunnel        => Kind == RowKind.Tunnel;
    public bool   IsInstance      => Kind == RowKind.Instance;

    public NotificationRow(TunnelNotification t)
    {
        Kind      = RowKind.Tunnel;
        Tunnel    = t;
        TimeLabel = FormatTime(t.CreatedAt);
    }

    public NotificationRow(InstanceShareNotification n)
    {
        Kind     = RowKind.Instance;
        Instance = n;
        n.PropertyChanged += (_, _) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRead)));
        TimeLabel = FormatTime(n.CreatedAt);
    }

    private static string FormatTime(DateTime dt)
    {
        var diff = DateTime.Now - dt;
        if (diff.TotalMinutes < 1)  return "just now";
        if (diff.TotalHours   < 1)  return $"{(int)diff.TotalMinutes}m ago";
        if (diff.TotalDays    < 1)  return $"{(int)diff.TotalHours}h ago";
        return $"{(int)diff.TotalDays}d ago";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class TopBar : UserControl
{
    private ITunnelNotificationManager?   _tunnelNotifManager;
    private IInstanceNotificationManager? _instanceNotifManager;

    private readonly ObservableCollection<NotificationRow> _rows = new();

    public TopBar()
    {
        InitializeComponent();
        Loaded += TopBar_Loaded;
    }

    private void TopBar_Loaded(object? sender, RoutedEventArgs e)
    {
        var app = Application.Current as App;
        _tunnelNotifManager   = app?.ServiceProvider?.GetService<ITunnelNotificationManager>();
        _instanceNotifManager = app?.ServiceProvider?.GetService<IInstanceNotificationManager>();

        if (_tunnelNotifManager != null)
            _tunnelNotifManager.Changed   += (_, _) => Dispatcher.UIThread.Post(Refresh);
        if (_instanceNotifManager != null)
            _instanceNotifManager.Changed += (_, _) => Dispatcher.UIThread.Post(Refresh);

        Refresh();
    }

    // ── Rebuild the merged list ───────────────────────────────────────────────

    private void Refresh()
    {
        _rows.Clear();

        // Tunnel notifications
        if (_tunnelNotifManager != null)
            foreach (var t in _tunnelNotifManager.Notifications)
                _rows.Add(new NotificationRow(t));

        // Instance notifications
        if (_instanceNotifManager != null)
            foreach (var n in _instanceNotifManager.Notifications)
                _rows.Add(new NotificationRow(n));

        // Sort newest first
        var sorted = _rows.OrderByDescending(r =>
            r.Kind == NotificationRow.RowKind.Tunnel
                ? r.Tunnel!.CreatedAt
                : r.Instance!.CreatedAt).ToList();

        _rows.Clear();
        foreach (var r in sorted) _rows.Add(r);

        NotificationList.ItemsSource = _rows;
        EmptyNotifText.IsVisible     = _rows.Count == 0;

        // Badge
        var unread = (_tunnelNotifManager?.UnreadCount ?? 0)
                   + (_instanceNotifManager?.UnreadCount ?? 0);
        if (unread > 0)
        {
            BadgeBorder.IsVisible = true;
            BadgeText.Text        = unread > 99 ? "99+" : unread.ToString();
        }
        else
        {
            BadgeBorder.IsVisible = false;
        }
    }

    // ── Bell toggle ───────────────────────────────────────────────────────────

    private void BellButton_Click(object? sender, RoutedEventArgs e)
        => NotificationPopup.IsOpen = !NotificationPopup.IsOpen;

    // ── Notification row clicked ──────────────────────────────────────────────

    private void NotifRow_Click(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left) return;

        if (sender is Control { Tag: NotificationRow row })
        {
            if (row.IsTunnel && _tunnelNotifManager != null)
                _ = _tunnelNotifManager.MarkReadAsync(row.Tunnel!);
            else if (row.IsInstance && _instanceNotifManager != null)
                _ = _instanceNotifManager.MarkReadAsync(row.Instance!);
        }
    }

    // ── Mark all read ─────────────────────────────────────────────────────────

    private void MarkAllRead_Click(object? sender, RoutedEventArgs e)
    {
        if (_tunnelNotifManager != null)   _ = _tunnelNotifManager.MarkAllReadAsync();
        if (_instanceNotifManager != null) _ = _instanceNotifManager.MarkAllReadAsync();
    }

    // ── Instance share: Install / Decline ────────────────────────────────────

    private async void InstallInstance_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: NotificationRow { IsInstance: true } row }) return;

        NotificationPopup.IsOpen = false;

        // Navigate to Home page and trigger install via HomePageViewModel
        var mainWindow = TopLevel.GetTopLevel(this) as MainWindow;
        mainWindow?.ShowPage(MinecraftControlHub.UI.AppPage.Home);

        var homeVm = (Application.Current as App)?.ServiceProvider
            ?.GetService<HomePageViewModel>();
        if (homeVm != null && row.Instance != null)
            await homeVm.AcceptShareNotificationAsync(row.Instance);
    }

    private async void DeclineInstance_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: NotificationRow { IsInstance: true } row }) return;
        if (_instanceNotifManager != null && row.Instance != null)
            await _instanceNotifManager.MarkReadAsync(row.Instance);
    }

    // ── Copy buttons (tunnel notifications) ──────────────────────────────────

    private void CopyIp_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string text }) TryCopy(text);
    }

    private void CopyPort_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag != null) TryCopy(btn.Tag.ToString()!);
    }

    private void CopyIpPort_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string text }) TryCopy(text);
    }

    private void TryCopy(string text)
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            _ = clipboard?.SetTextAsync(text);
        }
        catch { }
    }
}
