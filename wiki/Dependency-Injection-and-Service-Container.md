# Dependency Injection & Service Container

## Overview

MinecraftControlHub uses Microsoft's built-in DI container (`Microsoft.Extensions.DependencyInjection`), registered in `App.ConfigureServices()`. Every service is registered against its interface, so the UI and AI layers never hold a concrete implementation reference.

---

## Service Registrations

### Core Services

```csharp
// Singletons — shared state across the app
services.AddSingleton<ISettingsService, SettingsService>();
services.AddSingleton<IAppLogService, AppLogService>();
services.AddSingleton<IInstallationService, InstallationService>();
services.AddSingleton<IServerService, ServerService>();
services.AddSingleton<IMinecraftAccountService, MinecraftAccountService>();
services.AddSingleton<INexoraAccountService, NexoraAccountService>();
services.AddSingleton<IInstanceNotificationManager, InstanceNotificationManager>();
services.AddSingleton<ITunnelNotificationManager, TunnelNotificationManager>();
services.AddSingleton<ITunnelService, TunnelService>();
services.AddSingleton<IHealthCheckService, HealthCheckService>();
services.AddSingleton<IRamCalculatorService, RamCalculatorService>();

// HttpClient-managed (connection pooling, lifetime management)
services.AddHttpClient<INexoraApiService, NexoraApiService>();
services.AddHttpClient<IModrinthApiClient, ModrinthApiClient>();
services.AddHttpClient<IInstanceShareService, InstanceShareService>();
services.AddHttpClient<ITunnelShareService, TunnelShareService>();
services.AddHttpClient<IAIService, AIService>();

// Transient — new instance per injection
services.AddTransient<IModService, ModService>();
services.AddTransient<ILoaderService, LoaderService>();
services.AddTransient<IMinecraftLauncherService, MinecraftLauncherService>();
services.AddTransient<IJavaService, JavaService>();
services.AddTransient<IServerProvisioningService, ServerProvisioningService>();
services.AddTransient<IModpackExportImportService, ModpackExportImportService>();
services.AddTransient<IContentService, ContentService>();
```

### AI Services

```csharp
services.AddSingleton<IKnowledgeService, KnowledgeService>();
services.AddSingleton<IAITerminalService, AITerminalService>();
services.AddTransient<AICommandExecutor>();
```

### ViewModels

ViewModels are registered as transient and resolved directly by pages:

```csharp
services.AddTransient<HomePageViewModel>();
services.AddTransient<ServersPageViewModel>();
services.AddTransient<ModsPageViewModel>();
services.AddTransient<AiTerminalViewModel>();
services.AddTransient<TunnelPageViewModel>();
services.AddTransient<FriendsPageViewModel>();
services.AddTransient<SettingsPageViewModel>();
```

---

## Service Lifetimes

| Lifetime | Used for | Reason |
|---|---|---|
| `Singleton` | Services with shared state (installations list, servers, account, notifications) | State must be consistent across the whole app |
| `AddHttpClient` | HTTP-calling services | `IHttpClientFactory` manages connection pooling and avoids socket exhaustion |
| `Transient` | Stateless services (loader version fetching, mod search, launching) | No shared state needed; fresh instance is fine |

---

## Resolution

Services are resolved through constructor injection. A page resolves its ViewModel from the DI container:

```csharp
public partial class HomePage : UserControl
{
    public HomePageViewModel ViewModel { get; }

    public HomePage(HomePageViewModel vm)
    {
        InitializeComponent();
        ViewModel = vm;
        DataContext = ViewModel;
    }
}
```

`MainWindow` resolves pages from the container and caches them to preserve state during navigation:

```csharp
private readonly Dictionary<AppPage, UserControl> _pageCache = new();

private UserControl GetPage(AppPage page)
{
    if (!_pageCache.TryGetValue(page, out var ctrl))
    {
        ctrl = page switch
        {
            AppPage.Home    => _provider.GetRequiredService<HomePage>(),
            AppPage.Servers => _provider.GetRequiredService<ServersPage>(),
            // ...
        };
        _pageCache[page] = ctrl;
    }
    return ctrl;
}
```
