# Port Detection and Management

## Overview

`PortScanService` detects which TCP and UDP ports a Minecraft server is actively using by reading configuration files. This information is used when creating tunnel assignments — the Tunnel page can automatically suggest the correct port for a server.

---

## PortScanService

```csharp
public interface IPortScanService
{
    Task<IReadOnlyList<PortInfo>> GetServerPortsAsync(string serverDirectory);
    Task<int> FindAvailablePortAsync(int preferredPort = 25565);
}
```

---

## Port Detection Sources

`GetServerPortsAsync` scans multiple configuration files in a server directory:

| Source | Port type | Key |
|---|---|---|
| `server.properties` | TCP (Java) | `server-port` |
| `server.properties` | UDP (Bedrock/Geyser) | `bedrock-port` |
| `config/geyser-spigot.yml` | UDP | `port` under `bedrock:` |
| `plugins/floodgate/config.yml` | TCP | `port` |
| `config/paper-global.yml` | TCP | `proxies.velocity.port` |

---

## PortInfo Model

```csharp
public class PortInfo
{
    public int    Port     { get; init; }
    public string Protocol { get; init; }  // "tcp" or "udp"
    public string Source   { get; init; }  // e.g. "server.properties"
    public string Label    { get; init; }  // e.g. "Java Edition", "Bedrock (Geyser)"
}
```

---

## Available Port Detection

`FindAvailablePortAsync` scans for the preferred port (default 25565) and increments until a free one is found:

```csharp
public async Task<int> FindAvailablePortAsync(int preferredPort = 25565)
{
    for (int port = preferredPort; port < preferredPort + 100; port++)
    {
        if (!IsPortInUse(port)) return port;
    }
    throw new InvalidOperationException("No available port found in range");
}
```

Used by the "New Server" wizard to pre-fill the port field with a free port.
