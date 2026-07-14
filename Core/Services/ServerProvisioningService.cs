using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MinecraftControlHub.Core.Models;

namespace MinecraftControlHub.Core.Services;

public interface IServerProvisioningService
{
    Task ProvisionServerAsync(Server server);
    Task WriteStartScriptsAsync(Server server);
}

/// <summary>
/// Handles server folder layout, EULA, start scripts, and jar acquisition.
/// </summary>
public class ServerProvisioningService : IServerProvisioningService
{
    public async Task ProvisionServerAsync(Server server)
    {
        var serverDirectory = Path.Combine(AppPaths.ServersRoot, server.Id.ToString());
        server.ServerDirectory = serverDirectory;

        CreateFolderStructure(serverDirectory);
        await AcceptEulaAsync(serverDirectory);
        await WriteDefaultPropertiesAsync(server);
        await WriteDefaultRolesAsync(server);
        await DownloadServerJarAsync(server);
        await WriteStartScriptsAsync(server);

        if (!HasValidServerJar(server))
        {
            throw new InvalidOperationException(
                $"Failed to provision server '{server.Name}'. Could not download a valid {server.Type} server jar for Minecraft {server.MinecraftVersion}.");
        }
    }

    private static void CreateFolderStructure(string serverDirectory)
    {
        Directory.CreateDirectory(serverDirectory);
        Directory.CreateDirectory(Path.Combine(serverDirectory, "mods"));
        Directory.CreateDirectory(Path.Combine(serverDirectory, "plugins"));
        Directory.CreateDirectory(Path.Combine(serverDirectory, "world"));
        Directory.CreateDirectory(Path.Combine(serverDirectory, "backups"));
        Directory.CreateDirectory(Path.Combine(serverDirectory, "logs"));
    }

    private static async Task AcceptEulaAsync(string serverDirectory)
    {
        var eulaPath = Path.Combine(serverDirectory, "eula.txt");
        await File.WriteAllTextAsync(eulaPath, "# Minecraft Control Hub - EULA accepted automatically\neula=true\n");
    }

    private async Task WriteDefaultPropertiesAsync(Server server)
    {
        var propertiesPath = Path.Combine(server.ServerDirectory!, "server.properties");
        server.PropertiesPath = propertiesPath;
        server.Port ??= 25565;
        server.Port = FindAvailablePort(server.Port.Value);

        var gamemode = string.IsNullOrWhiteSpace(server.Gamemode) ? "survival" : server.Gamemode;
        var difficulty = string.IsNullOrWhiteSpace(server.Difficulty) ? "normal" : server.Difficulty;
        var whiteListEnabled = server.WhiteListEnabled;
        var allowCheats = server.AllowCheats;
        var motd = string.IsNullOrWhiteSpace(server.Motd) ? server.Name : server.Motd;

        var properties = $"""
            # Minecraft server properties
            server-port={server.Port}
            max-players={server.MaxPlayers}
            gamemode={gamemode}
            difficulty={difficulty}
            online-mode={(server.AllowOnlineMode ? "true" : "false")}
            white-list={(whiteListEnabled ? "true" : "false")}
            allow-cheats={(allowCheats ? "true" : "false")}
            motd={motd}
            """;

        await File.WriteAllTextAsync(propertiesPath, properties);
    }

    private async Task WriteDefaultRolesAsync(Server server)
    {
        if (!string.IsNullOrWhiteSpace(server.OpPlayers))
        {
            var opNames = SplitPlayerNames(server.OpPlayers);
            if (opNames.Any())
                await WriteOpsFileAsync(server, opNames);
        }

        if (server.WhiteListEnabled && !string.IsNullOrWhiteSpace(server.WhitelistedPlayers))
        {
            var whitelistNames = SplitPlayerNames(server.WhitelistedPlayers);
            if (whitelistNames.Any())
                await WriteWhitelistFileAsync(server, whitelistNames);
        }
    }

    private static IEnumerable<string> SplitPlayerNames(string names)
    {
        return names
            .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(name => name.Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name));
    }

    private static async Task WriteOpsFileAsync(Server server, IEnumerable<string> names)
    {
        var opsPath = Path.Combine(server.ServerDirectory!, "ops.json");
        var opsEntries = names.Select(name => new
        {
            uuid = GetOfflinePlayerUuid(name).ToString("D"),
            name,
            level = 4,
            bypassesPlayerLimit = false
        }).ToList();

        var options = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(opsPath, JsonSerializer.Serialize(opsEntries, options));
    }

    private static async Task WriteWhitelistFileAsync(Server server, IEnumerable<string> names)
    {
        var whitelistPath = Path.Combine(server.ServerDirectory!, "whitelist.json");
        var whitelistEntries = names.Select(name => new
        {
            uuid = GetOfflinePlayerUuid(name).ToString("D"),
            name
        }).ToList();

        var options = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(whitelistPath, JsonSerializer.Serialize(whitelistEntries, options));
    }

    private static Guid GetOfflinePlayerUuid(string playerName)
    {
        using var md5 = MD5.Create();
        var bytes = Encoding.UTF8.GetBytes($"OfflinePlayer:{playerName}");
        var hash = md5.ComputeHash(bytes);
        hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash);
    }

    public async Task WriteStartScriptsAsync(Server server)
    {
        var directory = server.ServerDirectory!;
        var minMem = server.MinMemoryMB;
        var maxMem = server.MaxMemoryMB;
        var javaCommand = !string.IsNullOrWhiteSpace(server.JavaPath) ? server.JavaPath : "java";
        var customJvmArgs = string.IsNullOrWhiteSpace(server.CustomJvmArgs) ? string.Empty : " " + server.CustomJvmArgs.Trim();

        // For Forge/NeoForge 1.17+, the installer creates run.bat/run.sh.
        // We generate start scripts that call those instead of java -jar.
        var forgeRunBat = Path.Combine(directory, "run.bat");
        var forgeRunSh  = Path.Combine(directory, "run.sh");
        var useForgeRunner = (server.Type is ServerType.Forge or ServerType.NeoForge)
                             && (File.Exists(forgeRunBat) || File.Exists(forgeRunSh));

        string windowsScript;
        string unixScript;

        if (useForgeRunner)
        {
            // Modern Forge: run.bat handles the classpath and launch. We wrap it to set RAM.
            // Set JVM args via env var that the Forge run script respects.
            windowsScript = $"""
                @echo off
                set JAVA_OPTS=-Xms{minMem}M -Xmx{maxMem}M{customJvmArgs}
                call run.bat
                """;
            unixScript = $"""
                #!/bin/bash
                export JAVA_OPTS="-Xms{minMem}M -Xmx{maxMem}M{customJvmArgs}"
                bash run.sh
                """;
        }
        else
        {
            var jarName = server.JarFileName ?? GetExpectedJarFileName(server);
            windowsScript = $"""
                @echo off
                "{javaCommand}" -Xms{minMem}M -Xmx{maxMem}M{customJvmArgs} -jar "{jarName}" nogui
                """;
            unixScript = $"""
                #!/bin/bash
                "{javaCommand}" -Xms{minMem}M -Xmx{maxMem}M{customJvmArgs} -jar "{jarName}" nogui
                """;
        }

        await File.WriteAllTextAsync(Path.Combine(directory, "start.bat"), windowsScript);
        await File.WriteAllTextAsync(Path.Combine(directory, "start.sh"), unixScript);
    }

    private static async Task DownloadServerJarAsync(Server server)
    {
        if (string.IsNullOrWhiteSpace(server.ServerDirectory))
        {
            return;
        }

        try
        {
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromMinutes(5); // Forge metadata + installer download can be slow

            switch (server.Type)
            {
                case ServerType.Vanilla:
                    await DownloadVanillaJarAsync(server, http);
                    break;
                case ServerType.Paper:
                case ServerType.Purpur:
                    await DownloadPaperLikeJarAsync(server, http);
                    break;
                case ServerType.Fabric:
                case ServerType.Quilt:
                    await DownloadFabricLikeJarAsync(server, http);
                    break;
                case ServerType.Forge:
                case ServerType.NeoForge:
                    await DownloadForgeLikeJarAsync(server, http);
                    break;
            }
        }
        catch
        {
            // Swallow provisioning errors and fall back to a stub so the server folder remains usable.
        }

        if (!string.IsNullOrWhiteSpace(server.JarFileName) && File.Exists(Path.Combine(server.ServerDirectory!, server.JarFileName)))
        {
            return;
        }

        // For Forge/NeoForge 1.17+, the installer creates run.bat + libraries/ instead of a jar.
        // If those exist, no placeholder is needed — the server is fully provisioned.
        if (server.Type is ServerType.Forge or ServerType.NeoForge)
        {
            var hasRunScript = File.Exists(Path.Combine(server.ServerDirectory!, "run.bat"))
                            || File.Exists(Path.Combine(server.ServerDirectory!, "run.sh"));
            var hasLibraries = Directory.Exists(Path.Combine(server.ServerDirectory!, "libraries"));
            if (hasRunScript && hasLibraries)
            {
                return;
            }
        }

        var fallbackName = GetExpectedJarFileName(server);
        var fallbackPath = Path.Combine(server.ServerDirectory!, fallbackName);
        if (!File.Exists(fallbackPath))
        {
            await File.WriteAllTextAsync(fallbackPath, $"# Placeholder jar for {server.Type} {server.MinecraftVersion}\n# Real download failed or is not available\n");
        }
        server.JarFileName = Path.GetFileName(fallbackPath);
    }

    private static async Task DownloadVanillaJarAsync(Server server, HttpClient http)
    {
        var manifestUrl = "https://launchermeta.mojang.com/mc/game/version_manifest.json";
        var manifestJson = await http.GetStringAsync(manifestUrl);
        using var manifestDoc = System.Text.Json.JsonDocument.Parse(manifestJson);
        var versions = manifestDoc.RootElement.GetProperty("versions");
        string? versionUrl = null;
        foreach (var version in versions.EnumerateArray())
        {
            if (version.GetProperty("id").GetString() == server.MinecraftVersion)
            {
                versionUrl = version.GetProperty("url").GetString();
                break;
            }
        }
 
        if (string.IsNullOrEmpty(versionUrl))
        {
            return;
        }
 
        var versionJson = await http.GetStringAsync(versionUrl);
        using var verDoc = System.Text.Json.JsonDocument.Parse(versionJson);
        if (verDoc.RootElement.TryGetProperty("downloads", out var downloads) &&
            downloads.TryGetProperty("server", out var serverObj) &&
            serverObj.TryGetProperty("url", out var serverUrlElement))
        {
            var serverUrl = serverUrlElement.GetString();
            if (!string.IsNullOrEmpty(serverUrl))
            {
                await DownloadFileAsync(http, serverUrl, Path.Combine(server.ServerDirectory!, "server.jar"));
                server.JarFileName = "server.jar";
            }
        }
    }
 
    private static async Task EnsureServerJarExistsAsync(Server server, HttpClient http)
    {
        var serverJarPath = Path.Combine(server.ServerDirectory!, "server.jar");
        if (File.Exists(serverJarPath))
            return;
 
        await DownloadVanillaJarAsync(server, http);
    }
 
    private static async Task DownloadPaperLikeJarAsync(Server server, HttpClient http)
    {
        var version = server.MinecraftVersion;
        var project = server.Type == ServerType.Paper ? "paper" : "purpur";
        var versionsUrl = $"https://api.papermc.io/v2/projects/{project}/versions/{version}";
        var versResp = await http.GetAsync(versionsUrl);
        if (!versResp.IsSuccessStatusCode)
        {
            return;
        }

        var versJson = await versResp.Content.ReadAsStringAsync();
        using var versDoc = System.Text.Json.JsonDocument.Parse(versJson);
        if (!versDoc.RootElement.TryGetProperty("builds", out var builds) || builds.GetArrayLength() == 0)
        {
            return;
        }

        var build = builds.EnumerateArray().Last().GetInt32();
        var downloadsUrl = $"https://api.papermc.io/v2/projects/{project}/versions/{version}/builds/{build}/downloads";
        var dlResp = await http.GetAsync(downloadsUrl);
        if (!dlResp.IsSuccessStatusCode)
        {
            return;
        }

        var dlJson = await dlResp.Content.ReadAsStringAsync();
        using var dlDoc = System.Text.Json.JsonDocument.Parse(dlJson);
        if (!dlDoc.RootElement.TryGetProperty("downloads", out var downloads) || !downloads.TryGetProperty("application", out var app))
        {
            return;
        }

        var name = app.GetProperty("name").GetString();
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        var downloadFileUrl = $"https://api.papermc.io/v2/projects/{project}/versions/{version}/builds/{build}/downloads/{name}";
        await DownloadFileAsync(http, downloadFileUrl, Path.Combine(server.ServerDirectory!, name));
        server.JarFileName = name;
    }

    private static async Task DownloadFabricLikeJarAsync(Server server, HttpClient http)
    {
        await DownloadVanillaJarAsync(server, http);
        await EnsureServerJarExistsAsync(server, http);
 
        string loaderVersion;
        string? installerVersion = null;
 
        if (server.Type == ServerType.Fabric)
        {
            var fabricLoaderVersionsUrl = $"https://meta.fabricmc.net/v2/versions/loader/{server.MinecraftVersion}";
            var loaderJson = await http.GetStringAsync(fabricLoaderVersionsUrl);
            using var loaderDoc = System.Text.Json.JsonDocument.Parse(loaderJson);
            loaderVersion = loaderDoc.RootElement.EnumerateArray()
                .Select(entry => new
                {
                    LoaderVersion = entry.GetProperty("loader").GetProperty("version").GetString(),
                    Stable = entry.GetProperty("loader").TryGetProperty("stable", out var stable) && stable.GetBoolean(),
                    Build = entry.GetProperty("loader").TryGetProperty("build", out var build) ? build.GetInt32() : 0
                })
                .Where(entry => entry.Stable && !string.IsNullOrWhiteSpace(entry.LoaderVersion))
                .OrderByDescending(entry => entry.Build)
                .Select(entry => entry.LoaderVersion!)
                .FirstOrDefault() ?? "0.15.0";
 
            installerVersion = await GetLatestFabricInstallerVersionAsync(http);
            if (string.IsNullOrWhiteSpace(installerVersion))
            {
                return;
            }
 
            var installerUrl = $"https://maven.fabricmc.net/net/fabricmc/fabric-installer/{installerVersion}/fabric-installer-{installerVersion}.jar";
            var installerPath = Path.Combine(server.ServerDirectory!, $"fabric-installer-{installerVersion}.jar");
            await DownloadFileAsync(http, installerUrl, installerPath);
 
            var psi = new ProcessStartInfo
            {
                FileName = "java",
                Arguments = $"-jar \"{installerPath}\" server -dir \"{server.ServerDirectory}\" -mcversion \"{server.MinecraftVersion}\" -loader \"{loaderVersion}\" -noprofile",
                WorkingDirectory = server.ServerDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            var proc = Process.Start(psi);
            if (proc != null)
            {
                var stdoutTask = proc.StandardOutput.ReadToEndAsync();
                var stderrTask = proc.StandardError.ReadToEndAsync();
                var exited = proc.WaitForExit(300_000);
                if (!exited)
                {
                    try { proc.Kill(entireProcessTree: true); } catch { }
                }
                try { await stdoutTask; } catch { }
                try { await stderrTask; } catch { }
            }
 
            await EnsureServerJarExistsAsync(server, http);
            var chosenJar = FindBestJar(server.ServerDirectory!);
            server.JarFileName = ChooseLauncherJar(server, chosenJar) ?? "fabric-server-launch.jar";
            return;
        }

        var quiltLoaderVersionsUrl = $"https://meta.quiltmc.org/v3/versions/loader/{server.MinecraftVersion}";
        var quiltLoaderJson = await http.GetStringAsync(quiltLoaderVersionsUrl);
        using var quiltLoaderDoc = System.Text.Json.JsonDocument.Parse(quiltLoaderJson);
        loaderVersion = quiltLoaderDoc.RootElement.EnumerateArray()
            .Select(entry => new
            {
                LoaderVersion = entry.GetProperty("loader").GetProperty("version").GetString(),
                Build = entry.GetProperty("loader").TryGetProperty("build", out var build) ? build.GetInt32() : 0
            })
            .Where(entry => !string.IsNullOrWhiteSpace(entry.LoaderVersion))
            .OrderByDescending(entry => entry.Build)
            .Select(entry => entry.LoaderVersion!)
            .FirstOrDefault() ?? "0.20.0-beta.9";

        installerVersion = await GetLatestQuiltInstallerVersionAsync(http);
        if (string.IsNullOrWhiteSpace(installerVersion))
        {
            return;
        }

        var quiltInstallerUrl = $"https://maven.quiltmc.org/repository/release/org/quiltmc/quilt-installer/{installerVersion}/quilt-installer-{installerVersion}.jar";
        var quiltInstallerPath = Path.Combine(server.ServerDirectory!, $"quilt-installer-{installerVersion}.jar");
        await DownloadFileAsync(http, quiltInstallerUrl, quiltInstallerPath);

        var quiltPsi = new ProcessStartInfo
        {
            FileName = "java",
            Arguments = $"-jar \"{quiltInstallerPath}\" install server --install-dir \"{server.ServerDirectory}\" --mc-version \"{server.MinecraftVersion}\" --loader-version \"{loaderVersion}\"",
            WorkingDirectory = server.ServerDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        var quiltProc = Process.Start(quiltPsi);
        if (quiltProc != null)
        {
            var stdoutTask = quiltProc.StandardOutput.ReadToEndAsync();
            var stderrTask = quiltProc.StandardError.ReadToEndAsync();
            var exited = quiltProc.WaitForExit(300_000);
            if (!exited)
            {
                try { quiltProc.Kill(entireProcessTree: true); } catch { }
            }
            try { await stdoutTask; } catch { }
            try { await stderrTask; } catch { }
        }

        server.JarFileName = FindBestJar(server.ServerDirectory!);
    }

    private static async Task DownloadForgeLikeJarAsync(Server server, HttpClient http)
    {
        var metadataUrl = server.Type == ServerType.Forge
            ? "https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml"
            : "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml";

        var metadata = await http.GetStringAsync(metadataUrl);
        var versions = new List<string>();
        var idx = 0;
        while (true)
        {
            var start = metadata.IndexOf("<version>", idx, StringComparison.OrdinalIgnoreCase);
            if (start == -1) break;
            start += "<version>".Length;
            var end = metadata.IndexOf("</version>", start, StringComparison.OrdinalIgnoreCase);
            if (end == -1) break;
            versions.Add(metadata[start..end].Trim());
            idx = end + "</version>".Length;
        }

        var match = versions
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .OrderByDescending(v => v)
            .FirstOrDefault(v => server.Type == ServerType.Forge
                ? v.StartsWith(server.MinecraftVersion + "-", StringComparison.OrdinalIgnoreCase)
                : v.Contains(server.MinecraftVersion, StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(match))
        {
            return;
        }

        var installerName = server.Type == ServerType.Forge
            ? $"forge-{match}-installer.jar"
            : $"neoforge-{match}-installer.jar";
        var installerUrl = server.Type == ServerType.Forge
            ? $"https://maven.minecraftforge.net/net/minecraftforge/forge/{match}/{installerName}"
            : $"https://maven.neoforged.net/releases/net/neoforged/neoforge/{match}/{installerName}";
        var installerPath = Path.Combine(server.ServerDirectory!, installerName);
        await DownloadFileAsync(http, installerUrl, installerPath);

        var psi = new ProcessStartInfo
        {
            FileName = "java",
            Arguments = $"-jar \"{installerPath}\" --installServer \"{server.ServerDirectory}\"",
            WorkingDirectory = server.ServerDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        var proc = Process.Start(psi);
        if (proc != null)
        {
            // CRITICAL: read stdout/stderr asynchronously to prevent pipe buffer deadlock.
            // If we don't drain the pipes, the child process blocks once the buffer fills (~4 KB).
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();
            var exited = proc.WaitForExit(300_000); // 5 minutes — Forge installer downloads many libraries
            if (!exited)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
            }
            // Await drain tasks so we don't leak background reads.
            try { await stdoutTask; } catch { }
            try { await stderrTask; } catch { }
        }

        server.JarFileName = ChooseLauncherJar(server, FindBestJar(server.ServerDirectory!));
    }

    private static string? ChooseLauncherJar(Server server, string? candidateJar)
    {
        if (string.IsNullOrWhiteSpace(candidateJar))
            return null;
 
        var fileName = candidateJar.ToLowerInvariant();
        if (server.Type == ServerType.Fabric)
        {
            var fabricLaunchPath = Path.Combine(server.ServerDirectory!, "fabric-server-launch.jar");
            if (File.Exists(fabricLaunchPath))
                return "fabric-server-launch.jar";
 
            var fabricLauncherPath = Path.Combine(server.ServerDirectory!, "fabric-server-launcher.jar");
            if (File.Exists(fabricLauncherPath))
                return "fabric-server-launcher.jar";
 
            if (fileName.Contains("fabric"))
                return candidateJar;
 
            var serverJarPath = Path.Combine(server.ServerDirectory!, "server.jar");
            if (File.Exists(serverJarPath))
                return "server.jar";
        }
 
        if (server.Type == ServerType.Quilt)
        {
            var quiltLaunchPath = Path.Combine(server.ServerDirectory!, "quilt-server-launch.jar");
            if (File.Exists(quiltLaunchPath))
                return "quilt-server-launch.jar";
 
            if (fileName.Contains("quilt"))
                return candidateJar;
 
            var serverJarPath = Path.Combine(server.ServerDirectory!, "server.jar");
            if (File.Exists(serverJarPath))
                return "server.jar";
        }
 
        return candidateJar;
    }

    private static async Task<string?> GetLatestFabricInstallerVersionAsync(HttpClient http)
    {
        try
        {
            var metadata = await http.GetStringAsync("https://maven.fabricmc.net/net/fabricmc/fabric-installer/maven-metadata.xml");
            var versions = new List<string>();
            var idx = 0;
            while (true)
            {
                var start = metadata.IndexOf("<version>", idx, StringComparison.OrdinalIgnoreCase);
                if (start == -1) break;
                start += "<version>".Length;
                var end = metadata.IndexOf("</version>", start, StringComparison.OrdinalIgnoreCase);
                if (end == -1) break;
                versions.Add(metadata[start..end].Trim());
                idx = end + "</version>".Length;
            }

            return versions.OrderByDescending(v => v).FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> GetLatestQuiltInstallerVersionAsync(HttpClient http)
    {
        try
        {
            var metadata = await http.GetStringAsync("https://maven.quiltmc.org/repository/release/org/quiltmc/quilt-installer/maven-metadata.xml");
            var versions = new List<string>();
            var idx = 0;
            while (true)
            {
                var start = metadata.IndexOf("<version>", idx, StringComparison.OrdinalIgnoreCase);
                if (start == -1) break;
                start += "<version>".Length;
                var end = metadata.IndexOf("</version>", start, StringComparison.OrdinalIgnoreCase);
                if (end == -1) break;
                versions.Add(metadata[start..end].Trim());
                idx = end + "</version>".Length;
            }

            return versions.OrderByDescending(v => v).FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static async Task DownloadFileAsync(HttpClient http, string url, string destinationPath)
    {
        var response = await http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        await using var file = File.Create(destinationPath);
        await response.Content.CopyToAsync(file);
    }
 
    private static int FindAvailablePort(int startingPort)
    {
        for (var port = startingPort; port <= 65535; port++)
        {
            if (IsPortAvailable(port))
                return port;
        }
 
        return startingPort;
    }
 
    private static bool IsPortAvailable(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            return true;
        }
        catch
        {
            return false;
        }
    }
 
    private static string? FindBestJar(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return null;
        }

        var jars = Directory.GetFiles(directory, "*.jar", SearchOption.TopDirectoryOnly)
            .Where(file => !Path.GetFileName(file).Contains("installer", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (jars.Count == 0)
        {
            return null;
        }

        return jars
            .OrderByDescending(file => new FileInfo(file).Length)
            .Select(Path.GetFileName)
            .FirstOrDefault();
    }

    private static bool HasValidServerJar(Server server)
    {
        if (string.IsNullOrWhiteSpace(server.ServerDirectory) || !Directory.Exists(server.ServerDirectory))
            return false;

        // For Forge/NeoForge 1.17+, the installer creates run.bat/run.sh + libraries/ folder
        // instead of a simple server jar. Accept these as valid provisioning.
        if (server.Type is ServerType.Forge or ServerType.NeoForge)
        {
            var runBat = Path.Combine(server.ServerDirectory, "run.bat");
            var runSh  = Path.Combine(server.ServerDirectory, "run.sh");
            var libs   = Path.Combine(server.ServerDirectory, "libraries");
            if ((File.Exists(runBat) || File.Exists(runSh)) && Directory.Exists(libs))
                return true;
        }

        string? jarPath = null;
        if (!string.IsNullOrWhiteSpace(server.JarFileName))
        {
            jarPath = Path.Combine(server.ServerDirectory, server.JarFileName);
            if (File.Exists(jarPath) && IsValidJar(jarPath))
                return true;
        }

        jarPath = FindBestJar(server.ServerDirectory);
        return jarPath != null && IsValidJar(Path.Combine(server.ServerDirectory, jarPath));
    }

    private static bool IsValidJar(string jarPath)
    {
        if (!File.Exists(jarPath))
            return false;

        try
        {
            using var stream = File.OpenRead(jarPath);
            var buffer = new byte[2];
            if (stream.Read(buffer, 0, 2) == 2)
            {
                return buffer[0] == 'P' && buffer[1] == 'K';
            }
        }
        catch
        {
            // If the file cannot be read, it is not a valid jar.
        }

        return false;
    }

    private static string GetExpectedJarFileName(Server server) =>
        server.Type switch
        {
            ServerType.Vanilla => "server.jar",
            ServerType.Paper or ServerType.Purpur => $"paper-{server.MinecraftVersion}.jar",
            ServerType.Fabric => "fabric-server-launch.jar",
            ServerType.Quilt => "quilt-server-launch.jar",
            ServerType.Forge or ServerType.NeoForge => $"{server.Type.ToString().ToLower()}-{server.MinecraftVersion}.jar",
            _ => $"server-{server.MinecraftVersion}.jar"
        };
}
