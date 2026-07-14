using System.Windows;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using MinecraftControlHub.AI.Services;
using MinecraftControlHub.Core.Services;
using MinecraftControlHub.UI.ViewModels;

namespace MinecraftControlHub;

public partial class App : Application
{
    public IServiceProvider? ServiceProvider { get; private set; }

    public App()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        ServiceProvider = services.BuildServiceProvider();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Global unhandled exception handlers — log & show a dialog instead of crashing silently.
        DispatcherUnhandledException += (_, ex) =>
        {
            var log = ServiceProvider?.GetService<IAppLogService>();
            log?.LogError("App.Crash", $"Unhandled UI exception: {ex.Exception.GetType().Name}: {ex.Exception.Message}\n{ex.Exception.StackTrace}", ex.Exception);
            System.Windows.MessageBox.Show(
                $"An unexpected error occurred:\n\n{ex.Exception.Message}\n\nDetails have been logged to diagnostics.log.",
                "Unexpected Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            ex.Handled = true;   // prevent app from closing
        };

        TaskScheduler.UnobservedTaskException += (_, ex) =>
        {
            var log = ServiceProvider?.GetService<IAppLogService>();
            log?.LogError("App.TaskCrash", $"Unobserved task exception: {ex.Exception.Message}\n{ex.Exception.StackTrace}", ex.Exception);
            ex.SetObserved();
        };

        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
        {
            var log = ServiceProvider?.GetService<IAppLogService>();
            if (ex.ExceptionObject is Exception e2)
                log?.LogError("App.DomainCrash", $"AppDomain unhandled: {e2.Message}\n{e2.StackTrace}", e2);
        };

        var mainWindow = new MainWindow();
        mainWindow.Show();

        // Apply saved theme (dark/light) before the window is shown
        var settingsService = ServiceProvider?.GetService<ISettingsService>();
        if (settingsService != null)
            ThemeService.ApplySavedTheme(settingsService);

        // Wire crash detection: when a server crashes, automatically send the
        // crash report to the AI terminal for instant diagnosis.
        var serverService = ServiceProvider?.GetService<IServerService>();
        var aiVm          = ServiceProvider?.GetService<AiTerminalViewModel>();
        if (serverService != null && aiVm != null)
        {
            serverService.ServerCrashed += (_, args) =>
            {
                var errorContext = string.IsNullOrWhiteSpace(args.CrashReport)
                    ? $"Server '{args.Server.Name}' crashed unexpectedly (no crash report found)."
                    : $"Server '{args.Server.Name}' crashed. Crash report:\n\n{args.CrashReport}";

                Dispatcher.InvokeAsync(() => _ = aiVm.AskAiAboutErrorAsync(errorContext));
            };
        }
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Core services
        services.AddSingleton<IAppLogService, AppLogService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IInstallationService, InstallationService>();
        services.AddSingleton<IServerProvisioningService, ServerProvisioningService>();
        services.AddSingleton<IServerService, ServerService>();
        services.AddHttpClient<IModrinthApiClient, ModrinthApiClient>();
        // CurseForge calls go through our Nexora backend proxy (keeps the API key
        // server-side per CurseForge ToS). No x-api-key header on the client.
        services.AddHttpClient<ICurseForgeApiClient, CurseForgeApiClient>();
        services.AddSingleton<IJavaService, JavaService>();
        services.AddSingleton<IModService, ModService>();
        services.AddSingleton<HealthCheckService>();
        services.AddHttpClient<IMinecraftLauncherService, MinecraftLauncherService>();
        // Loader service (Fabric/Forge/Quilt/NeoForge) needs HttpClient for metadata lookup
        services.AddHttpClient<ILoaderService, LoaderService>();

        // Export/import of installations as .mrpack (Prism Launcher / Modrinth App
        // compatible modpacks) — reuses the Modrinth client + loader/installation
        // services above, so no HttpClient of its own is needed.
        services.AddSingleton<IModpackExportImportService, ModpackExportImportService>();
        services.AddHttpClient<IContentService, ContentService>();
        // Account service holds shared sign-in state, so it must be a singleton.
        services.AddSingleton<IMinecraftAccountService>(_ => new MinecraftAccountService(new System.Net.Http.HttpClient()));
        // Nexora API and account services
        services.AddHttpClient<INexoraApiService, NexoraApiService>();
        services.AddSingleton<INexoraAccountService, NexoraAccountService>();

        // Tunnel sharing & notifications
        services.AddHttpClient<ITunnelShareService, TunnelShareService>();
        services.AddSingleton<ITunnelNotificationManager, TunnelNotificationManager>();

        // Instance sharing & notifications
        services.AddHttpClient<IInstanceShareService, InstanceShareService>();
        services.AddSingleton<IInstanceNotificationManager, InstanceNotificationManager>();

        // Tunnel / port scanning
        services.AddSingleton<IPortScanService, PortScanService>();
        services.AddSingleton<MinecraftControlHub.Networking.ITunnelService, MinecraftControlHub.Networking.TunnelService>();

        // AI services
        services.AddSingleton<IKnowledgeService, KnowledgeService>();
        services.AddHttpClient<IAIService, AIService>();
        services.AddSingleton<AICommandExecutor>();
        services.AddSingleton<AITerminalService>();
        // ViewModels
        services.AddTransient<HomePageViewModel>();
        services.AddTransient<AccountPageViewModel>();
        services.AddTransient<ServersPageViewModel>();
        services.AddTransient<ModsPageViewModel>();
        services.AddTransient<SettingsPageViewModel>();
        services.AddTransient<FriendsPageViewModel>();
        services.AddTransient<TunnelPageViewModel>();
        services.AddSingleton<AiTerminalViewModel>();
    }
}
