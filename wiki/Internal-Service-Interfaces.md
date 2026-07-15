# Internal Service Interfaces

## Installation & Launcher

```csharp
public interface IInstallationService
{
    Task<IReadOnlyList<Installation>> GetAllInstallationsAsync();
    Task<Installation> CreateInstallationAsync(Installation installation);
    Task UpdateInstallationAsync(Installation installation);
    Task DeleteInstallationAsync(string id);
    Task<Installation?> FindByNameAsync(string name);
    event EventHandler? InstallationsChanged;
}

public interface IMinecraftLauncherService
{
    Task LaunchAsync(string installationId);
}

public interface IJavaService
{
    Task<IReadOnlyList<JavaInstallation>> DetectInstalledJavaAsync();
    Task<JavaInstallation?> GetRecommendedAsync(string minecraftVersion);
    Task<string?> DownloadJavaAsync(int featureVersion);
}

public interface ILoaderService
{
    Task<IReadOnlyList<string>> GetFabricVersionsAsync(string minecraftVersion);
    Task<IReadOnlyList<string>> GetForgeVersionsAsync(string minecraftVersion);
    Task<IReadOnlyList<string>> GetNeoForgeVersionsAsync(string minecraftVersion);
    Task<IReadOnlyList<string>> GetQuiltVersionsAsync(string minecraftVersion);
}

public interface IRamCalculatorService
{
    RamRecommendation Calculate(Installation installation, int modCount);
}
```

---

## Server Management

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

public interface IServerProvisioningService
{
    Task ProvisionAsync(Server server, IProgress<string> progress);
}
```

---

## Mods & Content

```csharp
public interface IModService
{
    Task<IReadOnlyList<Mod>> GetInstalledModsAsync(string targetId);
    Task<InstallResult> InstallModAsync(string targetId, string modNameOrId);
    Task RemoveModAsync(string targetId, string modId);
    Task<IReadOnlyList<ModUpdate>> CheckUpdatesAsync(string targetId);
    Task UpdateModAsync(string targetId, string modId);
    Task<IReadOnlyList<SearchResult>> SearchAsync(string query, string loader, string gameVersion);
}

public interface IContentService
{
    Task<IReadOnlyList<ContentItem>> GetResourcePacksAsync(string installationId);
    Task<IReadOnlyList<ContentItem>> GetShaderPacksAsync(string installationId);
    Task<IReadOnlyList<WorldSave>> GetWorldSavesAsync(string installationId);
    Task InstallFromFileAsync(string installationId, ContentType type, string filePath);
    Task RemoveAsync(string installationId, string itemId);
    Task SetEnabledAsync(string installationId, string itemId, bool enabled);
}

public interface IModpackExportImportService
{
    Task<byte[]> ExportInstallationAsync(string installationId);
    Task<Installation> ImportModpackAsync(byte[] mrpackBytes, string name);
}
```

---

## Authentication & Nexora

```csharp
public interface IMinecraftAccountService
{
    MinecraftAccount? Current { get; }
    Task<DeviceCodeResponse> StartDeviceCodeFlowAsync();
    Task<MinecraftAccount?> PollForTokenAsync(DeviceCodeResponse deviceCode);
    Task<MinecraftAccount?> LoadStoredAccountAsync();
    Task SignOutAsync();
}

public interface INexoraAccountService
{
    NexoraAccount? Current { get; }
    Task SignInAsync(string emailOrUsername, string password);
    Task<bool> Verify2FAAsync(string challenge, string code);
    Task SignOutAsync();
    Task ValidateStoredTokenAsync();
    Task LinkMinecraftAccountAsync(MinecraftAccount minecraft);
    event EventHandler? AccountChanged;
}
```

---

## AI

```csharp
public interface IAIService
{
    bool IsConfigured { get; }
    Task<string> CompleteAsync(IReadOnlyList<ChatMessage> messages, CancellationToken ct = default);
    IAsyncEnumerable<string> StreamAsync(IReadOnlyList<ChatMessage> messages, CancellationToken ct = default);
    Task<List<string>> GetGeminiModelsAsync(string apiKey, CancellationToken ct = default);
}

public interface IKnowledgeService
{
    Task<string> BuildSystemPromptAsync();
}
```

---

## Infrastructure

```csharp
public interface ISettingsService
{
    AppSettings Settings { get; }
    void Save();
    void Reload();
}

public interface IAppLogService
{
    void Log(string message);
    void LogError(string message, Exception? ex = null);
    IReadOnlyList<string> GetRecentLines(int count = 100);
}

public interface IHealthCheckService
{
    Task<HealthReport> CheckInstallationAsync(string installationId);
}
```
