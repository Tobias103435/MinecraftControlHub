namespace MinecraftControlHub.Core.Models;

/// <summary>
/// Tier / pricing category shown in the UI.
/// </summary>
public enum TunnelProviderTier
{
    Free,
    FreemiumLimited,   // free but with notable restrictions
    Premium            // paid
}

/// <summary>
/// How the service's public address is discovered after launch.
/// </summary>
public enum TunnelAddressSource
{
    /// <summary>Regex match on stdout output.</summary>
    StdoutRegex,
    /// <summary>Poll a local HTTP endpoint (e.g. ngrok's 4040 API).</summary>
    LocalApi,
    /// <summary>Use a remote REST API (e.g. playit.gg 1.0.X).</summary>
    RemoteApi,
    /// <summary>
    /// No process is launched and no API is called. The user pastes in the
    /// address themselves, read from a tool that's already running on its
    /// own (e.g. playit.gg installed via its official Windows wizard as a
    /// persistent background service — attempts to also drive it from a
    /// second, launcher-spawned process just make it detach immediately).
    /// </summary>
    Manual
}

/// <summary>
/// A descriptor for a known tunnel provider — everything the UI and
/// TunnelService need to start/stop a tunnel and display information.
/// </summary>
public class TunnelProvider
{
    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    /// <summary>Stable internal identifier — never shown in the UI as-is.</summary>
    public required string Id { get; init; }

    /// <summary>Human-readable name shown in the UI.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Short description / tagline shown in the UI.</summary>
    public required string Description { get; init; }

    /// <summary>Website URL — shown as a clickable link in the UI.</summary>
    public required string WebsiteUrl { get; init; }

    /// <summary>Pricing tier for badge coloring.</summary>
    public required TunnelProviderTier Tier { get; init; }

    /// <summary>Human-readable price string shown on the badge.</summary>
    public required string PriceLabel { get; init; }

    // -----------------------------------------------------------------------
    // Protocol capabilities
    // -----------------------------------------------------------------------

    public bool SupportsTcp { get; init; }
    public bool SupportsUdp { get; init; }

    // -----------------------------------------------------------------------
    // Requirements / notes
    // -----------------------------------------------------------------------

    /// <summary>
    /// True when the user needs to enter an API key / auth token in Settings
    /// before this provider can be used.
    /// </summary>
    public bool RequiresApiKey { get; init; }

    /// <summary>
    /// True when the provider binary must be on PATH or configured.
    /// False for providers launched via a bundled/downloaded exe.
    /// </summary>
    public bool RequiresExternalBinary { get; init; }

    /// <summary>
    /// Notable limitation shown in the UI (e.g. "No UDP — voice chat won't work").
    /// Null when there are no notable limitations.
    /// </summary>
    public string? Limitation { get; init; }

    // -----------------------------------------------------------------------
    // Address discovery
    // -----------------------------------------------------------------------

    public TunnelAddressSource AddressSource { get; init; }

    /// <summary>
    /// For StdoutRegex: the regex pattern. Must have a named group "addr"
    /// that captures the full public address (host:port).
    /// </summary>
    public string? StdoutPattern { get; init; }

    /// <summary>
    /// For LocalApi: the local URL to poll (e.g. "http://127.0.0.1:4040/api/tunnels").
    /// </summary>
    public string? LocalApiUrl { get; init; }

    /// <summary>
    /// For LocalApi: JSON path (dot-separated) to the public address field.
    /// E.g. "tunnels[0].public_url" for ngrok.
    /// </summary>
    public string? LocalApiJsonPath { get; init; }

    /// <summary>
    /// For RemoteApi: the base URL of the API (e.g. "https://api.playit.gg").
    /// </summary>
    public string? RemoteApiUrl { get; init; }

    /// <summary>
    /// For RemoteApi: path to the secret key file (e.g. playit.toml).
    /// </summary>
    public string? SecretKeyPath { get; init; }

    // -----------------------------------------------------------------------
    // Command template
    // -----------------------------------------------------------------------

    /// <summary>
    /// Command template. Placeholders:
    ///   {port}     — local port number
    ///   {protocol} — "tcp" or "udp"
    ///   {apikey}   — the stored API key / token (if RequiresApiKey)
    /// </summary>
    public required string CommandTemplate { get; init; }

    /// <summary>
    /// Optional: arguments to run before the tunnel (e.g. ngrok config add-authtoken).
    /// Null if not needed.
    /// </summary>
    public string? SetupCommandTemplate { get; init; }

    // -----------------------------------------------------------------------
    // Recommendation logic
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns true when this provider can handle the given port protocol.
    /// </summary>
    public bool CanHandle(PortProtocol protocol) => protocol switch
    {
        PortProtocol.TCP => SupportsTcp,
        PortProtocol.UDP => SupportsUdp,
        _                => false
    };

    /// <summary>
    /// Priority score used to pick the default recommended provider for a port.
    /// Higher = more recommended. Score is reduced by limitations.
    /// </summary>
    public int RecommendationScore(PortProtocol protocol)
    {
        if (!CanHandle(protocol)) return -1;

        var score = Tier switch
        {
            TunnelProviderTier.Free            => 100,
            TunnelProviderTier.FreemiumLimited => 80,
            TunnelProviderTier.Premium         => 60,
            _                                  => 0
        };

        if (RequiresApiKey)      score -= 10;
        if (RequiresExternalBinary) score -= 15;
        if (Limitation != null)  score -= 5;

        // Bonus: playit.gg is purpose-built for game servers
        if (Id == "playit") score += 20;

        return score;
    }
}

/// <summary>
/// Per-port tunnel assignment: which provider the user chose for a specific port.
/// </summary>
public class PortTunnelAssignment : System.ComponentModel.INotifyPropertyChanged
{
    private TunnelProvider? _selectedProvider;
    private TunnelProvider? _recommendedProvider;

    public required PortInfo Port { get; init; }

    /// <summary>
    /// Providers that support this port's protocol — used by the XAML ComboBox ItemsSource.
    /// </summary>
    public IReadOnlyList<TunnelProvider> CompatibleProviders =>
        MinecraftControlHub.Core.Services.TunnelProviderRegistry.ForProtocol(Port.Protocol);

    public TunnelProvider? RecommendedProvider
    {
        get => _recommendedProvider;
        set
        {
            _recommendedProvider = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(RecommendedProvider)));
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(RecommendedProviderLabel)));
        }
    }

    public TunnelProvider? SelectedProvider
    {
        get => _selectedProvider;
        set
        {
            _selectedProvider = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(SelectedProvider)));
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsUsingRecommended)));
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(SelectedProviderLimitation)));
        }
    }

    public string RecommendedProviderLabel =>
        RecommendedProvider != null ? $"Recommended: {RecommendedProvider.DisplayName}" : string.Empty;

    public bool IsUsingRecommended =>
        SelectedProvider?.Id == RecommendedProvider?.Id;

    public string? SelectedProviderLimitation =>
        SelectedProvider?.Limitation;

    public bool HasLimitation => !string.IsNullOrEmpty(SelectedProvider?.Limitation);

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}