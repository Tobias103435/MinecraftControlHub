using System.Collections.ObjectModel;
using MinecraftControlHub.Core.Models;

namespace MinecraftControlHub.Core.Services;

/// <summary>
/// Singleton that polls the Nexora backend for new tunnel share notifications
/// and exposes them to the UI.  Polling interval: 30 seconds when logged in.
/// </summary>
public interface ITunnelNotificationManager
{
    /// <summary>All notifications for the current session, newest first.</summary>
    ObservableCollection<TunnelNotification> Notifications { get; }

    /// <summary>Number of unread notifications.</summary>
    int UnreadCount { get; }

    /// <summary>Raised when UnreadCount or Notifications changes.</summary>
    event EventHandler? Changed;

    /// <summary>Start polling (call after Nexora login).</summary>
    void StartPolling();

    /// <summary>Stop polling and clear state (call after logout).</summary>
    void StopAndClear();

    /// <summary>Force an immediate poll.</summary>
    Task RefreshAsync();

    /// <summary>Mark a single notification as read locally and on the server.</summary>
    Task MarkReadAsync(TunnelNotification notification);

    /// <summary>Mark all notifications as read.</summary>
    Task MarkAllReadAsync();
}

public class TunnelNotificationManager : ITunnelNotificationManager
{
    private readonly ITunnelShareService   _shareService;
    private readonly INexoraAccountService _nexoraService;
    private CancellationTokenSource?       _cts;
    private System.Windows.Threading.Dispatcher? _dispatcher;

    public ObservableCollection<TunnelNotification> Notifications { get; } = new();
    public int UnreadCount => Notifications.Count(n => !n.IsRead);
    public event EventHandler? Changed;

    public TunnelNotificationManager(
        ITunnelShareService   shareService,
        INexoraAccountService nexoraService)
    {
        _shareService  = shareService;
        _nexoraService = nexoraService;
        // Capture the UI dispatcher at construction time (DI runs on UI thread)
        _dispatcher = System.Windows.Application.Current?.Dispatcher;
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

        // Newest first
        var sorted = result.Data.OrderByDescending(n => n.CreatedAt).ToList();

        RunOnUi(() =>
        {
            // Merge: update existing, add new
            var existing = Notifications.ToDictionary(n => n.Id);
            var incomingIds = new HashSet<int>(sorted.Select(n => n.Id));

            // Remove notifications no longer returned (unlikely but safe)
            foreach (var id in existing.Keys.Where(id => !incomingIds.Contains(id)).ToList())
            {
                var item = existing[id];
                Notifications.Remove(item);
            }

            foreach (var notif in sorted)
            {
                if (existing.TryGetValue(notif.Id, out var cur))
                {
                    // Update read status in-place if changed
                    if (cur.IsRead != notif.IsRead)
                    {
                        cur.IsRead = notif.IsRead;
                    }
                }
                else
                {
                    // Insert at the correct position (newest first)
                    int insertAt = 0;
                    for (int i = 0; i < Notifications.Count; i++)
                    {
                        if (Notifications[i].CreatedAt <= notif.CreatedAt)
                        { insertAt = i; break; }
                        insertAt = i + 1;
                    }
                    Notifications.Insert(insertAt, notif);
                }
            }

            Changed?.Invoke(this, EventArgs.Empty);
        });
    }

    public async Task MarkReadAsync(TunnelNotification notification)
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

    // ── Poll loop ─────────────────────────────────────────────────────────────

    private async Task PollLoopAsync(CancellationToken ct)
    {
        // Initial fetch immediately
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
