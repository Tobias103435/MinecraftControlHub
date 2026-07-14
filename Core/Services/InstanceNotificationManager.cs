using System.Collections.ObjectModel;
using MinecraftControlHub.Core.Models;

namespace MinecraftControlHub.Core.Services;

/// <summary>
/// Singleton that polls for incoming instance-share notifications (30 s interval).
/// Mirrors TunnelNotificationManager but for instance shares.
/// </summary>
public interface IInstanceNotificationManager
{
    ObservableCollection<InstanceShareNotification> Notifications { get; }
    int  UnreadCount { get; }
    event EventHandler? Changed;

    void StartPolling();
    void StopAndClear();
    Task RefreshAsync();
    Task MarkReadAsync(InstanceShareNotification notification);
    Task MarkAllReadAsync();
}

public class InstanceNotificationManager : IInstanceNotificationManager
{
    private readonly IInstanceShareService _shareService;
    private readonly INexoraAccountService _nexoraService;
    private CancellationTokenSource?       _cts;
    private readonly System.Windows.Threading.Dispatcher? _dispatcher;

    public ObservableCollection<InstanceShareNotification> Notifications { get; } = new();
    public int UnreadCount => Notifications.Count(n => !n.IsRead);
    public event EventHandler? Changed;

    public InstanceNotificationManager(
        IInstanceShareService shareService,
        INexoraAccountService nexoraService)
    {
        _shareService  = shareService;
        _nexoraService = nexoraService;
        _dispatcher    = System.Windows.Application.Current?.Dispatcher;
    }

    public void StartPolling()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _ = PollLoopAsync(_cts.Token);
    }

    public void StopAndClear()
    {
        _cts?.Cancel();
        _cts = null;
        RunOnUi(() =>
        {
            Notifications.Clear();
            Changed?.Invoke(this, EventArgs.Empty);
        });
    }

    public async Task RefreshAsync()
    {
        var token = _nexoraService.Current?.Token;
        if (string.IsNullOrEmpty(token)) return;

        var result = await _shareService.GetNotificationsAsync(token);
        if (!result.Success || result.Data == null) return;

        var sorted = result.Data.OrderByDescending(n => n.CreatedAt).ToList();

        RunOnUi(() =>
        {
            var existing    = Notifications.ToDictionary(n => n.Id);
            var incomingIds = new HashSet<int>(sorted.Select(n => n.Id));

            foreach (var id in existing.Keys.Where(id => !incomingIds.Contains(id)).ToList())
                Notifications.Remove(existing[id]);

            foreach (var notif in sorted)
            {
                if (existing.TryGetValue(notif.Id, out var cur))
                {
                    if (cur.IsRead != notif.IsRead)
                        cur.IsRead = notif.IsRead;
                }
                else
                {
                    int insertAt = Notifications.Count;
                    for (int i = 0; i < Notifications.Count; i++)
                    {
                        if (Notifications[i].CreatedAt <= notif.CreatedAt)
                        { insertAt = i; break; }
                    }
                    Notifications.Insert(insertAt, notif);
                }
            }

            Changed?.Invoke(this, EventArgs.Empty);
        });
    }

    public async Task MarkReadAsync(InstanceShareNotification notification)
    {
        if (notification.IsRead) return;
        notification.IsRead = true;
        var token = _nexoraService.Current?.Token;
        if (!string.IsNullOrEmpty(token))
            await _shareService.MarkReadAsync(token, notification.Id);
        RunOnUi(() => Changed?.Invoke(this, EventArgs.Empty));
    }

    public async Task MarkAllReadAsync()
    {
        foreach (var n in Notifications) n.IsRead = true;
        var token = _nexoraService.Current?.Token;
        if (!string.IsNullOrEmpty(token))
            await _shareService.MarkAllReadAsync(token);
        RunOnUi(() => Changed?.Invoke(this, EventArgs.Empty));
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        await RefreshAsync();
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(30), ct); }
            catch (TaskCanceledException) { break; }
            if (!ct.IsCancellationRequested)
                await RefreshAsync();
        }
    }

    private void RunOnUi(Action action)
    {
        if (_dispatcher != null && !_dispatcher.CheckAccess())
            _dispatcher.Invoke(action);
        else
            action();
    }
}
