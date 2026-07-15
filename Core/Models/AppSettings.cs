namespace MinecraftControlHub.Core.Models;

/// <summary>
/// User-configurable application preferences, persisted to disk as JSON.
/// </summary>
public class AppSettings
{
    /// <summary>
    /// When true, "Check for updates" will also download and install any newer
    /// mod versions it finds, instead of only reporting them.
    /// </summary>
    public bool AutoUpdateMods { get; set; }

    /// <summary>
    /// When true, the app checks installed mods for updates on startup.
    /// </summary>
    public bool CheckForUpdatesOnStartup { get; set; }

    /// <summary>
    /// UI theme: "Dark" (default) or "Light".
    /// </summary>
    public string Theme { get; set; } = "Dark";

    /// <summary>
    /// When true, newly imported/created installations use their own game directory.
    /// </summary>
    public bool KeepModsBackup { get; set; }

    /// <summary>
    /// The in-game player name used when launching in offline mode (no Microsoft account).
    /// </summary>
    public string OfflineUsername { get; set; } = "Player";

    // -----------------------------------------------------------------------
    // Tunnel settings
    // -----------------------------------------------------------------------

    /// <summary>
    /// Maps provider ID → full path to the provider executable.
    /// E.g. { "playit": "C:\\tools\\playit.exe", "ngrok": "C:\\tools\\ngrok.exe" }
    /// Providers not listed here fall back to the system PATH (binary name = provider id).
    /// </summary>
    public Dictionary<string, string> TunnelExePaths { get; set; } = new();

    /// <summary>
    /// Maps provider ID → API key / auth token.
    /// E.g. { "ngrok": "xxxxxxxx_xxxxxxxxxxxx" }
    /// Only populated for providers that require an API key.
    /// </summary>
    public Dictionary<string, string> TunnelApiKeys { get; set; } = new();

    // -----------------------------------------------------------------------
    // AI Terminal settings
    // -----------------------------------------------------------------------

    /// <summary>
    /// AI provider: "OpenAI", "Gemini", or "Custom".
    /// </summary>
    public string AiProvider { get; set; } = "Gemini";

    /// <summary>
    /// API key for the selected AI provider.
    /// </summary>
    public string AiApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Base URL for Custom / OpenAI-compatible endpoints.
    /// Not used when AiProvider is "Gemini" (fixed endpoint).
    /// </summary>
    public string AiApiEndpoint { get; set; } = "https://api.openai.com/v1";

    /// <summary>
    /// Model identifier to use for AI Terminal queries.
    /// </summary>
    public string AiModel { get; set; } = "gemini-3.1-flash-lite";

    // -----------------------------------------------------------------------
    // Java & Performance
    // -----------------------------------------------------------------------

    /// <summary>
    /// Maximum RAM allocated to Minecraft in MB. Default 4096 (4 GB).
    /// </summary>
    public int MaxRamMb { get; set; } = 4096;

    /// <summary>
    /// Minimum RAM allocated to Minecraft in MB. Default 512.
    /// </summary>
    public int MinRamMb { get; set; } = 512;

    /// <summary>
    /// Additional JVM arguments appended to the launch command.
    /// </summary>
    public string CustomJvmArguments { get; set; } = string.Empty;

    // -----------------------------------------------------------------------
    // Launch options
    // -----------------------------------------------------------------------

    /// <summary>
    /// When true, the launcher window is hidden/closed after starting the game.
    /// </summary>
    public bool CloseLauncherOnGameStart { get; set; }

    /// <summary>
    /// Custom game window width (0 = use default).
    /// </summary>
    public int GameWindowWidth { get; set; }

    /// <summary>
    /// Custom game window height (0 = use default).
    /// </summary>
    public int GameWindowHeight { get; set; }

    /// <summary>
    /// When true, show a desktop notification when mod updates are available.
    /// </summary>
    public bool NotifyOnModUpdates { get; set; } = true;
}
