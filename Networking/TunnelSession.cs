using System.Diagnostics;
using System.IO;
using MinecraftControlHub.Core.Models;

namespace MinecraftControlHub.Networking;

/// <summary>
/// Represents one running tunnel process for a single port/provider pair.
/// Thread-safe: state changes are dispatched back to the UI thread via the
/// event, but the internal log list is guarded by a lock.
/// </summary>
public class TunnelSession : IDisposable
{
    private Process?                   _process;
    private System.Net.Http.HttpClient? _http;
    private CancellationTokenSource?   _pollCts;
    private bool                       _disposed;
    private readonly List<string>      _log = new();
    private readonly object            _logLock = new();
    private string?                    _apiKey;

    // -----------------------------------------------------------------------
    // Public state
    // -----------------------------------------------------------------------

    public PortTunnelAssignment Assignment { get; }
    public TunnelSessionState   State      { get; private set; } = TunnelSessionState.Idle;
    public string? PublicAddress           { get; private set; }
    /// <summary>
    /// Set when playit.gg prints a one-time claim URL on first run.
    /// The user must open this URL to link the agent to their account.
    /// </summary>
    public string? ClaimUrl                { get; private set; }
    public bool    HasClaimUrl             => !string.IsNullOrEmpty(ClaimUrl);
    public string? ErrorMessage            { get; private set; }

    public IReadOnlyList<string> Log
    {
        get { lock (_logLock) { return _log.ToList(); } }
    }

    /// <summary>Fired whenever state, address, or log changes.</summary>
    public event EventHandler<TunnelSessionChangedEventArgs>? Changed;

    // -----------------------------------------------------------------------
    // Constructor
    // -----------------------------------------------------------------------

    public TunnelSession(PortTunnelAssignment assignment)
    {
        Assignment = assignment;
    }

    // -----------------------------------------------------------------------
    // Start
    // -----------------------------------------------------------------------

    public async Task StartAsync(string exePath, string? apiKey, CancellationToken ct = default)
    {
        _apiKey = apiKey;

        if (State == TunnelSessionState.Running) return;

        SetState(TunnelSessionState.Starting);
        await Task.Yield(); // ensure caller is not blocked while we start the process
        PublicAddress = null;
        ErrorMessage  = null;
        lock (_logLock) { _log.Clear(); }

        var provider = Assignment.SelectedProvider;
        if (provider is null)
        {
            Fail("No provider selected.");
            return;
        }

        AppendLog($"Starting {provider.DisplayName} for port {Assignment.Port.Port}/{Assignment.Port.Protocol}...");

        // RemoteApi providers don't need a process - they use an existing daemon
        if (provider.AddressSource != TunnelAddressSource.RemoteApi &&
            provider.AddressSource != TunnelAddressSource.Manual)
        {
            var command = BuildCommand(provider, exePath, apiKey);
            if (command is null)
            {
                Fail("Could not build launch command — check the executable path in Settings.");
                return;
            }

            AppendLog($"CMD: {command}");

            try
            {
                _process = LaunchProcess(command);
                _process.OutputDataReceived += (_, e) => { if (e.Data is not null) HandleOutput(e.Data); };
                _process.ErrorDataReceived  += (_, e) => { if (e.Data is not null) HandleOutput(e.Data); };
                _process.Exited             += OnProcessExited;
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                Fail($"Failed to start process: {ex.Message}");
                return;
            }
        }
        else if (provider.AddressSource == TunnelAddressSource.Manual)
        {
            // Nothing to launch — a separately-running tool (e.g. playit.gg's
            // official Windows wizard, installed once as a background
            // service outside the launcher) already handles the tunnel.
            // Just wait here; ProvideManualAddress(...) below is called by
            // the UI once the user pastes in the address they can already
            // see in that tool.
            if (!string.IsNullOrEmpty(provider.Limitation))
                AppendLog(provider.Limitation);
            AppendLog("Vul hieronder je eigen IP-adres + poortnummer in (bijv. 123.456.78.90:25565).");
        }

        if (provider.AddressSource == TunnelAddressSource.LocalApi &&
            !string.IsNullOrEmpty(provider.LocalApiUrl) &&
            !string.IsNullOrEmpty(provider.LocalApiJsonPath))
        {
            _ = PollLocalApiAsync(provider.LocalApiUrl, provider.LocalApiJsonPath, ct);
        }

        if (provider.AddressSource == TunnelAddressSource.RemoteApi &&
            !string.IsNullOrEmpty(provider.RemoteApiUrl) &&
            !string.IsNullOrEmpty(provider.SecretKeyPath))
        {
            _ = ManageRemoteApiTunnelAsync(provider.RemoteApiUrl, provider.SecretKeyPath, ct);
        }
    }

    // -----------------------------------------------------------------------
    // Manual address entry (playit.gg run via its official standalone wizard)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Called by the UI when the user pastes in the public address shown by
    /// a separately-running tool (used for <see cref="TunnelAddressSource.Manual"/>).
    /// </summary>
    public void ProvideManualAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address)) return;
        SetPublicAddress(address.Trim());
    }

    // -----------------------------------------------------------------------
    // Stop
    // -----------------------------------------------------------------------

    public void Stop()
    {
        _pollCts?.Cancel();
        try { _process?.Kill(entireProcessTree: true); } catch { /* best effort */ }
        SetState(TunnelSessionState.Stopped);
        AppendLog("Tunnel stopped.");
    }

    // -----------------------------------------------------------------------
    // Internal helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Strips ANSI/VT100 escape sequences (e.g. colour codes) from a string.
    /// playit.gg is a TUI app that wraps every token in colour codes; without
    /// stripping them the address regex never matches.
    /// </summary>
    /// <summary>
    /// Strips ALL VT100/ANSI escape sequences from <paramref name="input"/>.
    /// The original pattern only covered a subset of CSI sequences (colors + a
    /// few cursor ops).  playit.gg v1.0+ is a full TUI that also emits
    /// ?-prefixed private sequences (ESC[?25l, ESC[?1049h, ESC[2J) and
    /// simple two-byte ESC sequences (ESC= ESC>).  Those leaked through and
    /// prevented the address regex from matching.
    /// </summary>
    /// <summary>
    /// Strips ALL VT100/ANSI escape sequences from <paramref name="input"/>.
    /// Covers CSI sequences (ESC [ params final) including private-mode ones
    /// like ESC[?25l and ESC[?1049h, plus simple two-byte ESC sequences.
    /// </summary>
    private static string StripAnsi(string input) =>
        System.Text.RegularExpressions.Regex.Replace(
            input,
            @"\x1b(?:\[[0-9;?]*[a-zA-Z]|[^\[])",
            string.Empty);

    private void HandleOutput(string rawLine)
    {
        AppendLog(rawLine);

        // TUI apps (like playit.gg) sometimes write multiple "virtual lines"
        // separated by bare carriage-returns (CR) rather than newlines.
        // Split on CR so we examine each sub-segment independently; this also
        // strips any trailing CR that BeginOutputReadLine leaves on the string.
        var segments = rawLine.Split('\r', StringSplitOptions.RemoveEmptyEntries);

        foreach (var segment in segments)
        {
            ProcessOutputLine(segment);
        }
    }

    private void ProcessOutputLine(string line)
    {
        // Strip ALL ANSI/VT100 escape sequences before any regex work.
        var cleanLine = StripAnsi(line);

        // playit --stdout prepends a timestamp + log-level prefix to every line:
        //   "2026-07-12T10:00:00.123456Z  INFO playit_agent_core::tunnel: connect address: ..."
        // Strip that prefix so the address patterns below can match normally.
        cleanLine = System.Text.RegularExpressions.Regex.Replace(
            cleanLine,
            @"^\d{4}-\d\d-\d\dT[\d:.]+Z\s+\w+\s+[\w:]+:\s*",
            string.Empty).Trim();

        // Debug: log the cleaned line to help diagnose regex issues
        if (!string.IsNullOrWhiteSpace(cleanLine))
            AppendLog($"[DEBUG] Cleaned: {cleanLine}");

        // ── playit.gg first-run: detect the claim URL and surface it ──────
        // On a fresh install playit prints a URL the user must open once to
        // link the agent to their account.  We keep state=Starting so the
        // UI shows "waiting" and we log an extra hint.
        var claimMatch = System.Text.RegularExpressions.Regex.Match(
            cleanLine, @"https://claim\.playit\.gg/[^\s]+",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (claimMatch.Success)
        {
            ClaimUrl = claimMatch.Value;
            AppendLog("──────────────────────────────────────────────────");
            AppendLog("ACTION REQUIRED — open this URL in your browser to");
            AppendLog("link playit.gg to your account (one-time setup):");
            AppendLog(claimMatch.Value);
            AppendLog("After you claim the agent it will start tunnelling.");
            AppendLog("──────────────────────────────────────────────────");
            // Raise Changed so the UI can show the claim URL prominently
            Changed?.Invoke(this, new TunnelSessionChangedEventArgs(this));
            return;
        }

        if (State != TunnelSessionState.Starting) return;
        var provider = Assignment.SelectedProvider;
        if (provider?.AddressSource != TunnelAddressSource.StdoutRegex) return;
        if (string.IsNullOrEmpty(provider.StdoutPattern)) return;

        var match = System.Text.RegularExpressions.Regex.Match(cleanLine, provider.StdoutPattern,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (!match.Success) return;

        var addrGroup = match.Groups["addr"];
        var portGroup = match.Groups["port"];

        string addr;
        if (addrGroup.Success && !string.IsNullOrEmpty(addrGroup.Value))
            addr = addrGroup.Value;
        else if (portGroup.Success && !string.IsNullOrEmpty(portGroup.Value))
            addr = portGroup.Value;
        else
            addr = match.Value;

        SetPublicAddress(addr);
    }

    private async Task PollLocalApiAsync(string url, string jsonPath, CancellationToken external)
    {
        _http    = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        _pollCts = CancellationTokenSource.CreateLinkedTokenSource(external);
        var ct   = _pollCts.Token;

        await Task.Delay(2000, ct).ConfigureAwait(false);

        for (int i = 0; i < 20 && !ct.IsCancellationRequested; i++)
        {
            try
            {
                var json = await _http.GetStringAsync(url, ct).ConfigureAwait(false);
                var addr = ExtractJsonPath(json, jsonPath);
                if (!string.IsNullOrEmpty(addr))
                {
                    // Strip protocol prefix (tcp:// etc.)
                    addr = System.Text.RegularExpressions.Regex.Replace(addr, @"^[a-z]+://", "");
                    SetPublicAddress(addr);
                    return;
                }
            }
            catch (OperationCanceledException) { return; }
            catch { /* not ready yet, try again */ }

            await Task.Delay(1500, ct).ConfigureAwait(false);
        }

        if (State == TunnelSessionState.Starting)
            AppendLog("Warning: could not read public address from local API — tunnel may still be active.");
    }

    private async Task ManageRemoteApiTunnelAsync(string apiUrl, string secretKeyPath, CancellationToken external)
    {
        _http    = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _pollCts = CancellationTokenSource.CreateLinkedTokenSource(external);
        var ct   = _pollCts.Token;

        try
        {
            // Get API key from settings (__session cookie)
            var apiKey = _apiKey;
            if (string.IsNullOrEmpty(apiKey))
            {
                Fail("No API key configured. Please add your playit.gg __session cookie in Settings → Tunnel providers.");
                AppendLog("To get your __session cookie:");
                AppendLog("1. Go to https://playit.gg and log in");
                AppendLog("2. Press F12 to open DevTools");
                AppendLog("3. Go to Application → Cookies");
                AppendLog("4. Copy the value of '__session'");
                AppendLog("5. Paste it in Settings → Tunnel providers → playit.gg API Key");
                return;
            }

            AppendLog("Using playit.gg REST API to manage tunnel...");

            // First, try to find an existing tunnel for this port
            var existingAddress = await FindExistingTunnelAsync(apiUrl, apiKey, ct);
            if (!string.IsNullOrEmpty(existingAddress))
            {
                SetPublicAddress(existingAddress);
                return;
            }

            AppendLog("No existing tunnel found for this port.");
            AppendLog("Please create a tunnel for this port via playit.gg web dashboard:");
            AppendLog("https://playit.gg/account/tunnels");
            Fail("No tunnel found. Create one via the web dashboard first.");
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            Fail($"Error managing remote API tunnel: {ex.Message}");
        }
    }

    private static async Task<string?> ReadSecretKeyAsync(string secretKeyPath, CancellationToken ct)
    {
        try
        {
            if (!File.Exists(secretKeyPath)) return null;

            var content = await File.ReadAllTextAsync(secretKeyPath, ct);
            // Parse TOML - simple line-based parsing for the secret key
            // Format: secret_key = "..."
            var match = System.Text.RegularExpressions.Regex.Match(content, @"secret_key\s*=\s*""([^""]+)""");
            return match.Success ? match.Groups[1].Value : null;
        }
        catch { return null; }
    }

    private async Task<string?> CreateTunnelAsync(string apiUrl, string secretKey, CancellationToken ct)
    {
        try
        {
            var port = Assignment.Port.Port;
            var protocol = Assignment.Port.Protocol.ToString().ToLower();

            var requestBody = new
            {
                tunnel_type = "raw-ports",
                port_type = protocol,
                port_count = 1,
                name = $"MinecraftControlHub-{port}",
                origin = new
                {
                    type = "ip",
                    ip = "127.0.0.1",
                    port = port
                }
            };

            var json = System.Text.Json.JsonSerializer.Serialize(requestBody);
            var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, $"{apiUrl}/tunnels/create");
            request.Content = content;
            request.Headers.Add("Authorization", $"Bearer {secretKey}");

            var response = await _http!.SendAsync(request, ct);
            var responseJson = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                AppendLog($"API error creating tunnel: {response.StatusCode} - {responseJson}");
                return null;
            }

            // Parse response to get tunnel ID
            using var doc = System.Text.Json.JsonDocument.Parse(responseJson);
            if (doc.RootElement.TryGetProperty("id", out var idProp))
                return idProp.GetString();

            return null;
        }
        catch (Exception ex)
        {
            AppendLog($"Exception creating tunnel: {ex.Message}");
            return null;
        }
    }

    private async Task<string?> FindExistingTunnelAsync(string apiUrl, string sessionCookie, CancellationToken ct)
    {
        try
        {
            var requestBody = new
            {
                agent_id = (string?)null  // List all tunnels
            };

            var json = System.Text.Json.JsonSerializer.Serialize(requestBody);
            var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, $"{apiUrl}/tunnels/list");
            request.Content = content;
            request.Headers.Add("Cookie", $"__session={sessionCookie}");

            var response = await _http!.SendAsync(request, ct);
            var responseJson = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                AppendLog($"API error listing tunnels: {response.StatusCode}");
                return null;
            }

            using var doc = System.Text.Json.JsonDocument.Parse(responseJson);
            var tunnels = doc.RootElement;

            var targetPort = Assignment.Port.Port;

            // Find a tunnel that matches our port
            foreach (var tunnel in tunnels.EnumerateArray())
            {
                // Check if this tunnel points to our target port
                if (tunnel.TryGetProperty("origin", out var origin))
                {
                    if (origin.TryGetProperty("port", out var portProp) && portProp.GetInt32() == targetPort)
                    {
                        // Get the connect address
                        if (tunnel.TryGetProperty("allocations", out var allocations))
                        {
                            foreach (var alloc in allocations.EnumerateArray())
                            {
                                if (alloc.TryGetProperty("connect_address", out var addrProp))
                                {
                                    var addr = addrProp.GetString();
                                    if (!string.IsNullOrEmpty(addr))
                                    {
                                        AppendLog($"Found existing tunnel for port {targetPort}: {addr}");
                                        return addr;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            AppendLog($"Exception finding existing tunnel: {ex.Message}");
            return null;
        }
    }

    private async Task<string?> GetTunnelAddressAsync(string apiUrl, string secretKey, string tunnelId, CancellationToken ct)
    {
        try
        {
            var requestBody = new
            {
                agent_id = (string?)null  // List all tunnels
            };

            var json = System.Text.Json.JsonSerializer.Serialize(requestBody);
            var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, $"{apiUrl}/tunnels/list");
            request.Content = content;
            request.Headers.Add("Authorization", $"Bearer {secretKey}");

            var response = await _http!.SendAsync(request, ct);
            var responseJson = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
                return null;

            using var doc = System.Text.Json.JsonDocument.Parse(responseJson);
            var tunnels = doc.RootElement;

            // Find our tunnel by ID
            foreach (var tunnel in tunnels.EnumerateArray())
            {
                if (tunnel.TryGetProperty("id", out var idProp) && idProp.GetString() == tunnelId)
                {
                    // Get the connect address
                    if (tunnel.TryGetProperty("allocations", out var allocations))
                    {
                        foreach (var alloc in allocations.EnumerateArray())
                        {
                            if (alloc.TryGetProperty("connect_address", out var addrProp))
                            {
                                var addr = addrProp.GetString();
                                if (!string.IsNullOrEmpty(addr))
                                    return addr;
                            }
                        }
                    }
                }
            }

            return null;
        }
        catch { return null; }
    }

    private static string? ExtractJsonPath(string json, string path)
    {
        try
        {
            using var doc     = System.Text.Json.JsonDocument.Parse(json);
            var       current = doc.RootElement;

            foreach (var segment in path.Split('.'))
            {
                var m = System.Text.RegularExpressions.Regex.Match(segment, @"^(\w+)(?:\[(\d+)\])?$");
                if (!m.Success) return null;

                var prop  = m.Groups[1].Value;
                var index = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : -1;

                if (!current.TryGetProperty(prop, out current)) return null;
                if (index >= 0)
                {
                    if (current.ValueKind != System.Text.Json.JsonValueKind.Array) return null;
                    current = current[index];
                }
            }

            return current.ValueKind == System.Text.Json.JsonValueKind.String
                ? current.GetString()
                : current.ToString();
        }
        catch { return null; }
    }

    /// <summary>
    /// Splits a quoted or unquoted command string into exe + args,
    /// resolves .lnk shortcuts, and finds the binary on PATH if needed.
    /// </summary>
    private static Process LaunchProcess(string command)
    {
        string exe, args;

        if (command.StartsWith('"'))
        {
            int end = command.IndexOf('"', 1);
            exe  = end > 0 ? command[1..end] : command.Trim('"');
            args = end > 0 ? command[(end + 1)..].Trim() : string.Empty;
        }
        else
        {
            int idx = command.IndexOf(' ');
            if (idx < 0) { exe = command; args = string.Empty; }
            else         { exe = command[..idx]; args = command[(idx + 1)..]; }
        }

        exe = ResolveExePath(exe);

        var psi = new ProcessStartInfo
        {
            FileName               = exe,
            Arguments              = args,
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
        };

        // Tell TUI programs (playit.gg, etc.) to use plain-text output.
        // TERM=dumb makes them skip cursor-positioning escape sequences.
        // NO_COLOR=1 is the de-facto standard to disable ANSI color codes.
        // Without these, playit v1.0 draws a full TUI over the pipe which
        // breaks line-based output parsing entirely.
        psi.EnvironmentVariables["TERM"]     = "dumb";
        psi.EnvironmentVariables["NO_COLOR"] = "1";

        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.Start();
        return p;
    }

    /// <summary>
    /// Resolves the executable path:
    /// • Follows .lnk Windows shortcuts to their real target.
    /// • If the path has no extension, appends .exe.
    /// • If the file does not exist as given, searches every directory on PATH.
    /// Throws <see cref="FileNotFoundException"/> with a helpful message if nothing is found.
    /// </summary>
    private static string ResolveExePath(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new FileNotFoundException(
                "No executable path is configured for this provider. " +
                "Open Settings → Tunnel providers and either enter the full path to the .exe or set it to the name of a binary on your PATH.");

        // Resolve .lnk shortcuts (Windows Shell Link)
        if (raw.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && File.Exists(raw))
        {
            var target = ResolveLnkTarget(raw);
            if (!string.IsNullOrEmpty(target))
                raw = target;
        }

        // Ensure .exe extension when browsing to a path without one
        if (!Path.HasExtension(raw))
            raw += ".exe";

        // If it exists as an absolute/relative path, done
        if (File.Exists(raw))
            return Path.GetFullPath(raw);

        // Not found as given — try every directory on PATH
        var name = Path.GetFileName(raw);
        var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        foreach (var dir in pathDirs)
        {
            var candidate = Path.Combine(dir.Trim(), name);
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException(
            $"Could not find \"{raw}\". " +
            "Make sure the path in Settings → Tunnel providers points to the actual .exe file " +
            "(not a shortcut or Start Menu entry), or install the tool so it is on your system PATH.",
            raw);
    }

    /// <summary>
    /// Reads the target path from a Windows .lnk shortcut file
    /// using a simple binary parser (no COM dependency needed).
    /// </summary>
    private static string? ResolveLnkTarget(string lnkPath)
    {
        try
        {
            // The Shell Link (.lnk) binary format stores the target path starting at a
            // fixed offset inside the StringData section.  We use a minimal parser that
            // covers the common case: local file targets stored in the LinkTarget IDList.
            // If that fails we fall back to reading the LocalBasePath field directly.
            using var fs = new FileStream(lnkPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var br = new BinaryReader(fs);

            // Header is always 0x4C bytes
            if (fs.Length < 0x4C) return null;
            fs.Seek(0x14, SeekOrigin.Begin); // LinkFlags
            var linkFlags = br.ReadUInt32();

            bool hasLinkTargetIDList = (linkFlags & 0x0001) != 0;
            bool hasLinkInfo         = (linkFlags & 0x0002) != 0;

            fs.Seek(0x4C, SeekOrigin.Begin); // end of header

            // Skip the IDList if present (first 2 bytes = size of IDList)
            if (hasLinkTargetIDList)
            {
                var idListSize = br.ReadUInt16();
                fs.Seek(idListSize, SeekOrigin.Current);
            }

            if (!hasLinkInfo) return null;

            long linkInfoStart = fs.Position;
            var linkInfoSize  = br.ReadUInt32();
            var linkInfoHeaderSize = br.ReadUInt32();
            /* flags */ br.ReadUInt32();
            /* VolumeIDOffset */ br.ReadUInt32();
            var localBasePathOffset = br.ReadUInt32();

            fs.Seek(linkInfoStart + localBasePathOffset, SeekOrigin.Begin);

            // Read null-terminated ASCII string
            var sb = new System.Text.StringBuilder();
            int b;
            while ((b = fs.ReadByte()) > 0) sb.Append((char)b);
            var localPath = sb.ToString();
            return string.IsNullOrWhiteSpace(localPath) ? null : localPath;
        }
        catch
        {
            return null;
        }
    }

    private string? BuildCommand(TunnelProvider provider, string exePath, string? apiKey)
    {
        var tunnelConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MinecraftControlHub", "tunnel-configs");
        Directory.CreateDirectory(tunnelConfigDir);

        return provider.CommandTemplate
            .Replace("{exe}",        exePath,  StringComparison.OrdinalIgnoreCase)
            .Replace("{port}",       Assignment.Port.Port.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{protocol}",   Assignment.Port.Protocol.ToString().ToLower(), StringComparison.OrdinalIgnoreCase)
            .Replace("{apikey}",     apiKey ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{configfile}", Path.Combine(tunnelConfigDir, $"{provider.Id}.toml"),
                     StringComparison.OrdinalIgnoreCase);
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (State == TunnelSessionState.Running || State == TunnelSessionState.Starting)
        {
            ErrorMessage = "Tunnel process exited unexpectedly.";
            SetState(TunnelSessionState.Error);
            AppendLog("Process exited unexpectedly.");
        }
    }

    private void SetPublicAddress(string addr)
    {
        PublicAddress = addr;
        SetState(TunnelSessionState.Running);
        AppendLog($"Tunnel active -> {addr}");
    }

    private void Fail(string message)
    {
        ErrorMessage = message;
        SetState(TunnelSessionState.Error);
        AppendLog($"ERROR: {message}");
    }

    private void SetState(TunnelSessionState state)
    {
        State = state;
        Changed?.Invoke(this, new TunnelSessionChangedEventArgs(this));
    }

    private void AppendLog(string line)
    {
        var stamped = $"[{DateTime.Now:HH:mm:ss}] {line}";
        lock (_logLock) { _log.Add(stamped); }
        Changed?.Invoke(this, new TunnelSessionChangedEventArgs(this));
    }

    // -----------------------------------------------------------------------
    // IDisposable
    // -----------------------------------------------------------------------

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _process?.Dispose();
        _http?.Dispose();
        _pollCts?.Dispose();
    }
}

// ---------------------------------------------------------------------------
// State enum & event args
// ---------------------------------------------------------------------------

public enum TunnelSessionState
{
    Idle,
    Starting,
    Running,
    Stopped,
    Error
}

public class TunnelSessionChangedEventArgs : EventArgs
{
    public TunnelSession Session { get; }
    public TunnelSessionChangedEventArgs(TunnelSession session) => Session = session;
}