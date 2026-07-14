# Networking

Services that reach outside the local machine via external tunnel processes.

## Implemented

### `TunnelSession`
One running tunnel process per port/provider pair. Handles:
- Process launch using the provider's command template (`{exe}`, `{port}`, `{protocol}`, `{apikey}`, `{configfile}` placeholders)
- Address discovery via **StdoutRegex** (playit.gg, bore, serveo, frp) and **LocalApi polling** (ngrok)
- Live log capture (stdout + stderr) exposed as `IReadOnlyList<string> Log`
- `TunnelSessionState` enum: `Idle` → `Starting` → `Running` / `Error` / `Stopped`
- `Changed` event fired on every state, address, or log update
- Thread-safe; UI thread marshalling done in `TunnelPageViewModel`

### `TunnelService`
Manages the lifecycle of all sessions:
- `StartAllAsync` — one session per enabled port assignment, runs in parallel
- `StartOneAsync` — start or restart a single port assignment
- `StopOne(port)` / `StopAll()` — graceful kill + dispose
- `SessionChanged` event forwarded from individual sessions

## Providers supported
| Provider    | TCP | UDP | Key needed | Address discovery |
|-------------|-----|-----|------------|-------------------|
| playit.gg   | ✓   | ✓   | No (free)  | stdout regex      |
| ngrok       | ✓   | —   | Yes        | local API (:4040) |
| bore.pub    | ✓   | —   | No         | stdout regex      |
| serveo.net  | ✓   | —   | No (ssh)   | stdout regex      |
| frp         | ✓   | ✓   | Yes (VPS)  | stdout regex      |
| playit Pro  | ✓   | ✓   | Yes        | stdout regex      |
| ngrok Pro   | ✓   | —   | Yes        | local API (:4040) |

## AI integration
`AICommandExecutor` can start and stop tunnels via `ITunnelService.StartOneAsync` / `StopOne`.
The executor resolves the provider from `TunnelProviderRegistry`, checks the API key from
`AppSettings.TunnelApiKeys`, and falls back to the binary name on PATH if no explicit exe path
is configured. If a required API key is missing it returns an actionable error message with a
link to the provider's website instead of silently failing.

## Planned
- UDP tunnel support for game providers that need it
- Automatic reconnect on tunnel drop
