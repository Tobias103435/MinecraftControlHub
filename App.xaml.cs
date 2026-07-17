using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
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

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Global unhandled exception handlers — log & show a dialog instead of crashing silently.
        // NOTE: Dispatcher.UIThread.UnhandledException only intercepts exceptions that flow
        // through Avalonia's dispatcher (mirrors WPF's DispatcherUnhandledException, but with a
        // narrower scope by design — see Avalonia docs on unhandled exceptions).
        Dispatcher.UIThread.UnhandledException += (_, ex) =>
        {
            var log = ServiceProvider?.GetService<IAppLogService>();
            log?.LogError("App.Crash", $"Unhandled UI exception: {ex.Exception.GetType().Name}: {ex.Exception.Message}\n{ex.Exception.StackTrace}", ex.Exception);
            ShowErrorDialog($"An unexpected error occurred:\n\n{ex.Exception.Message}\n\nDetails have been logged to diagnostics.log.");
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

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = new MainWindow();
            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();

        // Apply saved theme (dark/light) before the window is shown
        var settingsService = ServiceProvider?.GetService<ISettingsService>();
        if (settingsService != null)
            ThemeService.ApplySavedTheme(settingsService);

        // Background update check — runs silently; subscribers (TopBar, SettingsPage) react via the UpdateChecked event
        _ = Task.Run(async () =>
        {
            try
            {
                var updateService = ServiceProvider?.GetService<IUpdateService>();
                if (updateService != null)
                    await updateService.CheckAsync();
            }
            catch { /* never crash the app over a failed update check */ }
        });

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

                Dispatcher.UIThread.Post(() => _ = aiVm.AskAiAboutErrorAsync(errorContext));
            };
        }
    }

    /// <summary>
    /// Avalonia has no built-in MessageBox (unlike WPF's System.Windows.MessageBox).
    /// This is a minimal stand-in that preserves the "show the user something went
    /// wrong" behavior without pulling in a dialog library.
    /// </summary>
    private static void ShowErrorDialog(string message)
    {
        try
        {
            var okButton = new Button { Content = "OK", HorizontalAlignment = HorizontalAlignment.Right };

            var window = new Window
            {
                Title = "Unexpected Error",
                Width = 480,
                Height = 220,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Content = new StackPanel
                {
                    Margin = new Avalonia.Thickness(20),
                    Spacing = 16,
                    Children =
                    {
                        new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                        okButton
                    }
                }
            };

            okButton.Click += (_, _) => window.Close();
            window.Show();
        }
        catch { /* best effort — never let the error dialog itself crash the app */ }
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

        // Auto-update
        services.AddHttpClient<IUpdateService, UpdateService>();

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
