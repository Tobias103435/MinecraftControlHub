using Avalonia;

namespace MinecraftControlHub;

internal static class Program
{
    // Avalonia apps need an explicit Main/entry point — WPF generated this
    // automatically via UseWPF + the App.xaml StartupUri, which no longer
    // applies now that the project is an Avalonia app.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .LogToTrace();
}
