# Server Management Service

## Overview

`ServerService` manages the full lifecycle of Minecraft servers — provisioning, starting, stopping, and deleting them — and streams live console output to the UI.

---

## IServerService

```csharp
public interface IServerService
{
    Task<IReadOnlyList<Server>> GetAllServersAsync();
    Task<Server> CreateServerAsync(Server server);
    Task StartServerAsync(string id);
    Task StopServerAsync(string id);
    Task SendCommandAsync(string id, string command);
    Task DeleteServerAsync(string id);
    Task<Server?> FindByNameAsync(string name);

    event EventHandler? ServersChanged;
    event EventHandler<ServerOutputEventArgs>? ServerOutputReceived;
    event EventHandler<ServerCrashedEventArgs>? ServerCrashed;
}
```

---

## Server Model

```csharp
public class Server
{
    public string Id             { get; init; }   // GUID
    public string Name           { get; set; }
    public string Type           { get; set; }    // Vanilla | Paper | Purpur | Fabric | Forge | NeoForge | Quilt
    public string MinecraftVersion { get; set; }
    public int    Ram            { get; set; }    // MB
    public int    Port           { get; set; }
    public bool   IsRunning      { get; private set; }
}
```

---

## Server Directory Layout

Each server lives in its own directory:
```
Servers/<id>/
  server.jar           (or start.bat for loaders that use it)
  server.properties
  world/
  plugins/ or mods/
  logs/
    latest.log
  eula.txt             (auto-accepted on creation)
```

---

## Provisioning

`ServerProvisioningService` downloads the correct server artifact:

| Type | Source |
|---|---|
| Vanilla | Mojang version manifest |
| Paper | PaperMC API |
| Purpur | Purpur API |
| Fabric | Fabric installer → generates start.sh/bat |
| Forge | Forge installer → runs headless install |
| NeoForge | NeoForge Maven → runs headless install |
| Quilt | Quilt installer |

Provisioning runs in the background with progress events. The server appears in the list as "provisioning" until complete.

---

## Process Management

```csharp
private async Task StartServerAsync(Server server)
{
    var psi = new ProcessStartInfo
    {
        FileName = "java",
        Arguments = $"-Xms{server.Ram}M -Xmx{server.Ram}M -jar server.jar nogui",
        WorkingDirectory = serverDirectory,
        RedirectStandardOutput = true,
        RedirectStandardInput  = true,
        UseShellExecute = false
    };

    _process = Process.Start(psi);
    server.IsRunning = true;
    _ = ReadOutputAsync(_process); // fire-and-forget background reader
}
```

---

## Crash Detection

When the server process exits with a non-zero exit code:

```csharp
_process.Exited += (_, _) =>
{
    if (_process.ExitCode != 0)
    {
        var report = ReadCrashReport(server.Directory);
        ServerCrashed?.Invoke(this, new ServerCrashedEventArgs(server, report));
    }
};
```

`App.xaml.cs` subscribes to `ServerCrashed` and automatically forwards the report to the AI terminal for diagnosis.

---

## Console Streaming

Every line of server stdout is broadcast via `ServerOutputReceived`:

```csharp
private async Task ReadOutputAsync(Process process)
{
    while (!process.StandardOutput.EndOfStream)
    {
        var line = await process.StandardOutput.ReadLineAsync();
        ServerOutputReceived?.Invoke(this, new(server.Id, line));
    }
}
```

The `ServersPage` terminal subscribes and appends each line to the UI `TextBox`.
