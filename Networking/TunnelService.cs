using System.Collections.ObjectModel;
using MinecraftControlHub.Core.Models;

namespace MinecraftControlHub.Networking;

/// <summary>
/// Manages the lifecycle of all active tunnel sessions.
/// Each assignment gets at most one session at a time.
/// Call <see cref="StartAllAsync"/> / <see cref="StopAll"/> from the ViewModel.
/// </summary>
public interface ITunnelService
{
    /// <summary>All currently tracked sessions (one per active assignment).</summary>
    IReadOnlyList<TunnelSession> Sessions { get; }

    /// <summary>Fired whenever any session's state, address, or log changes.</summary>
    event EventHandler<TunnelSessionChangedEventArgs>? SessionChanged;

    /// <summary>Starts a tunnel for every enabled assignment that is not already running.</summary>
    Task StartAllAsync(IEnumerable<PortTunnelAssignment> assignments,
                       Func<TunnelProvider, string?> getExePath,
                       Func<TunnelProvider, string?> getApiKey,
                       CancellationToken ct = default);

    /// <summary>Starts or restarts a single assignment.</summary>
    Task StartOneAsync(PortTunnelAssignment assignment,
                       string  exePath,
                       string? apiKey,
                       CancellationToken ct = default);

    /// <summary>Stops and disposes a single session by port number.</summary>
    void StopOne(int port);

    /// <summary>Provides a manual address to an active session (for Manual address-source providers).</summary>
    void ProvideManualAddress(int port, string address);

    /// <summary>Stops all running sessions.</summary>
    void StopAll();
}

public class TunnelService : ITunnelService
{
    private readonly List<TunnelSession> _sessions = new();
    private readonly object              _lock     = new();

    public IReadOnlyList<TunnelSession> Sessions
    {
        get { lock (_lock) { return _sessions.ToList(); } }
    }

    public event EventHandler<TunnelSessionChangedEventArgs>? SessionChanged;

    // -----------------------------------------------------------------------
    // Start all
    // -----------------------------------------------------------------------

    public async Task StartAllAsync(
        IEnumerable<PortTunnelAssignment> assignments,
        Func<TunnelProvider, string?> getExePath,
        Func<TunnelProvider, string?> getApiKey,
        CancellationToken ct = default)
    {
        var tasks = assignments
            .Select(a =>
            {
                var exePath = getExePath(a.SelectedProvider!);
                var apiKey  = getApiKey(a.SelectedProvider!);
                return StartOneAsync(a, exePath ?? string.Empty, apiKey, ct);
            });

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------------
    // Start one
    // -----------------------------------------------------------------------

    public async Task StartOneAsync(
        PortTunnelAssignment assignment,
        string  exePath,
        string? apiKey,
        CancellationToken ct = default)
    {
        // Stop any existing session for this port first
        StopOne(assignment.Port.Port);

        var session = new TunnelSession(assignment);
        session.Changed += (s, e) => SessionChanged?.Invoke(s, e);

        lock (_lock) { _sessions.Add(session); }
        SessionChanged?.Invoke(this, new TunnelSessionChangedEventArgs(session));

        await session.StartAsync(exePath, apiKey, ct).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------------
    // Provide manual address (for providers using TunnelAddressSource.Manual)
    // -----------------------------------------------------------------------

    public void ProvideManualAddress(int port, string address)
    {
        TunnelSession? session;
        lock (_lock) { session = _sessions.FirstOrDefault(s => s.Assignment.Port.Port == port); }
        session?.ProvideManualAddress(address);
    }

    // -----------------------------------------------------------------------
    // Stop one
    // -----------------------------------------------------------------------

    public void StopOne(int port)
    {
        List<TunnelSession> toRemove;
        lock (_lock)
        {
            toRemove = _sessions
                .Where(s => s.Assignment.Port.Port == port)
                .ToList();
            foreach (var s in toRemove) _sessions.Remove(s);
        }

        foreach (var s in toRemove)
        {
            s.Stop();
            s.Dispose();
        }
    }

    // -----------------------------------------------------------------------
    // Stop all
    // -----------------------------------------------------------------------

    public void StopAll()
    {
        List<TunnelSession> all;
        lock (_lock)
        {
            all = _sessions.ToList();
            _sessions.Clear();
        }

        foreach (var s in all)
        {
            s.Stop();
            s.Dispose();
        }
    }
}
