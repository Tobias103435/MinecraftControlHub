using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using MinecraftControlHub.Core.Models;

namespace MinecraftControlHub.Core.Services;

public interface IServerService
{
    Task<List<Server>> GetAllServersAsync();
    Task<Server?> GetServerAsync(Guid id);
    Task<Server> CreateServerAsync(Server server);
    Task UpdateServerAsync(Server server);
    Task UpdateServerPropertiesAsync(Server server);
    Task DeleteServerAsync(Guid id);
    Task StartServerAsync(Guid id);
    Task StopServerAsync(Guid id);
    Task SendServerCommandAsync(Guid id, string command);
    event EventHandler<ServerConsoleOutputEventArgs>? ServerOutputReceived;
    event EventHandler<ServerCrashedEventArgs>? ServerCrashed;
    event EventHandler? ServersChanged;
}

public class ServerService : IServerService
{
    private readonly List<Server> _servers;
    private readonly IServerProvisioningService _provisioningService;
    private readonly Dictionary<Guid, Process> _processes = new();
    private readonly Dictionary<Guid, StreamWriter> _processInputs = new();
    private readonly string _serversStatePath;
    private readonly string _oldServersStatePath;
    private readonly string _oldServersRoot;

    public ServerService(IServerProvisioningService provisioningService)
    {
        _provisioningService = provisioningService;
        _servers = new List<Server>();
        var rootPath = AppPaths.ServersRoot;
        _serversStatePath = Path.Combine(rootPath, "servers.json");
        _oldServersRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MinecraftControlHub", "Servers");
        _oldServersStatePath = Path.Combine(_oldServersRoot, "servers.json");
        LoadServersFromDisk();
    }

    public Task<List<Server>> GetAllServersAsync()
    {
        return Task.FromResult(_servers.ToList());
    }

    public Task<Server?> GetServerAsync(Guid id)
    {
        var server = _servers.FirstOrDefault(s => s.Id == id);
        return Task.FromResult(server);
    }

    public async Task<Server> CreateServerAsync(Server server)
    {
        server.CreatedAt = DateTime.UtcNow;
        server.Status = ServerStatus.Provisioning;
        server.TunnelEnabled ??= false;

        await _provisioningService.ProvisionServerAsync(server);

        server.Status = ServerStatus.Stopped;
        _servers.Add(server);
        SaveServersToDisk();
        try { ServersChanged?.Invoke(this, EventArgs.Empty); } catch { }
        return server;
    }

    public event EventHandler<ServerConsoleOutputEventArgs>? ServerOutputReceived;
    public event EventHandler<ServerCrashedEventArgs>? ServerCrashed;
    public event EventHandler? ServersChanged;

    public Task UpdateServerAsync(Server server)
    {
        var existing = _servers.FirstOrDefault(s => s.Id == server.Id);
        if (existing != null)
        {
            var index = _servers.IndexOf(existing);
            _servers[index] = server;
            SaveServersToDisk();
        }
        return Task.CompletedTask;
    }

    public Task UpdateServerPropertiesAsync(Server server)
    {
        if (string.IsNullOrWhiteSpace(server.ServerDirectory))
            return Task.CompletedTask;

        var propertiesPath = Path.Combine(server.ServerDirectory, "server.properties");
        var lines = File.Exists(propertiesPath) ? File.ReadAllLines(propertiesPath).ToList() : new List<string>();

        void SetProperty(string key, string? value)
        {
            var index = lines.FindIndex(line => line.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                lines[index] = $"{key}={value}";
            }
            else
            {
                lines.Add($"{key}={value}");
            }
        }

        server.Port ??= 25565;
        SetProperty("server-port", server.Port!.ToString());
        SetProperty("max-players", server.MaxPlayers.ToString());
        SetProperty("gamemode", string.IsNullOrWhiteSpace(server.Gamemode) ? "survival" : server.Gamemode);
        SetProperty("difficulty", string.IsNullOrWhiteSpace(server.Difficulty) ? "normal" : server.Difficulty);
        SetProperty("online-mode", server.AllowOnlineMode ? "true" : "false");
        SetProperty("white-list", server.WhiteListEnabled ? "true" : "false");
        SetProperty("allow-cheats", server.AllowCheats ? "true" : "false");
        SetProperty("motd", string.IsNullOrWhiteSpace(server.Motd) ? server.Name : server.Motd);
        SetProperty("view-distance", server.ViewDistance.ToString());
        SetProperty("simulation-distance", server.SimulationDistance.ToString());
        SetProperty("pvp", server.PvpEnabled ? "true" : "false");
        SetProperty("spawn-protection", server.SpawnProtection.ToString());
        SetProperty("allow-nether", server.AllowNether ? "true" : "false");
        SetProperty("allow-flight", server.AllowFlight ? "true" : "false");
        SetProperty("hardcore", server.HardcoreMode ? "true" : "false");
        SetProperty("force-gamemode", server.ForceGamemode ? "true" : "false");
        SetProperty("op-permission-level", server.OpPermissionLevel.ToString());
        SetProperty("network-compression-threshold", server.NetworkCompressionThreshold.ToString());
        SetProperty("player-idle-timeout", server.PlayerIdleTimeout.ToString());
        SetProperty("enable-command-block", server.EnableCommandBlocks ? "true" : "false");
        SetProperty("spawn-animals", server.SpawnAnimals ? "true" : "false");
        SetProperty("spawn-monsters", server.SpawnMonsters ? "true" : "false");
        SetProperty("spawn-npcs", server.SpawnNpcs ? "true" : "false");

        File.WriteAllLines(propertiesPath, lines);
        UpdateServerRoleFiles(server);
        SaveServersToDisk();

        return Task.CompletedTask;
    }

    public Task DeleteServerAsync(Guid id)
    {
        var server = _servers.FirstOrDefault(s => s.Id == id);
        if (server != null)
        {
            // stop running process if present
            if (_processes.TryGetValue(server.Id, out var proc))
            {
                try { if (!proc.HasExited) proc.Kill(); } catch { }
                _processes.Remove(server.Id);
            }

            if (!string.IsNullOrWhiteSpace(server.ServerDirectory) && Directory.Exists(server.ServerDirectory))
            {
                try
                {
                    Directory.Delete(server.ServerDirectory, recursive: true);
                }
                catch
                {
                    // best effort: if the directory cannot be deleted, keep going.
                }
            }

            _servers.Remove(server);
            SaveServersToDisk();
            try { ServersChanged?.Invoke(this, EventArgs.Empty); } catch { }
        }
        return Task.CompletedTask;
    }

    public Task StartServerAsync(Guid id)
    {
        var server = _servers.FirstOrDefault(s => s.Id == id);
        if (server == null)
            return Task.CompletedTask;
 
        if (server.ServerDirectory == null || !Directory.Exists(server.ServerDirectory))
        {
            throw new DirectoryNotFoundException("Server directory not found.");
        }
 
        var serverDirectory = server.ServerDirectory;
        var startBat = Path.Combine(serverDirectory, "start.bat");
        if (!File.Exists(startBat))
        {
            throw new FileNotFoundException("Start script not found.", startBat);
        }
 
        EnsureEulaAccepted(serverDirectory);
 
        if (server.Port == null)
        {
            server.Port = 25565;
        }
 
        var availablePort = FindAvailablePort(server.Port.Value);
        if (availablePort != server.Port.Value)
        {
            server.Port = availablePort;
            UpdateServerPropertiesPort(server);
        }

        // If already running, ignore
        if (server.Status == ServerStatus.Running)
            return Task.CompletedTask;

        // If stuck at Starting from a previous failed attempt, force-stop first
        if (server.Status == ServerStatus.Starting)
        {
            if (_processes.TryGetValue(server.Id, out var oldProc))
            {
                try
                {
                    if (!oldProc.HasExited)
                        oldProc.Kill(entireProcessTree: true);
                }
                catch { }
                _processes.Remove(server.Id);
                _processInputs.Remove(server.Id);
            }
            server.Status = ServerStatus.Stopped;
        }

        server.Status = ServerStatus.Starting;
        server.LastStarted = DateTime.UtcNow;
        SaveServersToDisk();

        var jarFileName = server.JarFileName;
        var jarPath = Path.Combine(serverDirectory, jarFileName ?? "server.jar");
        if (!File.Exists(jarPath))
        {
            var jarFiles = Directory.GetFiles(serverDirectory, "*.jar", SearchOption.TopDirectoryOnly)
                .Where(file => !Path.GetFileName(file).Contains("installer", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(file => new FileInfo(file).Length)
                .Select(Path.GetFileName)
                .ToList();
            if (jarFiles.Count > 0)
            {
                jarFileName = jarFiles[0];
                server.JarFileName = jarFileName;
                jarPath = Path.Combine(serverDirectory, jarFileName!);
            }
        }
 
        if ((server.Type == ServerType.Fabric || server.Type == ServerType.Quilt) &&
            jarFileName != null && string.Equals(jarFileName, "server.jar", StringComparison.OrdinalIgnoreCase))
        {
            var alternateJar = server.Type == ServerType.Fabric ? "fabric-server-launch.jar" : "quilt-server-launch.jar";
            var alternatePath = Path.Combine(serverDirectory, alternateJar);
            if (File.Exists(alternatePath))
            {
                jarFileName = alternateJar;
                server.JarFileName = jarFileName;
                jarPath = alternatePath;
            }
        }
 
        var javaExecutable = !string.IsNullOrWhiteSpace(server.JavaPath) && File.Exists(server.JavaPath)
            ? server.JavaPath
            : "java";
        var customJvmArgs = string.IsNullOrWhiteSpace(server.CustomJvmArgs) ? string.Empty : " " + server.CustomJvmArgs.Trim();

        ProcessStartInfo psi;

        // For Forge/NeoForge 1.17+, the server uses run.bat instead of java -jar.
        // start.bat sets JAVA_OPTS and calls run.bat which handles the classpath.
        var forgeStartBat = Path.Combine(serverDirectory, "start.bat");
        var forgeRunBat = Path.Combine(serverDirectory, "run.bat");
        if ((server.Type == ServerType.Forge || server.Type == ServerType.NeoForge) &&
            File.Exists(forgeStartBat) && File.Exists(forgeRunBat))
        {
            psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{forgeStartBat}\"",
                WorkingDirectory = server.ServerDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true
            };
        }
        else
        {
            psi = new ProcessStartInfo
            {
                FileName = javaExecutable,
                Arguments = $"-Xms{server.MinMemoryMB}M -Xmx{server.MaxMemoryMB}M{customJvmArgs} -jar \"{jarPath}\" nogui",
                WorkingDirectory = server.ServerDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true
            };
        }

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        process.OutputDataReceived += (s, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                UpdateServerPlayerCountFromLog(server, e.Data);
                ServerOutputReceived?.Invoke(this, new ServerConsoleOutputEventArgs(server.Id, e.Data, false));
            }
        };
        process.ErrorDataReceived += (s, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                UpdateServerPlayerCountFromLog(server, e.Data);
                ServerOutputReceived?.Invoke(this, new ServerConsoleOutputEventArgs(server.Id, e.Data, true));
            }
        };
        process.Exited += (s, e) =>
        {
            try
            {
                // If exit code is non-zero, consider it a crash and raise an event
                int exitCode = -1;
                try { exitCode = process.ExitCode; } catch { }

                if (exitCode != 0)
                {
                    // try to locate a crash report in server logs
                    string? crashPath = null;
                    string? crashReport = null;
                    try
                    {
                        var logDir = Path.Combine(server.ServerDirectory ?? string.Empty, "logs");
                        if (Directory.Exists(logDir))
                        {
                            var crashes = Directory.GetFiles(logDir, "*crash*.log").OrderByDescending(f => File.GetLastWriteTime(f)).ToList();
                            if (crashes.Count > 0)
                            {
                                crashPath = crashes.First();
                                try { crashReport = File.ReadAllText(crashPath); } catch { crashReport = null; }
                            }
                        }
                    }
                    catch { }

                    try
                    {
                        ServerCrashed?.Invoke(this, new ServerCrashedEventArgs(server, crashPath, crashReport));
                    }
                    catch { }
                }
            }
            catch { }
            finally
            {
                server.Status = ServerStatus.Stopped;
                server.LoadPercent = 0;
                server.CurrentPlayers = 0;
                _processes.Remove(server.Id);
                SaveServersToDisk();
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (process.StandardInput != null)
        {
            _processInputs[server.Id] = process.StandardInput;
        }

        _processes[server.Id] = process;

        // Simulate startup/load progress in background
        _ = Task.Run(async () =>
        {
            try
            {
                for (int i = 0; i <= 10; i++)
                {
                    if (process.HasExited)
                        break;
                    server.LoadPercent = i * 10;
                    SaveServersToDisk();
                    await Task.Delay(200);
                }

                        if (!process.HasExited)
                            server.Status = ServerStatus.Running;

                        server.CurrentPlayers = 0;
                        server.LoadPercent = 100;
                        SaveServersToDisk();
            }
            catch { }
        });

        return Task.CompletedTask;
    }

    public Task StopServerAsync(Guid id)
    {
        var server = _servers.FirstOrDefault(s => s.Id == id);
        if (server == null)
            return Task.CompletedTask;

        if (_processes.TryGetValue(server.Id, out var proc))
        {
            try
            {
                server.Status = ServerStatus.Stopping;
                if (!proc.HasExited)
                {
                    if (_processInputs.TryGetValue(server.Id, out var input))
                    {
                        try
                        {
                            input.WriteLine("stop");
                            input.Flush();
                            proc.WaitForExit(5000);
                        }
                        catch { }
                    }

                    if (!proc.HasExited)
                    {
                        proc.Kill(true);
                        proc.WaitForExit(3000);
                    }
                }
            }
            catch { }
            finally
            {
                server.Status = ServerStatus.Stopped;
                server.LoadPercent = 0;
                server.CurrentPlayers = 0;
                _processes.Remove(server.Id);
                _processInputs.Remove(server.Id);
                SaveServersToDisk();
            }
        }
        else
        {
            server.Status = ServerStatus.Stopped;
            server.LoadPercent = 0;
            server.CurrentPlayers = 0;
            SaveServersToDisk();
        }

        return Task.CompletedTask;
    }

    public Task SendServerCommandAsync(Guid id, string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return Task.CompletedTask;

        if (_processes.TryGetValue(id, out var process) && !process.HasExited && _processInputs.TryGetValue(id, out var input))
        {
            try
            {
                input.WriteLine(command);
                input.Flush();
            }
            catch { }
        }

        return Task.CompletedTask;
    }

    private void LoadServersFromDisk()
    {
        var existingDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var stateFiles = new[] { _serversStatePath, _oldServersStatePath };
            foreach (var stateFile in stateFiles)
            {
                if (!File.Exists(stateFile))
                    continue;

                var json = File.ReadAllText(stateFile);
                var persistedServers = JsonSerializer.Deserialize<List<Server>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (persistedServers == null)
                    continue;

                foreach (var server in persistedServers)
                {
                    if (server.Id == Guid.Empty)
                    {
                        server.Id = Guid.NewGuid();
                    }

                    if (string.IsNullOrWhiteSpace(server.ServerDirectory))
                    {
                        server.ServerDirectory = Path.Combine(Path.GetDirectoryName(stateFile)!, server.Id.ToString());
                    }

                    if (server.ServerDirectory != null)
                    {
                        var normalizedPath = NormalizePath(server.ServerDirectory);
                        if (Directory.Exists(normalizedPath) && IsServerDirectory(normalizedPath) && !existingDirectories.Contains(normalizedPath))
                        {
                            _servers.Add(server);
                            existingDirectories.Add(normalizedPath);
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignore malformed persisted state and fall back to discovering folders.
        }

        DiscoverServersFromDisk(AppPaths.ServersRoot, existingDirectories);
        DiscoverServersFromDisk(_oldServersRoot, existingDirectories);
    }

    private void DiscoverServersFromDisk(string rootPath, HashSet<string> existingDirectories)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            return;
        }

        foreach (var directory in Directory.GetDirectories(rootPath))
        {
            var normalizedPath = NormalizePath(directory);
            if (existingDirectories.Contains(normalizedPath))
            {
                continue;
            }

            if (!IsServerDirectory(normalizedPath))
            {
                continue;
            }

            var folderName = Path.GetFileName(normalizedPath);
            if (!Guid.TryParse(folderName, out var serverId))
            {
                serverId = Guid.NewGuid();
            }

            var discoveredServer = new Server
            {
                Id = serverId,
                Name = folderName,
                ServerDirectory = normalizedPath,
                Status = ServerStatus.Stopped,
                CreatedAt = Directory.GetCreationTimeUtc(normalizedPath),
                AllowOnlineMode = true,
                MaxPlayers = 10,
                Gamemode = "survival",
                Difficulty = "normal"
            };

            var propertiesPath = Path.Combine(normalizedPath, "server.properties");
            if (File.Exists(propertiesPath))
            {
                foreach (var line in File.ReadAllLines(propertiesPath))
                {
                    if (line.StartsWith("motd=", StringComparison.OrdinalIgnoreCase))
                    {
                        discoveredServer.Name = line[5..].Trim();
                        discoveredServer.Motd = discoveredServer.Name;
                    }
                    else if (line.StartsWith("max-players=", StringComparison.OrdinalIgnoreCase) && int.TryParse(line[12..], out var maxPlayers))
                    {
                        discoveredServer.MaxPlayers = maxPlayers;
                    }
                    else if (line.StartsWith("online-mode=", StringComparison.OrdinalIgnoreCase) && bool.TryParse(line[12..], out var onlineMode))
                    {
                        discoveredServer.AllowOnlineMode = onlineMode;
                    }
                    else if (line.StartsWith("server-port=", StringComparison.OrdinalIgnoreCase) && int.TryParse(line[13..], out var port))
                    {
                        discoveredServer.Port = port;
                    }
                }
            }

            var jarFiles = Directory.GetFiles(normalizedPath, "*.jar", SearchOption.TopDirectoryOnly)
                .OrderByDescending(file => new FileInfo(file).Length)
                .ToList();
            if (jarFiles.Count > 0)
            {
                discoveredServer.JarFileName = Path.GetFileName(ChooseBestJar(jarFiles));
                discoveredServer.Type = DetectServerType(jarFiles, discoveredServer.Type);
            }

            if (string.IsNullOrWhiteSpace(discoveredServer.Name) || Guid.TryParse(discoveredServer.Name, out _))
            {
                discoveredServer.Name = ChooseFriendlyServerName(folderName, discoveredServer.Motd, jarFiles);
            }

            _servers.Add(discoveredServer);
            existingDirectories.Add(normalizedPath);
        }

        SaveServersToDisk();
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path ?? string.Empty;

        var normalized = Path.GetFullPath(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return normalized;
    }

    private static bool IsServerDirectory(string directory)
    {
        if (!Directory.Exists(directory))
            return false;

        var hasJar = Directory.GetFiles(directory, "*.jar", SearchOption.TopDirectoryOnly).Any();
        if (!hasJar)
            return false;

        var requiredMarkers = new[] { "server.properties", "start.bat", "start.sh", "eula.txt" };
        return requiredMarkers.Any(marker => File.Exists(Path.Combine(directory, marker)));
    }

    private static string ChooseBestJar(List<string> jarFiles)
    {
        if (jarFiles.Any(path => string.Equals(Path.GetFileName(path), "fabric-server-launch.jar", StringComparison.OrdinalIgnoreCase)))
            return jarFiles.First(path => string.Equals(Path.GetFileName(path), "fabric-server-launch.jar", StringComparison.OrdinalIgnoreCase));

        if (jarFiles.Any(path => string.Equals(Path.GetFileName(path), "quilt-server-launch.jar", StringComparison.OrdinalIgnoreCase)))
            return jarFiles.First(path => string.Equals(Path.GetFileName(path), "quilt-server-launch.jar", StringComparison.OrdinalIgnoreCase));

        if (jarFiles.Any(path => string.Equals(Path.GetFileName(path), "server.jar", StringComparison.OrdinalIgnoreCase)))
            return jarFiles.First(path => string.Equals(Path.GetFileName(path), "server.jar", StringComparison.OrdinalIgnoreCase));

        return jarFiles[0];
    }

    private static ServerType DetectServerType(IEnumerable<string> jarFiles, ServerType currentType)
    {
        var names = jarFiles.Select(Path.GetFileName).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!.ToLowerInvariant()).ToList();
        if (names.Any(name => name.Contains("fabric-server-launch")))
            return ServerType.Fabric;
        if (names.Any(name => name.Contains("quilt-server-launch")))
            return ServerType.Quilt;
        if (names.Any(name => name.Contains("paper")))
            return ServerType.Paper;
        if (names.Any(name => name.Contains("purpur")))
            return ServerType.Purpur;
        if (names.Any(name => name.Contains("forge")))
            return ServerType.Forge;
        if (names.Any(name => name.Contains("neoforge")))
            return ServerType.NeoForge;
        return currentType;
    }

    private static string ChooseFriendlyServerName(string folderName, string? motd, List<string> jarFiles)
    {
        if (!string.IsNullOrWhiteSpace(motd))
            return motd.Trim();

        if (!Guid.TryParse(folderName, out _) && !string.IsNullOrWhiteSpace(folderName))
            return folderName;

        if (jarFiles.Count > 0)
            return Path.GetFileNameWithoutExtension(jarFiles[0]);

        return "Unnamed server";
    }

    private static int FindAvailablePort(int startingPort)
    {
        for (var port = startingPort; port <= 65535; port++)
        {
            if (IsPortAvailable(port))
            {
                return port;
            }
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
 
    private void UpdateServerPropertiesPort(Server server)
    {
        if (server.ServerDirectory == null)
            return;
 
        var propertiesPath = Path.Combine(server.ServerDirectory, "server.properties");
        if (!File.Exists(propertiesPath))
        {
            var properties = $"""
                # Minecraft server properties
                server-port={server.Port}
                max-players={server.MaxPlayers}
                gamemode=survival
                difficulty=normal
                online-mode={(server.AllowOnlineMode ? "true" : "false")}
                motd={server.Name}
                """;
            File.WriteAllText(propertiesPath, properties);
            return;
        }
 
        var lines = File.ReadAllLines(propertiesPath);
        var updated = false;
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("server-port=", StringComparison.OrdinalIgnoreCase))
            {
                lines[i] = $"server-port={server.Port}";
                updated = true;
                break;
            }
        }
 
        if (!updated)
        {
            Array.Resize(ref lines, lines.Length + 1);
            lines[^1] = $"server-port={server.Port}";
        }
 
        File.WriteAllLines(propertiesPath, lines);
    }

    private void UpdateServerRoleFiles(Server server)
    {
        if (string.IsNullOrWhiteSpace(server.ServerDirectory))
            return;

        var opsPath = Path.Combine(server.ServerDirectory, "ops.json");
        if (!string.IsNullOrWhiteSpace(server.OpPlayers))
        {
            var opNames = server.OpPlayers.Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(name => name.Trim())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList();

            if (opNames.Any())
            {
                var opsEntries = opNames.Select(name => new
                {
                    uuid = GetOfflinePlayerUuid(name).ToString("D"),
                    name,
                    level = 4,
                    bypassesPlayerLimit = false
                }).ToList();

                File.WriteAllText(opsPath, JsonSerializer.Serialize(opsEntries, new JsonSerializerOptions { WriteIndented = true }));
            }
            else if (File.Exists(opsPath))
            {
                File.Delete(opsPath);
            }
        }
        else if (File.Exists(opsPath))
        {
            File.Delete(opsPath);
        }

        var whitelistPath = Path.Combine(server.ServerDirectory, "whitelist.json");
        if (server.WhiteListEnabled && !string.IsNullOrWhiteSpace(server.WhitelistedPlayers))
        {
            var whitelistNames = server.WhitelistedPlayers.Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(name => name.Trim())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList();

            if (whitelistNames.Any())
            {
                var whitelistEntries = whitelistNames.Select(name => new
                {
                    uuid = GetOfflinePlayerUuid(name).ToString("D"),
                    name
                }).ToList();

                File.WriteAllText(whitelistPath, JsonSerializer.Serialize(whitelistEntries, new JsonSerializerOptions { WriteIndented = true }));
            }
            else if (File.Exists(whitelistPath))
            {
                File.Delete(whitelistPath);
            }
        }
        else if (File.Exists(whitelistPath))
        {
            File.Delete(whitelistPath);
        }
    }

    private static Guid GetOfflinePlayerUuid(string playerName)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes($"OfflinePlayer:{playerName}");
        var hash = md5.ComputeHash(bytes);
        hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash);
    }

    private void UpdateServerPlayerCountFromLog(Server server, string line)
    {
        var text = line.ToLowerInvariant();
        if (text.Contains(" joined the game") || text.Contains(" joined the game."))
        {
            server.CurrentPlayers = Math.Min(server.MaxPlayers, server.CurrentPlayers + 1);
            SaveServersToDisk();
        }
        else if (text.Contains(" left the game") || text.Contains(" lost connection") || text.Contains(" left the world"))
        {
            server.CurrentPlayers = Math.Max(0, server.CurrentPlayers - 1);
            SaveServersToDisk();
        }
    }
 
    private static void EnsureEulaAccepted(string serverDirectory)
    {
        var eulaPath = Path.Combine(serverDirectory, "eula.txt");
        if (File.Exists(eulaPath))
        {
            var lines = File.ReadAllLines(eulaPath);
            var hasEula = false;
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].StartsWith("eula=", StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = "eula=true";
                    hasEula = true;
                    break;
                }
            }
 
            if (!hasEula)
            {
                Array.Resize(ref lines, lines.Length + 1);
                lines[^1] = "eula=true";
            }
 
            File.WriteAllLines(eulaPath, lines);
            return;
        }
 
        File.WriteAllText(eulaPath, "# Minecraft Control Hub - EULA accepted automatically\neula=true\n");
    }
 
    private void SaveServersToDisk()
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(_servers, options);
            var tempPath = _serversStatePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Copy(tempPath, _serversStatePath, overwrite: true);
            File.Delete(tempPath);
        }
        catch
        {
            // Ignore save failures for now; best effort only.
        }
    }
}
