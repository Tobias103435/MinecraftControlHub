using MinecraftControlHub.Core.Models;

namespace MinecraftControlHub.Core.Services;

/// <summary>
/// Central registry of all known tunnel providers.
/// Providers are listed in display order (best/most-recommended first).
/// </summary>
public static class TunnelProviderRegistry
{
    public static IReadOnlyList<TunnelProvider> All { get; } = BuildProviders();

    // -----------------------------------------------------------------------
    // playit.gg stdout patterns
    //
    // playit prints addresses in a few different formats depending on the
    // version and tunnel type.  Known domain suffixes:
    //   *.playit.gg       — classic / dedicated-IP tunnels
    //   *.joinmc.link     — free Minecraft-specific tunnels (Java + Bedrock)
    //   *.ip.joinmc.link  — variant used by some regions
    //   *.mc.gl           — older free tunnel domain (still seen occasionally)
    //
    // Observed line formats (all matched by the pattern below):
    //   "connect address: whacking-unbroken.gl.joinmc.link"         (no port)
    //   "Connect address: something.playit.gg:25565"
    //   "tunnel address: something.mc.gl:19132"
    //   "Allocated address: foo.joinmc.link:25565"
    // -----------------------------------------------------------------------
    // Playit prints addresses in multiple formats depending on version:
    //   TUI table row:  "● whacking-unbroken.gl.joinmc.link => 127.0.0.1:25565"
    //   Log line:       "connect address: something.playit.gg:25565"
    //   Older formats:  "tunnel address: ...", "Allocated address: ..."
    // The pattern matches both the bullet-table format and the keyword-prefix format.
    // Playit stdout patterns after ANSI stripping (done in TunnelSession.StripAnsi).
    // Three variants observed in the wild:
    //   1. TUI bullet:   "● whacking-unbroken.gl.joinmc.link => 127.0.0.1:25565"
    //   2. Log keyword:  "connect address: something.playit.gg:25565"
    //   3. Bare address: "whacking-unbroken.gl.joinmc.link => 127.0.0.1:25565"
    private const string PlayitStdoutPattern =
        // Matches all known formats:
        //   --stdout log:  "... connect address: whacking.joinmc.link"
        //   TUI bullet:    "● whacking.joinmc.link => 127.0.0.1:25565"
        //   Bare address:  "whacking.joinmc.link => 127.0.0.1:25565"
        @"(?:(?:connect|tunnel|allocated)\s*(?:address)?:\s*|[●•\*]\s*|=>\s*)" +
        @"(?<addr>[\w\-][\w\-\.]*\.(?:playit\.gg|joinmc\.link|ip\.joinmc\.link|mc\.gl)" +
        @"(?::\d+)?)";

    private static IReadOnlyList<TunnelProvider> BuildProviders() => new[]
    {
        // ---------------------------------------------------------------
        // playit.gg — FREE, TCP + UDP, purpose-built for game servers
        //
        // FIXED (again): the process+stdout-regex approach was correct in
        // isolation, but playit's official Windows install is a GUI wizard
        // that sets itself up as a persistent background service. Once that
        // service exists, launching the daemon exe a second time (from the
        // launcher) just detects it and detaches immediately ("Detached
        // from service...") — there's no output to read, because the real
        // tunnel work happens in a separate, already-running process
        // outside the launcher's control. Fighting that architecture cost
        // three separate failed attempts (subprocess detach, cookie-auth
        // Unauthorized, bearer-auth InvalidSignature — playit's remote API
        // needs undocumented HMAC-signed requests). Given all three cost
        // real debugging time for no working result, this now assumes the
        // official wizard is already installed once (outside the launcher,
        // confirmed to work when run manually) and just asks for the
        // address it already shows.
        // ---------------------------------------------------------------
        new TunnelProvider
        {
            Id                  = "playit",
            DisplayName         = "playit.gg",
            Description         = "Built for game servers. Free, no port-forwarding needed.",
            WebsiteUrl          = "https://playit.gg",
            Tier                = TunnelProviderTier.Free,
            PriceLabel          = "Free",
            SupportsTcp         = true,
            SupportsUdp         = true,
            RequiresApiKey      = false,
            RequiresExternalBinary = false,
            Limitation          = "Vul hieronder handmatig het IP-adres en de poort in die jij/je vrienden gebruiken (bijv. je eigen publiek IP + de poort die je hebt doorgezet).",
            AddressSource       = TunnelAddressSource.Manual,
            CommandTemplate     = ""
        },

        // ---------------------------------------------------------------
        // ngrok — FreemiumLimited: great DX, local API, but NO UDP
        // ---------------------------------------------------------------
        new TunnelProvider
        {
            Id                  = "ngrok",
            DisplayName         = "ngrok",
            Description         = "Reliable TCP tunnels with a built-in local API. No UDP.",
            WebsiteUrl          = "https://ngrok.com",
            Tier                = TunnelProviderTier.FreemiumLimited,
            PriceLabel          = "Free / $8+/mo",
            SupportsTcp         = true,
            SupportsUdp         = false,
            RequiresApiKey      = true,      // user needs an ngrok authtoken
            RequiresExternalBinary = true,
            Limitation          = "No UDP — voice chat and Bedrock won't work through this tunnel.",
            AddressSource       = TunnelAddressSource.LocalApi,
            LocalApiUrl         = "http://127.0.0.1:4040/api/tunnels",
            LocalApiJsonPath    = "tunnels[0].public_url",
            SetupCommandTemplate = "\"{exe}\" config add-authtoken \"{apikey}\"",
            CommandTemplate     = "\"{exe}\" tcp {port}"
        },

        // ---------------------------------------------------------------
        // bore.pub — Free, TCP only, no account needed, open-source
        // ---------------------------------------------------------------
        new TunnelProvider
        {
            Id                  = "bore",
            DisplayName         = "bore.pub",
            Description         = "Lightweight open-source TCP tunnel. No signup required.",
            WebsiteUrl          = "https://github.com/ekzhang/bore",
            Tier                = TunnelProviderTier.Free,
            PriceLabel          = "Free",
            SupportsTcp         = true,
            SupportsUdp         = false,
            RequiresApiKey      = false,
            RequiresExternalBinary = true,
            Limitation          = "No UDP — voice chat and Bedrock won't work through this tunnel.",
            AddressSource       = TunnelAddressSource.StdoutRegex,
            // bore prints: "listening at bore.pub:PORT"
            StdoutPattern       = @"listening at (?<addr>[^\s]+)",
            CommandTemplate     = "\"{exe}\" local {port} --to bore.pub"
        },

        // ---------------------------------------------------------------
        // serveo.net — Free, TCP, SSH-based, no install needed
        // ---------------------------------------------------------------
        new TunnelProvider
        {
            Id                  = "serveo",
            DisplayName         = "serveo.net",
            Description         = "SSH-based tunnel, no client install needed. TCP only.",
            WebsiteUrl          = "https://serveo.net",
            Tier                = TunnelProviderTier.Free,
            PriceLabel          = "Free",
            SupportsTcp         = true,
            SupportsUdp         = false,
            RequiresApiKey      = false,
            RequiresExternalBinary = false,  // uses system ssh.exe
            Limitation          = "No UDP — voice chat and Bedrock won't work through this tunnel.",
            AddressSource       = TunnelAddressSource.StdoutRegex,
            // serveo prints: "Forwarding TCP connections from tcp://serveo.net:PORT"
            StdoutPattern       = @"Forwarding TCP connections from (?<addr>[^\s]+)",
            CommandTemplate     = "ssh -o StrictHostKeyChecking=no -R 0:localhost:{port} serveo.net"
        },

        // ---------------------------------------------------------------
        // frp — Free, self-hosted, TCP + UDP, most powerful/flexible
        // ---------------------------------------------------------------
        new TunnelProvider
        {
            Id                  = "frp",
            DisplayName         = "frp (self-hosted)",
            Description         = "Host on your own VPS. Full TCP + UDP, no third-party limits.",
            WebsiteUrl          = "https://github.com/fatedier/frp",
            Tier                = TunnelProviderTier.Free,
            PriceLabel          = "Free (own VPS)",
            SupportsTcp         = true,
            SupportsUdp         = true,
            RequiresApiKey      = true,      // VPS host + token
            RequiresExternalBinary = true,
            Limitation          = "Requires your own VPS running frps.",
            AddressSource       = TunnelAddressSource.StdoutRegex,
            StdoutPattern       = @"start proxy success.*?remote_port=(?<port>\d+)",
            CommandTemplate     = "\"{exe}\" -c \"{configfile}\""
        },

        // ---------------------------------------------------------------
        // ngrok Pro — Premium, full TCP, stable address
        // ---------------------------------------------------------------
        new TunnelProvider
        {
            Id                  = "ngrok-pro",
            DisplayName         = "ngrok Pro",
            Description         = "Stable reserved domains, higher limits. Still no UDP.",
            WebsiteUrl          = "https://ngrok.com/pricing",
            Tier                = TunnelProviderTier.Premium,
            PriceLabel          = "$20/mo",
            SupportsTcp         = true,
            SupportsUdp         = false,
            RequiresApiKey      = true,
            RequiresExternalBinary = true,
            Limitation          = "No UDP — voice chat and Bedrock won't work through this tunnel.",
            AddressSource       = TunnelAddressSource.LocalApi,
            LocalApiUrl         = "http://127.0.0.1:4040/api/tunnels",
            LocalApiJsonPath    = "tunnels[0].public_url",
            SetupCommandTemplate = "\"{exe}\" config add-authtoken \"{apikey}\"",
            CommandTemplate     = "\"{exe}\" tcp {port}"
        },

        // ---------------------------------------------------------------
        // playit.gg Premium — dedicated IP, custom domain, TCP + UDP
        // ---------------------------------------------------------------
        new TunnelProvider
        {
            Id                  = "playit-premium",
            DisplayName         = "playit.gg Premium",
            Description         = "Dedicated IP, custom domain, no random ports. TCP + UDP.",
            WebsiteUrl          = "https://playit.gg/pricing",
            Tier                = TunnelProviderTier.Premium,
            PriceLabel          = "$3–$6/mo",
            SupportsTcp         = true,
            SupportsUdp         = true,
            RequiresApiKey      = true,
            RequiresExternalBinary = true,
            Limitation          = null,
            AddressSource       = TunnelAddressSource.StdoutRegex,
            // Same broad pattern — premium also uses playit.gg + joinmc.link domains.
            StdoutPattern       = PlayitStdoutPattern,
            CommandTemplate     = "\"{exe}\" --secret \"{apikey}\""
        },
    };

    // -----------------------------------------------------------------------
    // Helper: get providers that can handle a specific protocol, sorted best first
    // -----------------------------------------------------------------------
    public static IReadOnlyList<TunnelProvider> ForProtocol(PortProtocol protocol)
        => All
            .Where(p => p.CanHandle(protocol))
            .OrderByDescending(p => p.RecommendationScore(protocol))
            .ToList();

    /// <summary>Returns the top recommended provider for a port.</summary>
    public static TunnelProvider? GetRecommended(PortProtocol protocol)
        => ForProtocol(protocol).FirstOrDefault();

    public static TunnelProvider? GetById(string id)
        => All.FirstOrDefault(p => p.Id == id);
}