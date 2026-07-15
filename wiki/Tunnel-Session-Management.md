# Tunnel Session Management

## Overview

`TunnelSession` manages the lifecycle of a single active tunnel — from process launch to address discovery to clean shutdown. It abstracts over all address discovery modes so `TunnelService` has a uniform interface regardless of provider.

---

## Session Lifecycle

```
CreateSessionAsync()
    → Resolve executable path (.lnk, PATH fallback)
    → Build command from template
    → Set TERM=dumb, NO_COLOR=1 (suppress TUI escape sequences)
    → Start process
    → Begin stdout reader task
    → Begin address discovery (mode-dependent)
    → Session.State = Running

Address discovered
    → Session.PublicAddress = "xxx.joinmc.link:25565"
    → SessionUpdated event fires

StopSessionAsync()
    → Kill process
    → Session.State = Stopped
    → SessionUpdated event fires
```

---

## Process Launch

```csharp
var psi = new ProcessStartInfo
{
    FileName = resolvedExePath,
    Arguments = BuildArguments(provider, port),
    RedirectStandardOutput = true,
    RedirectStandardError  = true,
    UseShellExecute = false,
    CreateNoWindow  = true,
    Environment =
    {
        ["TERM"]     = "dumb",
        ["NO_COLOR"] = "1"
    }
};
```

`TERM=dumb` and `NO_COLOR=1` prevent TUI tools from emitting ANSI/VT100 escape sequences that would corrupt the stdout parser.

---

## ANSI Stripping

Even with the environment variables, some providers still emit escape sequences. All stdout lines go through an ANSI stripper before pattern matching:

```csharp
private static string StripAnsi(string text)
    => Regex.Replace(text, @"\x1B\[[0-9;]*[a-zA-Z]|\x1B\][^\x07]*\x07", string.Empty);
```

Timestamp prefixes (e.g. `[2025-07-15 14:32:01]`) are also stripped before regex matching.

---

## StdoutRegex Address Discovery

For `StdoutRegex` providers, every stdout line is tested against the provider's pattern:

```csharp
// playit.gg stdout patterns (multiple formats observed in the wild):
// "connect address: whacking-unbroken.gl.joinmc.link"
// "Connect address: something.playit.gg:25565"
// "tunnel address: something.mc.gl:19132"
// "Allocated address: foo.joinmc.link:25565"
// "● whacking-unbroken.gl.joinmc.link => 127.0.0.1:25565"  (TUI table row)
```

The regex is broad enough to catch all observed playit output formats.

---

## LocalApi Address Discovery (ngrok)

For ngrok, the public address is obtained by polling `http://localhost:4040/api/tunnels`:

```csharp
var response = await _http.GetAsync(provider.LocalApiUrl);
var json = await response.Content.ReadAsStringAsync();
// Extract address via dot-path: "tunnels[0].public_url"
var address = ExtractByPath(json, provider.LocalApiJsonPath);
```

Polling continues every 2 seconds until an address is found or a timeout occurs.

---

## Error States

| State | Cause |
|---|---|
| `ExeNotFound` | Executable path not configured or file not found |
| `AuthError` | Process exits immediately with auth-related output |
| `PortInUse` | Process reports port already bound |
| `Timeout` | Address not discovered within the configured timeout |
| `ProcessExited` | Process exited unexpectedly after starting |
