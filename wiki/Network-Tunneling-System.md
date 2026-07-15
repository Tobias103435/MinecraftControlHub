# Network Tunneling System

## Overview

The tunneling subsystem exposes a local Minecraft server to the internet without requiring port forwarding on the user's router. It supports five providers out of the box, each with different authentication requirements and address discovery methods.

---

## Components

| Component | Responsibility |
|---|---|
| `TunnelProviderRegistry` | Static registry of all provider descriptors (`TunnelProvider` models) |
| `TunnelService` | Manages active `TunnelSession` objects per port/provider assignment |
| `TunnelSession` | Handles process launch, stdout parsing, ANSI stripping, address discovery, error states |
| `PortScanService` | Scans `server.properties` and configs to find which ports a server is using |
| `TunnelShareService` | Sends a tunnel address to friends via the Nexora API |
| `TunnelNotificationManager` | Polls for incoming tunnel-share notifications from friends |

---

## Address Discovery Modes

Each provider uses one of four modes to discover the public tunnel address:

| Mode | How it works | Providers |
|---|---|---|
| `StdoutRegex` | Parses the tunnel process stdout with a regex pattern | playit.gg (premium), ngrok (legacy), bore, serveo, frp |
| `LocalApi` | Polls a local HTTP endpoint (e.g. ngrok's `localhost:4040/api/tunnels`) | ngrok (modern) |
| `RemoteApi` | Calls a REST API using a stored secret/token | playit Pro |
| `Manual` | No process launched; user pastes the address from an external tool | playit.gg (free tier) |

---

## Supported Providers

| Provider | Auth required | TCP | UDP | Address mode |
|---|---|---|---|---|
| playit.gg (free) | None | ✓ | ✓ | Manual |
| playit Pro | `playit.toml` secret | ✓ | ✓ | RemoteApi |
| ngrok | Authtoken | ✓ | ✗ | LocalApi |
| bore.pub | None | ✓ | ✗ | StdoutRegex |
| serveo.net | None (SSH) | ✓ | ✗ | StdoutRegex |
| frp | Server token | ✓ | ✗ | StdoutRegex |

**playit.gg is the recommended provider** — it supports both TCP and UDP (Bedrock-compatible), works for free without an account, and is purpose-built for game servers (+20 recommendation score bonus in the scoring algorithm).

---

## TunnelService

`TunnelService` manages assignments of ports to providers. Each assignment becomes a `TunnelSession`:

```csharp
public interface ITunnelService
{
    Task<TunnelSession> CreateSessionAsync(int port, string providerId);
    Task StopSessionAsync(string sessionId);
    IReadOnlyList<TunnelSession> ActiveSessions { get; }
    event EventHandler<TunnelSession>? SessionUpdated;
}
```

---

## Sharing

After a tunnel is active, the public address can be shared with friends through the Nexora friend system:

```csharp
// ShareTunnelWindow
await _tunnelShareService.ShareTunnelAsync(
    token: nexoraToken,
    recipientUsernames: selectedFriends,
    address: tunnelSession.PublicAddress
);
```

Recipients receive a notification in their launcher (polled every 30 seconds by `TunnelNotificationManager`).

---

## Related Pages

- [Tunnel Provider Architecture](Tunnel-Provider-Architecture)
- [Tunnel Session Management](Tunnel-Session-Management)
- [Port Detection and Management](Port-Detection-and-Management)
- [Supported Tunnel Providers](Supported-Tunnel-Providers)
- [playit.gg Provider](playit.gg-Provider)
