# Utility and Helper Services

## AppPaths

`AppPaths` is a static class providing all file system paths used by the app. All paths are rooted under `%LocalAppData%\MinecraftControlHub\`.

| Property | Path |
|---|---|
| `BaseDir` | `%LocalAppData%\MinecraftControlHub\` |
| `SettingsFile` | `BaseDir\settings.json` |
| `NexoraAccountFile` | `BaseDir\nexora_account.json` |
| `MinecraftAccountFile` | `BaseDir\minecraft_account.json` |
| `InstancesDir` | `BaseDir\instances\` |
| `ServersDir` | `BaseDir\Servers\` |
| `MinecraftDir` | `BaseDir\minecraft\` |
| `LogsDir` | `BaseDir\logs\` |
| `DiagnosticsLog` | `BaseDir\logs\diagnostics.log` |
| `TunnelConfigsDir` | `BaseDir\tunnel-configs\` |

---

## AppLogService

Append-only diagnostics logger.

```csharp
public interface IAppLogService
{
    void Log(string message);
    void LogError(string message, Exception? ex = null);
    IReadOnlyList<string> GetRecentLines(int count = 100);
}
```

- Writes to `diagnostics.log` with UTC timestamps
- Thread-safe (file lock per write)
- Does not rotate — clear manually if it grows large
- Used by services for background errors, not for game console output

---

## HealthCheckService

Aggregates health signals for an installation into a numeric score and a `HealthReport`.

```csharp
public interface IHealthCheckService
{
    Task<HealthReport> CheckInstallationAsync(string installationId);
}
```

See [Health Monitoring and Diagnostics](Health-Monitoring-and-Diagnostics) for the full scoring algorithm.

---

## RamCalculatorService

Recommends min/max RAM for an installation based on Minecraft version, loader, and mod count.

```csharp
public interface IRamCalculatorService
{
    RamRecommendation Calculate(Installation installation, int modCount);
}

public record RamRecommendation(int Min, int Max); // MB
```

---

## ThemeService

Static helper that applies WPF `ResourceDictionary` theme files at runtime.

```csharp
public static class ThemeService
{
    public static void ApplySavedTheme(ISettingsService settings);
    public static void ApplyTheme(string themeName); // "Theme" | "LightTheme"
}
```

Called in `App.OnStartup` before the main window renders to avoid a flash of the wrong theme.

---

## JavaService

```csharp
public interface IJavaService
{
    Task<IReadOnlyList<JavaInstallation>> DetectInstalledJavaAsync();
    Task<JavaInstallation?> GetRecommendedAsync(string minecraftVersion);
    Task<string?> DownloadJavaAsync(int featureVersion);
}
```

Scans common paths, the Windows Registry, and `JAVA_HOME`. Calls Adoptium API to download missing Java versions when needed.

---

## PortScanService

Detects which ports a server directory is using by reading `server.properties` and plugin/mod configs.

```csharp
public interface IPortScanService
{
    Task<IReadOnlyList<PortInfo>> GetServerPortsAsync(string serverDirectory);
    Task<int> FindAvailablePortAsync(int preferredPort = 25565);
}
```

See [Port Detection and Management](Port-Detection-and-Management) for details.
