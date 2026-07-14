using System.IO;
using MinecraftControlHub.Core.Models;

namespace MinecraftControlHub.Core.Services;

public interface IPortScanService
{
    /// <summary>
    /// Scans the given server directory and returns all detected ports.
    /// Vanilla ports come from server.properties; mod/plugin ports are detected
    /// by looking for known jar file patterns and reading their config files.
    /// When a config file doesn't exist yet (mod never started), the default port
    /// is returned with Source set to indicate it's a default value.
    /// </summary>
    Task<List<PortInfo>> ScanAsync(Server server);
}

public class PortScanService : IPortScanService
{
    // -----------------------------------------------------------------------
    // Mod / plugin descriptor: everything we need to know about one mod's port.
    // -----------------------------------------------------------------------
    private sealed record ModPortDescriptor(
        string Name,
        PortProtocol Protocol,
        int DefaultPort,
        // Glob patterns (case-insensitive) to detect the jar in mods/ or plugins/
        IReadOnlyList<string> JarPatterns,
        // Config paths relative to the server directory, in order of preference
        IReadOnlyList<string> ConfigPaths,
        // Function that tries to extract the port number from the config file text.
        // Returns null when nothing usable is found (falls back to DefaultPort).
        Func<string, int?> ExtractPort
    );

    // -----------------------------------------------------------------------
    // Registry of known mods / plugins
    // -----------------------------------------------------------------------
    private static readonly IReadOnlyList<ModPortDescriptor> KnownMods = new[]
    {
        // Simple Voice Chat — Fabric / Forge variant
        new ModPortDescriptor(
            Name:         "Simple Voice Chat",
            Protocol:     PortProtocol.UDP,
            DefaultPort:  24454,
            JarPatterns:  new[] { "voicechat-*", "simple-voice-chat-*" },
            ConfigPaths:  new[] { Path.Combine("config", "voicechat-server.properties") },
            ExtractPort:  text =>
            {
                foreach (var line in text.Split('\n'))
                {
                    var t = line.Trim();
                    if (t.StartsWith("port=", StringComparison.OrdinalIgnoreCase) &&
                        int.TryParse(t[5..].Split('#')[0].Trim(), out var p))
                        return p;
                }
                return null;
            }
        ),

        // Simple Voice Chat — Paper / Spigot plugin variant
        new ModPortDescriptor(
            Name:         "Simple Voice Chat",
            Protocol:     PortProtocol.UDP,
            DefaultPort:  24454,
            JarPatterns:  new[] { "voicechat-bukkit-*", "voicechat-paper-*" },
            ConfigPaths:  new[] { Path.Combine("plugins", "voicechat", "config.yml") },
            ExtractPort:  text =>
            {
                foreach (var line in text.Split('\n'))
                {
                    var t = line.Trim();
                    if (t.StartsWith("port:", StringComparison.OrdinalIgnoreCase) &&
                        int.TryParse(t[5..].Split('#')[0].Trim(), out var p))
                        return p;
                }
                return null;
            }
        ),

        // Geyser (Bedrock bridge)
        new ModPortDescriptor(
            Name:         "Geyser (Bedrock)",
            Protocol:     PortProtocol.UDP,
            DefaultPort:  19132,
            JarPatterns:  new[] { "Geyser-*", "geyser-*" },
            ConfigPaths:  new[]
            {
                Path.Combine("plugins", "Geyser-Spigot",  "config.yml"),
                Path.Combine("plugins", "Geyser-Paper",   "config.yml"),
                Path.Combine("plugins", "Geyser-Fabric",  "config.yml"),
                Path.Combine("mods",    "Geyser-Fabric",  "config.yml"),
                Path.Combine("config",  "Geyser-Fabric",  "config.yml"),
            },
            ExtractPort:  text =>
            {
                // YAML: "  port: 19132" under the "bedrock:" section
                bool inBedrock = false;
                foreach (var line in text.Split('\n'))
                {
                    var t = line.TrimEnd();
                    if (t.TrimStart().StartsWith("bedrock:", StringComparison.OrdinalIgnoreCase))
                    {
                        inBedrock = true;
                        continue;
                    }
                    if (inBedrock)
                    {
                        // A new non-indented key ends the section
                        if (t.Length > 0 && !char.IsWhiteSpace(t[0]) && !t.TrimStart().StartsWith("port:"))
                            inBedrock = false;

                        var stripped = t.Trim();
                        if (stripped.StartsWith("port:", StringComparison.OrdinalIgnoreCase) &&
                            int.TryParse(stripped[5..].Split('#')[0].Trim(), out var p))
                            return p;
                    }
                }
                return null;
            }
        ),

        // Dynmap
        new ModPortDescriptor(
            Name:         "Dynmap",
            Protocol:     PortProtocol.TCP,
            DefaultPort:  8123,
            JarPatterns:  new[] { "dynmap-*", "Dynmap-*" },
            ConfigPaths:  new[] { Path.Combine("plugins", "dynmap", "configuration.txt") },
            ExtractPort:  text =>
            {
                foreach (var line in text.Split('\n'))
                {
                    var t = line.Trim();
                    if (t.StartsWith("webserver-port:", StringComparison.OrdinalIgnoreCase) ||
                        t.StartsWith("webserver-port=", StringComparison.OrdinalIgnoreCase))
                    {
                        var raw = t.Contains('=') ? t.Split('=')[1] : t.Split(':')[1];
                        if (int.TryParse(raw.Split('#')[0].Trim(), out var p)) return p;
                    }
                }
                return null;
            }
        ),

        // BlueMap
        new ModPortDescriptor(
            Name:         "BlueMap",
            Protocol:     PortProtocol.TCP,
            DefaultPort:  8100,
            JarPatterns:  new[] { "BlueMap-*", "bluemap-*" },
            ConfigPaths:  new[] { Path.Combine("bluemap", "webapp.conf") },
            ExtractPort:  text =>
            {
                foreach (var line in text.Split('\n'))
                {
                    var t = line.Trim();
                    // HOCON-ish: "port: 8100" or "port=8100"
                    if (t.StartsWith("port:", StringComparison.OrdinalIgnoreCase) ||
                        t.StartsWith("port=", StringComparison.OrdinalIgnoreCase))
                    {
                        var raw = t.Contains('=') ? t.Split('=')[1] : t.Split(':')[1];
                        if (int.TryParse(raw.Split('#')[0].Trim(), out var p)) return p;
                    }
                }
                return null;
            }
        ),

        // Plan (Player Analytics)
        new ModPortDescriptor(
            Name:         "Plan (Analytics)",
            Protocol:     PortProtocol.TCP,
            DefaultPort:  8804,
            JarPatterns:  new[] { "Plan-*", "plan-*" },
            ConfigPaths:  new[] { Path.Combine("plugins", "Plan", "config.yml") },
            ExtractPort:  text =>
            {
                // Nested YAML:
                //   Webserver:
                //     Port: 8804
                bool inWebserver = false;
                foreach (var line in text.Split('\n'))
                {
                    var t = line.TrimEnd();
                    if (t.TrimStart().StartsWith("Webserver:", StringComparison.OrdinalIgnoreCase) ||
                        t.TrimStart().StartsWith("webserver:", StringComparison.OrdinalIgnoreCase))
                    {
                        inWebserver = true;
                        continue;
                    }
                    if (inWebserver)
                    {
                        if (t.Length > 0 && !char.IsWhiteSpace(t[0]))
                            inWebserver = false;

                        var stripped = t.Trim();
                        if ((stripped.StartsWith("Port:", StringComparison.OrdinalIgnoreCase)) &&
                            int.TryParse(stripped[5..].Split('#')[0].Trim(), out var p))
                            return p;
                    }
                }
                return null;
            }
        ),
    };

    // -----------------------------------------------------------------------
    // Public scan entry point
    // -----------------------------------------------------------------------
    public Task<List<PortInfo>> ScanAsync(Server server)
        => Task.Run(() => Scan(server));

    private static List<PortInfo> Scan(Server server)
    {
        var ports = new List<PortInfo>();

        if (string.IsNullOrWhiteSpace(server.ServerDirectory) ||
            !Directory.Exists(server.ServerDirectory))
            return ports;

        var serverDir = server.ServerDirectory!;

        // 1. Vanilla ports from server.properties
        ScanServerProperties(serverDir, ports);

        // 2. Known mod / plugin ports
        ScanModPorts(serverDir, ports);

        return ports;
    }

    // -----------------------------------------------------------------------
    // Step 1: server.properties
    // -----------------------------------------------------------------------
    private static void ScanServerProperties(string serverDir, List<PortInfo> ports)
    {
        var propertiesPath = Path.Combine(serverDir, "server.properties");
        if (!File.Exists(propertiesPath))
            return;

        var lines = File.ReadAllLines(propertiesPath);

        int port = 25565, queryPort = 25565, rconPort = 25575;
        bool enableQuery = false, enableRcon = false;

        foreach (var line in lines)
        {
            if (line.StartsWith('#')) continue;

            if (TryParseProperty(line, "server-port", out var v) && int.TryParse(v, out var sp))
                port = sp;
            else if (TryParseProperty(line, "query.port", out v) && int.TryParse(v, out var qp))
                queryPort = qp;
            else if (TryParseProperty(line, "rcon.port", out v) && int.TryParse(v, out var rp))
                rconPort = rp;
            else if (TryParseProperty(line, "enable-query", out v) && bool.TryParse(v, out var eq))
                enableQuery = eq;
            else if (TryParseProperty(line, "enable-rcon", out v) && bool.TryParse(v, out var er))
                enableRcon = er;
        }

        // Main server port — always present
        ports.Add(new PortInfo
        {
            Port      = port,
            Protocol  = PortProtocol.TCP,
            Purpose   = "Minecraft Server",
            IsEnabled = true,
            IsCustom  = false,
            Source    = "server.properties"
        });

        // Query port — only when explicitly enabled
        if (enableQuery)
            ports.Add(new PortInfo
            {
                Port      = queryPort,
                Protocol  = PortProtocol.UDP,
                Purpose   = "Server Query",
                IsEnabled = true,
                IsCustom  = false,
                Source    = "server.properties"
            });

        // RCON port — only when explicitly enabled
        if (enableRcon)
            ports.Add(new PortInfo
            {
                Port      = rconPort,
                Protocol  = PortProtocol.TCP,
                Purpose   = "RCON",
                IsEnabled = true,
                IsCustom  = false,
                Source    = "server.properties"
            });
    }

    private static bool TryParseProperty(string line, string key, out string value)
    {
        value = string.Empty;
        var prefix = key + "=";
        if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        value = line[prefix.Length..].Trim();
        return true;
    }

    // -----------------------------------------------------------------------
    // Step 2: mod / plugin ports
    // -----------------------------------------------------------------------
    private static void ScanModPorts(string serverDir, List<PortInfo> ports)
    {
        // Collect all jar file names once (mods/ + plugins/ dirs)
        var installedJars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var subDir in new[] { "mods", "plugins" })
        {
            var dir = Path.Combine(serverDir, subDir);
            if (Directory.Exists(dir))
                foreach (var f in Directory.EnumerateFiles(dir, "*.jar", SearchOption.TopDirectoryOnly))
                    installedJars.Add(Path.GetFileName(f));
        }

        // We track which mods we've already emitted so the two Voice Chat
        // descriptors don't both fire when only one variant is present.
        var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var mod in KnownMods)
        {
            // Skip if we already emitted this mod name
            if (emitted.Contains(mod.Name)) continue;

            // Check if any of the jar patterns match an installed jar
            bool jarFound = mod.JarPatterns.Any(pattern =>
                installedJars.Any(jar => MatchesGlob(jar, pattern)));

            if (!jarFound) continue;

            // Find the first existing config, or fall through to "never started"
            string? configText = null;
            string? usedConfigPath = null;

            foreach (var rel in mod.ConfigPaths)
            {
                var full = Path.Combine(serverDir, rel);
                if (File.Exists(full))
                {
                    configText = File.ReadAllText(full);
                    usedConfigPath = rel;
                    break;
                }
            }

            int resolvedPort;
            string source;

            if (configText != null)
            {
                var extracted = mod.ExtractPort(configText);
                resolvedPort = extracted ?? mod.DefaultPort;
                source = $"mod:{mod.Name}";
            }
            else
            {
                // Config doesn't exist yet (mod never started)
                resolvedPort = mod.DefaultPort;
                source = $"mod:{mod.Name} (default — not started yet)";
            }

            ports.Add(new PortInfo
            {
                Port      = resolvedPort,
                Protocol  = mod.Protocol,
                Purpose   = mod.Name,
                IsEnabled = true,
                IsCustom  = false,
                Source    = source
            });

            emitted.Add(mod.Name);
        }
    }

    // -----------------------------------------------------------------------
    // Minimal glob matching: supports leading/trailing/mid '*' wildcards.
    // e.g. "voicechat-*" matches "voicechat-fabric-2.5.26+mc1.20.4.jar"
    // -----------------------------------------------------------------------
    private static bool MatchesGlob(string fileName, string pattern)
    {
        // Strip .jar extension for comparison so patterns don't need it
        var name    = Path.GetFileNameWithoutExtension(fileName);
        var pat     = pattern.TrimEnd('*').TrimEnd('-');

        // Simple prefix match (all our patterns are "prefix-*")
        return name.StartsWith(pat, StringComparison.OrdinalIgnoreCase);
    }
}
