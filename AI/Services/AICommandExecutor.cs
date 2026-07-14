using System.IO;
using System.IO.Compression;
using MinecraftControlHub.AI.Models;
using MinecraftControlHub.Core.Models;
using MinecraftControlHub.Core.Services;
using MinecraftControlHub.Networking;

namespace MinecraftControlHub.AI.Services;

public class AICommandExecutor
{
    private readonly IInstallationService        _installations;
    private readonly IServerService              _servers;
    private readonly IModService                 _mods;
    private readonly ITunnelService              _tunnelService;
    private readonly ISettingsService            _settings;
    private readonly IMinecraftLauncherService   _launcher;
    private readonly IMinecraftAccountService    _accounts;
    private readonly IModpackExportImportService _modpacks;

    // Rolling output buffer: last 200 lines per server, used by ReadServerOutput
    private readonly Dictionary<Guid, Queue<string>> _outputBuffers = new();
    private const int OutputBufferSize = 200;

    public AICommandExecutor(
        IInstallationService        installations,
        IServerService              servers,
        IModService                 mods,
        ITunnelService              tunnelService,
        ISettingsService            settings,
        IMinecraftLauncherService   launcher,
        IMinecraftAccountService    accounts,
        IModpackExportImportService modpacks)
    {
        _installations = installations;
        _servers       = servers;
        _mods          = mods;
        _tunnelService = tunnelService;
        _settings      = settings;
        _launcher      = launcher;
        _accounts      = accounts;
        _modpacks      = modpacks;

        // Tap into live server output so ReadServerOutput can return recent lines
        _servers.ServerOutputReceived += (_, e) =>
        {
            lock (_outputBuffers)
            {
                if (!_outputBuffers.TryGetValue(e.ServerId, out var queue))
                {
                    queue = new Queue<string>(OutputBufferSize + 1);
                    _outputBuffers[e.ServerId] = queue;
                }
                queue.Enqueue(e.Line);
                while (queue.Count > OutputBufferSize)
                    queue.Dequeue();
            }
        };
    }

    public async Task<AICommandBatchResult> ExecuteAsync(AICommandBatch batch)
    {
        var result = new AICommandBatchResult();

        foreach (var cmd in batch.Commands)
        {
            var cmdResult = await ExecuteSingleAsync(cmd);
            result.Results.Add(cmdResult);

            if (!cmdResult.Success)
                break;
        }

        return result;
    }

    private async Task<AICommandResult> ExecuteSingleAsync(AICommand cmd)
    {
        try
        {
            return cmd.Action.ToLowerInvariant() switch
            {
                "createinstallation"   => await CreateInstallationAsync(cmd),
                "deleteinstallation"   => await DeleteInstallationAsync(cmd),
                "installmod"           => await InstallModAsync(cmd),
                "removemod"            => await RemoveModAsync(cmd),
                "uninstallmod"         => await RemoveModAsync(cmd),
                "createserver"         => await CreateServerAsync(cmd),
                "startserver"          => await StartServerAsync(cmd),
                "stopserver"           => await StopServerAsync(cmd),
                "deleteserver"         => await DeleteServerAsync(cmd),
                "launchinstallation"   => await LaunchInstallationAsync(cmd),
                "launchinstance"       => await LaunchInstallationAsync(cmd),
                "checkdependencies"    => await CheckDependenciesAsync(cmd),
                "importmodfromfile"    => await ImportModFromFileAsync(cmd),
                "getlogs"              => await GetLogsAsync(cmd),
                "verifyinstallation"   => await VerifyInstallationAsync(cmd),
                "sendservercommand"    => await SendServerCommandAsync(cmd),
                "readserveroutput"     => await ReadServerOutputAsync(cmd),
                "exportmodpack"        => await ExportModpackAsync(cmd),
                "importmodpack"        => await ImportModpackAsync(cmd),
                "createbackup"         => await CreateBackupAsync(cmd),
                "restorebackup"        => await RestoreBackupAsync(cmd),
                "listbackups"          => await ListBackupsAsync(cmd),
                "editconfig"           => await EditConfigAsync(cmd),
                "whitelistplayer"      => await WhitelistPlayerAsync(cmd),
                "unwhitelistplayer"    => await UnwhitelistPlayerAsync(cmd),
                "opplayer"             => await OpPlayerAsync(cmd),
                "deopplayer"           => await DeopPlayerAsync(cmd),
                "optimizeserver"       => await OptimizeServerAsync(cmd),
                "suggestmods"          => await SuggestModsAsync(cmd),
                "scanmodconflicts"     => await ScanModConflictsAsync(cmd),
                "syncservermods"       => await SyncServerModsAsync(cmd),
                "cleancache"           => await CleanCacheAsync(cmd),
                "diagnoseperformance"  => await DiagnosePerformanceAsync(cmd),
                "switchloader"         => await SwitchLoaderAsync(cmd),
                "enabletunnel"         => await EnableTunnelAsync(cmd),
                "disabletunnel"      => DisableTunnel(cmd),
                _ => new AICommandResult
                {
                    Success = false,
                    Message = $"Unknown action '{cmd.Action}'. Check CommandKnowledge.json for supported actions."
                }
            };
        }
        catch (Exception ex)
        {
            return new AICommandResult
            {
                Success = false,
                Message = $"{cmd.Action} failed: {ex.Message}"
            };
        }
    }

    private async Task<AICommandResult> CreateInstallationAsync(AICommand cmd)
    {
        var name    = Require(cmd, "name");
        var version = Require(cmd, "minecraftVersion");
        var loader  = GetLoader(cmd.Parameters.GetValueOrDefault("loader", "Fabric"));

        var installation = new Installation
        {
            Name             = name,
            MinecraftVersion = version,
            Loader           = loader
        };

        await _installations.CreateInstallationAsync(installation);

        return new AICommandResult
        {
            Success = true,
            Message = $"Installation '{name}' created ({version}, {loader}).",
            Data    = installation
        };
    }

    private async Task<AICommandResult> DeleteInstallationAsync(AICommand cmd)
    {
        var name         = Require(cmd, "name");
        var installation = await FindInstallationByNameAsync(name);

        if (installation == null)
            return NotFound("Installation", name);

        await _installations.DeleteInstallationAsync(installation.Id);

        return new AICommandResult
        {
            Success = true,
            Message = $"Installation '{name}' deleted."
        };
    }

    private async Task<AICommandResult> InstallModAsync(AICommand cmd)
    {
        // Accept both "targetName" (new) and "installationName" (legacy alias)
        var targetName = cmd.Parameters.GetValueOrDefault("targetName")
                      ?? Require(cmd, "installationName");
        var modName    = Require(cmd, "modName");

        // Try to find a client installation first, then fall back to a server.
        var installation = await FindInstallationByNameAsync(targetName);
        if (installation != null)
            return await InstallModIntoInstallationAsync(installation, modName);

        var server = await FindServerByNameAsync(targetName);
        if (server != null)
            return await InstallModIntoServerAsync(server, modName);

        return new AICommandResult
        {
            Success = false,
            Message = $"No installation or server named '{targetName}' was found."
        };
    }

    private async Task<AICommandResult> InstallModIntoInstallationAsync(
        Installation installation, string modName)
    {
        var searchPage = await _mods.SearchModsAsync(modName,
            installation.MinecraftVersion,
            installation.Loader,
            limit: 1);

        var top = searchPage.Hits.FirstOrDefault();
        if (top == null)
            return new AICommandResult
            {
                Success = false,
                Message = $"No mod found on Modrinth matching '{modName}' for " +
                          $"{installation.MinecraftVersion} ({installation.Loader})."
            };

        var result = await _mods.InstallModFromSearchAsync(installation, top);
        return new AICommandResult
        {
            Success = result.Success,
            Message = result.Success
                ? $"Installed '{result.PrimaryMod?.Name ?? modName}' into '{installation.Name}'." +
                  (result.InstalledDependencies.Count > 0
                      ? $" Also installed {result.InstalledDependencies.Count} dependency(ies)."
                      : string.Empty)
                : (result.Error ?? $"Failed to install '{modName}'."),
            Data = result
        };
    }

    private async Task<AICommandResult> InstallModIntoServerAsync(Server server, string modName)
    {
        if (string.IsNullOrWhiteSpace(server.ServerDirectory))
            return new AICommandResult
            {
                Success = false,
                Message = $"Server '{server.Name}' has no directory set — was it provisioned correctly?"
            };

        var modsDir = Path.Combine(server.ServerDirectory, "mods");
        Directory.CreateDirectory(modsDir);

        // Map server type to a loader so Modrinth returns compatible files.
        var loader = server.Type switch
        {
            ServerType.Fabric   => LoaderType.Fabric,
            ServerType.Quilt    => LoaderType.Quilt,
            ServerType.Forge    => LoaderType.Forge,
            ServerType.NeoForge => LoaderType.NeoForge,
            _                   => LoaderType.Fabric   // Paper/Purpur/Vanilla: try Fabric as best-effort
        };

        var searchPage = await _mods.SearchModsAsync(modName,
            server.MinecraftVersion,
            loader,
            limit: 1);

        var top = searchPage.Hits.FirstOrDefault();
        if (top == null)
            return new AICommandResult
            {
                Success = false,
                Message = $"No mod found on Modrinth matching '{modName}' for " +
                          $"{server.MinecraftVersion} ({server.Type})."
            };

        // Build a thin Installation proxy so ModService can download the file.
        var proxy = new Installation
        {
            Id               = server.Id,
            Name             = server.Name,
            MinecraftVersion = server.MinecraftVersion,
            Loader           = loader,
            GameDirectory    = server.ServerDirectory
        };

        var result = await _mods.InstallModFromSearchAsync(proxy, top);
        return new AICommandResult
        {
            Success = result.Success,
            Message = result.Success
                ? $"Installed '{result.PrimaryMod?.Name ?? modName}' into server '{server.Name}'." +
                  (result.InstalledDependencies.Count > 0
                      ? $" Also installed {result.InstalledDependencies.Count} dependency(ies)."
                      : string.Empty)
                : (result.Error ?? $"Failed to install '{modName}' on server '{server.Name}'."),
            Data = result
        };
    }

    private async Task<AICommandResult> RemoveModAsync(AICommand cmd)
    {
        // Accept both "targetName" (new) and "installationName" (legacy alias)
        var targetName = cmd.Parameters.GetValueOrDefault("targetName")
                      ?? Require(cmd, "installationName");
        var modName    = Require(cmd, "modName");

        // Find by installation first, then server.
        var installation = await FindInstallationByNameAsync(targetName);
        Guid targetId;
        if (installation != null)
        {
            targetId = installation.Id;
        }
        else
        {
            var server = await FindServerByNameAsync(targetName);
            if (server == null)
                return new AICommandResult
                {
                    Success = false,
                    Message = $"No installation or server named '{targetName}' was found."
                };
            targetId = server.Id;
        }

        var mods = await _mods.GetInstalledModsAsync(targetId);
        var mod  = mods.FirstOrDefault(m =>
            m.Name.Equals(modName, StringComparison.OrdinalIgnoreCase));

        if (mod == null)
            return new AICommandResult
            {
                Success = false,
                Message = $"Mod '{modName}' is not installed in '{targetName}'."
            };

        await _mods.UninstallModAsync(targetId, mod.Id);

        return new AICommandResult
        {
            Success = true,
            Message = $"Removed '{mod.Name}' from '{targetName}'."
        };
    }

    private async Task<AICommandResult> CreateServerAsync(AICommand cmd)
    {
        var name       = Require(cmd, "name");
        var version    = Require(cmd, "minecraftVersion");
        var serverType = GetServerType(cmd.Parameters.GetValueOrDefault("serverType", "Vanilla"));

        var server = new Server
        {
            Name             = name,
            MinecraftVersion = version,
            Type             = serverType
        };

        server = await _servers.CreateServerAsync(server);
        var verification = await VerifyServerAsync(server);
        if (!verification.Success)
        {
            return new AICommandResult
            {
                Success = false,
                Message = $"Server '{name}' was created, but provisioning verification failed: {verification.Message}",
                Data = server
            };
        }

        return new AICommandResult
        {
            Success = true,
            Message = $"Server '{name}' created and provisioned successfully ({version}, {serverType}).",
            Data    = server
        };
    }

    private async Task<AICommandResult> StartServerAsync(AICommand cmd)
    {
        var name   = Require(cmd, "name");
        var server = await FindServerByNameAsync(name);

        if (server == null)
            return NotFound("Server", name);

        await _servers.StartServerAsync(server.Id);

        return new AICommandResult
        {
            Success = true,
            Message = $"Server '{name}' is starting."
        };
    }

    private async Task<AICommandResult> StopServerAsync(AICommand cmd)
    {
        var name   = Require(cmd, "name");
        var server = await FindServerByNameAsync(name);

        if (server == null)
            return NotFound("Server", name);

        await _servers.StopServerAsync(server.Id);

        return new AICommandResult
        {
            Success = true,
            Message = $"Server '{name}' stopped."
        };
    }

    private async Task<AICommandResult> DeleteServerAsync(AICommand cmd)
    {
        var name   = Require(cmd, "name");
        var server = await FindServerByNameAsync(name);

        if (server == null)
            return NotFound("Server", name);

        await _servers.DeleteServerAsync(server.Id);

        return new AICommandResult
        {
            Success = true,
            Message = $"Server '{name}' deleted."
        };
    }

    private async Task<AICommandResult> CheckDependenciesAsync(AICommand cmd)
    {
        var targetName = cmd.Parameters.GetValueOrDefault("targetName")
                      ?? cmd.Parameters.GetValueOrDefault("name")
                      ?? Require(cmd, "targetName");

        // Works for both installations and servers
        var installation = await FindInstallationByNameAsync(targetName);
        if (installation == null)
        {
            var server = await FindServerByNameAsync(targetName);
            if (server == null) return NotFound("Installation or server", targetName);
            // Build a proxy so we can reuse CheckDependencyCompatibilityAsync
            installation = new Installation
            {
                Id               = server.Id,
                Name             = server.Name,
                MinecraftVersion = server.MinecraftVersion,
                Loader           = server.Type switch
                {
                    ServerType.Fabric   => LoaderType.Fabric,
                    ServerType.Quilt    => LoaderType.Quilt,
                    ServerType.Forge    => LoaderType.Forge,
                    ServerType.NeoForge => LoaderType.NeoForge,
                    _                   => LoaderType.Fabric
                },
                GameDirectory    = server.ServerDirectory ?? string.Empty
            };
        }

        var result = await _mods.CheckDependencyCompatibilityAsync(installation);

        if (result.Issues.Count == 0)
            return new AICommandResult
            {
                Success = true,
                Message = $"All dependencies for '{targetName}' look good. {result.Summary}"
            };

        var lines = result.Issues
            .Select(i => $"- [{i.TypeLabel}] {i.RequiringModName} needs '{i.DependencyName}': {i.Description}")
            .ToList();

        return new AICommandResult
        {
            Success = false,
            Message = $"Found {result.Issues.Count} dependency issue(s) in '{targetName}':\n" +
                      string.Join("\n", lines) + "\n\n" +
                      "Use FixDependencies or install the missing mods individually.",
            Data    = result
        };
    }

    private async Task<AICommandResult> ImportModFromFileAsync(AICommand cmd)
    {
        var targetName = cmd.Parameters.GetValueOrDefault("targetName")
                      ?? cmd.Parameters.GetValueOrDefault("name")
                      ?? Require(cmd, "targetName");

        var filePath = cmd.Parameters.GetValueOrDefault("filePath", string.Empty);

        // If no file path was provided, ask the user to pick one via the UI
        if (string.IsNullOrWhiteSpace(filePath))
        {
            // Open file picker on the UI thread
            string? picked = null;
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title       = $"Select mod .jar to import into '{targetName}'",
                    Filter      = "Mod files (*.jar)|*.jar|All files (*.*)|*.*",
                    Multiselect = false
                };
                if (dlg.ShowDialog() == true)
                    picked = dlg.FileName;
            });

            if (string.IsNullOrWhiteSpace(picked))
                return new AICommandResult { Success = false, Message = "No file selected." };

            filePath = picked;
        }

        if (!File.Exists(filePath))
            return new AICommandResult
            {
                Success = false,
                Message = $"File not found: {filePath}"
            };

        if (!filePath.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
            return new AICommandResult
            {
                Success = false,
                Message = "Only .jar files can be imported as mods."
            };

        // Find target directory
        string? modsDir = null;
        var installation = await FindInstallationByNameAsync(targetName);
        if (installation != null)
        {
            modsDir = Path.Combine(installation.GameDirectory ?? string.Empty, "mods");
        }
        else
        {
            var server = await FindServerByNameAsync(targetName);
            if (server != null)
                modsDir = Path.Combine(server.ServerDirectory ?? string.Empty, "mods");
        }

        if (string.IsNullOrWhiteSpace(modsDir))
            return NotFound("Installation or server", targetName);

        Directory.CreateDirectory(modsDir);
        var dest = Path.Combine(modsDir, Path.GetFileName(filePath));

        if (File.Exists(dest))
            return new AICommandResult
            {
                Success = false,
                Message = $"A file named '{Path.GetFileName(filePath)}' is already in the mods folder of '{targetName}'. Remove it first if you want to replace it."
            };

        File.Copy(filePath, dest);

        return new AICommandResult
        {
            Success = true,
            Message = $"Imported '{Path.GetFileName(filePath)}' into '{targetName}'. " +
                      $"Restart the game/server for the mod to load."
        };
    }

    private async Task<AICommandResult> GetLogsAsync(AICommand cmd)
    {
        var targetName = cmd.Parameters.GetValueOrDefault("targetName")
                      ?? cmd.Parameters.GetValueOrDefault("name")
                      ?? Require(cmd, "targetName");

        var logType = (cmd.Parameters.GetValueOrDefault("logType") ?? "latest").ToLowerInvariant();
        var lines   = int.TryParse(cmd.Parameters.GetValueOrDefault("lines", "100"), out var n) ? n : 100;
        lines = Math.Clamp(lines, 10, 500);

        // Try installation first
        var installation = await FindInstallationByNameAsync(targetName);
        if (installation != null)
        {
            var gameDir  = installation.GameDirectory ?? string.Empty;
            var logsDir  = Path.Combine(gameDir, "logs");
            var crashDir = Path.Combine(gameDir, "crash-reports");

            // Candidate log files in priority order
            var candidates = new[]
            {
                Path.Combine(logsDir,  "latest.log"),        // vanilla / fabric / forge latest
                Path.Combine(logsDir,  "debug.log"),         // forge debug log (very detailed)
                AppPaths.GameLogFile(installation.Id, installation.Name) // app's own launch log
            };

            return BuildLogResult(targetName, logType, candidates, crashDir, lines);
        }

        // Try server
        var server = await FindServerByNameAsync(targetName);
        if (server != null)
        {
            var serverDir = server.ServerDirectory ?? string.Empty;
            var logsDir   = Path.Combine(serverDir, "logs");
            var crashDir  = Path.Combine(serverDir, "crash-reports");

            var candidates = new[]
            {
                Path.Combine(logsDir, "latest.log"),
                Path.Combine(logsDir, "debug.log")
            };

            return BuildLogResult(targetName, logType, candidates, crashDir, lines);
        }

        return NotFound("Installation or server", targetName);
    }

    private static AICommandResult BuildLogResult(
        string targetName, string logType,
        string[] candidates, string crashDir, int lines)
    {
        if (logType == "crash")
        {
            // Most recent crash report
            string? crashFile = null;
            if (Directory.Exists(crashDir))
                crashFile = Directory.GetFiles(crashDir, "*.txt")
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();

            if (crashFile == null)
                return new AICommandResult
                {
                    Success = false,
                    Message = $"No crash reports found for '{targetName}' in:\n  {crashDir}\n\n" +
                              $"The game may not have crashed, or the crash-reports folder is empty. " +
                              $"Try logType='latest' to read the regular log instead."
                };

            return ReadLines(crashFile, targetName, lines);
        }

        // For "latest", "debug" or anything else — try each candidate in order
        // If logType is "debug", prioritise debug.log
        var orderedCandidates = logType == "debug"
            ? candidates.OrderBy(p => Path.GetFileName(p) == "debug.log" ? 0 : 1).ToArray()
            : candidates;
        foreach (var path in orderedCandidates)
        {
            if (File.Exists(path))
                return ReadLines(path, targetName, lines);
        }

        // Nothing found — tell the AI exactly where we looked
        var searched = string.Join("\n  ", orderedCandidates);
        return new AICommandResult
        {
            Success = false,
            Message = $"No log file found for '{targetName}'.\n\nLooked in:\n  {searched}\n\n" +
                      $"The game or server may not have been launched yet. " +
                      $"Start it first, then call GetLogs again. " +
                      $"For crash logs use logType='crash'."
        };
    }

    private static AICommandResult ReadLines(string path, string targetName, int lines)
    {
        try
        {
            var allLines = File.ReadAllLines(path);
            var total    = allLines.Length;
            var taken    = total <= lines ? allLines : allLines[^lines..];
            var excerpt  = string.Join("\n", taken);
            var truncMsg = total > lines ? $"(showing last {lines} of {total} lines)\n\n" : string.Empty;

            // Prefix with an analysis instruction so the AI treats this as
            // context to diagnose rather than just raw output to display.
            return new AICommandResult
            {
                Success = true,
                Message = $"Log retrieved for '{targetName}' ({Path.GetFileName(path)}).\n" +
                          $"{truncMsg}" +
                          $"Analyse the following log and explain what went wrong and how to fix it:\n\n" +
                          $"```\n{excerpt}\n```"
            };
        }
        catch (Exception ex)
        {
            return new AICommandResult
            {
                Success = false,
                Message = $"Could not read log file '{path}': {ex.Message}"
            };
        }
    }

    private async Task<AICommandResult> VerifyInstallationAsync(AICommand cmd)
    {
        var targetName = cmd.Parameters.GetValueOrDefault("targetName")
                      ?? cmd.Parameters.GetValueOrDefault("name")
                      ?? Require(cmd, "targetName");

        var installation = await FindInstallationByNameAsync(targetName);
        if (installation == null)
        {
            var server = await FindServerByNameAsync(targetName);
            if (server != null) return await VerifyServerAsync(server);
            return NotFound("Installation or server", targetName);
        }

        var issues = new List<string>();
        var ok     = new List<string>();

        // 1. Game directory exists
        if (string.IsNullOrWhiteSpace(installation.GameDirectory) ||
            !Directory.Exists(installation.GameDirectory))
        {
            issues.Add("Game directory is missing or not set.");
        }
        else
        {
            ok.Add("Game directory exists.");

            // 2. Mods folder exists
            var modsDir = Path.Combine(installation.GameDirectory, "mods");
            if (!Directory.Exists(modsDir))
                issues.Add("Mods folder is missing.");
            else
                ok.Add($"Mods folder present ({Directory.GetFiles(modsDir, "*.jar").Length} jar files).");

            // 3. Check for .jar files that don't match the installation's loader
            var jars = Directory.GetFiles(modsDir, "*.jar", SearchOption.TopDirectoryOnly);
            // (We can't inspect jar manifests easily, just report the count)
            if (jars.Length > 0)
                ok.Add($"{jars.Length} mod jar(s) found.");
        }

        // 4. Dependency check
        try
        {
            var depResult = await _mods.CheckDependencyCompatibilityAsync(installation);
            if (depResult.Issues.Count == 0)
                ok.Add("All mod dependencies are satisfied.");
            else
            {
                foreach (var issue in depResult.Issues)
                    issues.Add($"[Dependency] {issue.RequiringModName}: {issue.Description}");
            }
        }
        catch (Exception ex)
        {
            issues.Add($"Could not check dependencies: {ex.Message}");
        }

        // 5. Java check — just report, don't throw
        ok.Add($"Loader: {installation.Loader}, Minecraft: {installation.MinecraftVersion}");

        var summary = new System.Text.StringBuilder();
        if (ok.Count > 0)
        {
            summary.AppendLine("✓ Checks passed:");
            foreach (var o in ok) summary.AppendLine($"  • {o}");
        }
        if (issues.Count > 0)
        {
            summary.AppendLine("\n⚠ Issues found:");
            foreach (var i in issues) summary.AppendLine($"  • {i}");
        }

        return new AICommandResult
        {
            Success = issues.Count == 0,
            Message = summary.ToString().Trim()
        };
    }

    private static AICommandResult VerifyServerResult(Server server, List<string> ok, List<string> issues)
    {
        var summary = new System.Text.StringBuilder();
        if (ok.Count > 0)
        {
            summary.AppendLine("✓ Checks passed:");
            foreach (var o in ok) summary.AppendLine($"  • {o}");
        }
        if (issues.Count > 0)
        {
            summary.AppendLine("\n⚠ Issues found:");
            foreach (var i in issues) summary.AppendLine($"  • {i}");
        }
        return new AICommandResult { Success = issues.Count == 0, Message = summary.ToString().Trim() };
    }

    private static Task<AICommandResult> VerifyServerAsync(Server server)
    {
        var issues = new List<string>();
        var ok     = new List<string>();

        ok.Add($"Type: {server.Type}, Minecraft: {server.MinecraftVersion}");

        if (string.IsNullOrWhiteSpace(server.ServerDirectory) ||
            !Directory.Exists(server.ServerDirectory))
        {
            issues.Add("Server directory is missing.");
        }
        else
        {
            ok.Add("Server directory exists.");

            var runBat = Path.Combine(server.ServerDirectory, "run.bat");
            var jarFile = string.IsNullOrWhiteSpace(server.JarFileName) ? null
                : Path.Combine(server.ServerDirectory, server.JarFileName);

            if (File.Exists(runBat))
                ok.Add("run.bat found (modern Forge/NeoForge launcher script).");
            else if (jarFile != null && File.Exists(jarFile))
                ok.Add($"Server jar found: {server.JarFileName}");
            else
                issues.Add($"No server jar or run.bat found. Provisioning may have failed. " +
                           $"JarFileName='{server.JarFileName ?? "null"}'.");

            var eula = Path.Combine(server.ServerDirectory, "eula.txt");
            if (File.Exists(eula))
                ok.Add("eula.txt accepted.");
            else
                issues.Add("eula.txt is missing.");

            var props = Path.Combine(server.ServerDirectory, "server.properties");
            if (File.Exists(props))
                ok.Add("server.properties present.");
            else
                issues.Add("server.properties is missing.");

            var modsDir = Path.Combine(server.ServerDirectory, "mods");
            if (Directory.Exists(modsDir))
            {
                var modCount = Directory.GetFiles(modsDir, "*.jar").Length;
                ok.Add($"Mods folder present ({modCount} jar files).");
            }
        }

        return Task.FromResult(VerifyServerResult(server, ok, issues));
    }

    private async Task<AICommandResult> LaunchInstallationAsync(AICommand cmd)
    {
        var name = Require(cmd, "name");
        var installation = await FindInstallationByNameAsync(name);
        if (installation == null)
            return NotFound("Installation", name);

        var account      = await _accounts.GetValidAccountAsync();
        var offlineName  = _settings.Settings.OfflineUsername;

        var result = await _launcher.LaunchAsync(installation, offlineName, account);

        return new AICommandResult
        {
            Success = result.Success,
            Message = result.Success
                ? $"Launching '{installation.Name}'… the game window should appear shortly."
                : $"Could not launch '{installation.Name}': {result.Error}"
        };
    }

    private async Task<AICommandResult> ExportModpackAsync(AICommand cmd)
    {
        var name   = Require(cmd, "name");
        var format = (cmd.Parameters.GetValueOrDefault("format") ?? "mrpack").ToLowerInvariant();

        var installation = await FindInstallationByNameAsync(name);
        if (installation == null) return NotFound("Installation", name);

        // Ask user for save location on the UI thread
        string? outputPath = null;
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var ext    = format == "prism" ? "zip" : "mrpack";
            var filter = format == "prism"
                ? "Prism Launcher zip (*.zip)|*.zip"
                : "Modrinth pack (*.mrpack)|*.mrpack";
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title      = $"Export '{name}' as modpack",
                FileName   = $"{name}.{ext}",
                Filter     = filter,
                DefaultExt = ext
            };
            if (dlg.ShowDialog() == true)
                outputPath = dlg.FileName;
        });

        if (string.IsNullOrWhiteSpace(outputPath))
            return new AICommandResult { Success = false, Message = "Export cancelled." };

        ModpackExportResult result;
        if (format == "prism")
            result = await _modpacks.ExportPrismNativeAsync(installation, outputPath);
        else
            result = await _modpacks.ExportAsync(installation, outputPath);

        return new AICommandResult
        {
            Success = result.Success,
            Message = result.Success
                ? $"Exported '{name}' to: {result.FilePath}\n{result.Summary}" +
                  (result.Warnings.Count > 0
                      ? $"\n⚠ {result.Warnings.Count} warning(s):\n" + string.Join("\n", result.Warnings.Select(w => $"  • {w}"))
                      : string.Empty)
                : $"Export failed: {result.Error}"
        };
    }

    private async Task<AICommandResult> ImportModpackAsync(AICommand cmd)
    {
        var filePath = cmd.Parameters.GetValueOrDefault("filePath", string.Empty);

        if (string.IsNullOrWhiteSpace(filePath))
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title  = "Select modpack file to import",
                    Filter = "Modpack files (*.mrpack;*.zip)|*.mrpack;*.zip|All files (*.*)|*.*"
                };
                if (dlg.ShowDialog() == true)
                    filePath = dlg.FileName;
            });
        }

        if (string.IsNullOrWhiteSpace(filePath))
            return new AICommandResult { Success = false, Message = "No file selected." };

        if (!File.Exists(filePath))
            return new AICommandResult { Success = false, Message = $"File not found: {filePath}" };

        var result = await _modpacks.ImportAsync(filePath);

        return new AICommandResult
        {
            Success = result.Success,
            Message = result.Success
                ? $"Imported modpack as '{result.Installation?.Name}'.\n{result.Summary}" +
                  (result.Warnings.Count > 0
                      ? $"\n⚠ {result.Warnings.Count} warning(s):\n" + string.Join("\n", result.Warnings.Select(w => $"  • {w}"))
                      : string.Empty)
                : $"Import failed: {result.Error}"
        };
    }

    private async Task<AICommandResult> SendServerCommandAsync(AICommand cmd)
    {
        var serverName = cmd.Parameters.GetValueOrDefault("serverName")
                      ?? cmd.Parameters.GetValueOrDefault("name")
                      ?? Require(cmd, "serverName");
        var command    = Require(cmd, "command");

        var server = await FindServerByNameAsync(serverName);
        if (server == null) return NotFound("Server", serverName);

        if (server.Status != ServerStatus.Running)
            return new AICommandResult
            {
                Success = false,
                Message = $"Server '{serverName}' is not running (status: {server.Status}). Start it first."
            };

        await _servers.SendServerCommandAsync(server.Id, command);

        return new AICommandResult
        {
            Success = true,
            Message = $"Sent command '{command}' to server '{serverName}'."
        };
    }

    private async Task<AICommandResult> ReadServerOutputAsync(AICommand cmd)
    {
        var serverName = cmd.Parameters.GetValueOrDefault("serverName")
                      ?? cmd.Parameters.GetValueOrDefault("name")
                      ?? Require(cmd, "serverName");
        var lines      = int.TryParse(cmd.Parameters.GetValueOrDefault("lines", "50"), out var n) ? n : 50;
        lines = Math.Clamp(lines, 5, 200);

        var server = await FindServerByNameAsync(serverName);
        if (server == null) return NotFound("Server", serverName);

        string[] recent;
        lock (_outputBuffers)
        {
            if (_outputBuffers.TryGetValue(server.Id, out var queue))
                recent = queue.TakeLast(lines).ToArray();
            else
                recent = Array.Empty<string>();
        }

        if (recent.Length == 0)
            return new AICommandResult
            {
                Success = false,
                Message = $"No output captured yet for '{serverName}'. " +
                          "The server may not have produced output since this session started, " +
                          "or it is not running. Try GetLogs to read the log file instead."
            };

        var excerpt = string.Join("\n", recent);
        return new AICommandResult
        {
            Success = true,
            Message = $"Last {recent.Length} lines of live output from '{serverName}':\n\n" +
                      $"Analyse this output and report anything noteworthy:\n```\n{excerpt}\n```"
        };
    }

    // =========================================================================
    // Backup management
    // =========================================================================

    private async Task<AICommandResult> CreateBackupAsync(AICommand cmd)
    {
        var targetName = cmd.Parameters.GetValueOrDefault("targetName")
                      ?? cmd.Parameters.GetValueOrDefault("name")
                      ?? Require(cmd, "targetName");

        var server = await FindServerByNameAsync(targetName);
        string? sourceDir = null;
        string  label     = string.Empty;

        if (server != null)
        {
            sourceDir = server.ServerDirectory;
            label     = $"server '{server.Name}'";
        }
        else
        {
            var inst = await FindInstallationByNameAsync(targetName);
            if (inst == null) return NotFound("Installation or server", targetName);
            sourceDir = inst.GameDirectory;
            label     = $"installation '{inst.Name}'";
        }

        if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir))
            return new AICommandResult
            {
                Success = false,
                Message = $"Directory not found for {label}."
            };

        var backupsDir = Path.Combine(sourceDir, "backups");
        Directory.CreateDirectory(backupsDir);

        var timestamp  = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var zipName    = $"backup_{timestamp}.zip";
        var zipPath    = Path.Combine(backupsDir, zipName);

        // Zip everything except the backups folder itself
        await Task.Run(() =>
        {
            using var archive = System.IO.Compression.ZipFile.Open(
                zipPath, System.IO.Compression.ZipArchiveMode.Create);

            foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                // Skip files inside the backups folder
                if (file.StartsWith(backupsDir, StringComparison.OrdinalIgnoreCase))
                    continue;

                var entryName = Path.GetRelativePath(sourceDir, file)
                                    .Replace(Path.DirectorySeparatorChar, '/');
                archive.CreateEntryFromFile(file, entryName,
                    System.IO.Compression.CompressionLevel.Optimal);
            }
        });

        var size = new FileInfo(zipPath).Length;
        var sizeMb = (size / 1024.0 / 1024.0).ToString("F1");

        return new AICommandResult
        {
            Success = true,
            Message = $"Backup created for {label}: {zipName} ({sizeMb} MB)\n" +
                      $"Saved to: {zipPath}"
        };
    }

    private async Task<AICommandResult> ListBackupsAsync(AICommand cmd)
    {
        var targetName = cmd.Parameters.GetValueOrDefault("targetName")
                      ?? cmd.Parameters.GetValueOrDefault("name")
                      ?? Require(cmd, "targetName");

        var server = await FindServerByNameAsync(targetName);
        string? sourceDir = null;

        if (server != null) sourceDir = server.ServerDirectory;
        else
        {
            var inst = await FindInstallationByNameAsync(targetName);
            if (inst == null) return NotFound("Installation or server", targetName);
            sourceDir = inst.GameDirectory;
        }

        var backupsDir = Path.Combine(sourceDir ?? string.Empty, "backups");
        if (!Directory.Exists(backupsDir))
            return new AICommandResult { Success = true, Message = $"No backups found for '{targetName}'." };

        var zips = Directory.GetFiles(backupsDir, "*.zip")
            .OrderByDescending(f => File.GetLastWriteTime(f))
            .Select(f =>
            {
                var fi   = new FileInfo(f);
                var mb   = (fi.Length / 1024.0 / 1024.0).ToString("F1");
                return $"• {fi.Name} — {mb} MB — {fi.LastWriteTime:yyyy-MM-dd HH:mm}";
            })
            .ToList();

        if (zips.Count == 0)
            return new AICommandResult { Success = true, Message = $"No backups found for '{targetName}'." };

        return new AICommandResult
        {
            Success = true,
            Message = $"Backups for '{targetName}' ({zips.Count}):\n" + string.Join("\n", zips)
        };
    }

    private async Task<AICommandResult> RestoreBackupAsync(AICommand cmd)
    {
        var targetName = cmd.Parameters.GetValueOrDefault("targetName")
                      ?? cmd.Parameters.GetValueOrDefault("name")
                      ?? Require(cmd, "targetName");
        var backupName = Require(cmd, "backupName");

        var server = await FindServerByNameAsync(targetName);
        string? sourceDir = null;
        string  label     = string.Empty;

        if (server != null)
        {
            if (server.Status == ServerStatus.Running)
                return new AICommandResult
                {
                    Success = false,
                    Message = $"Stop server '{targetName}' before restoring a backup."
                };
            sourceDir = server.ServerDirectory;
            label     = $"server '{server.Name}'";
        }
        else
        {
            var inst = await FindInstallationByNameAsync(targetName);
            if (inst == null) return NotFound("Installation or server", targetName);
            sourceDir = inst.GameDirectory;
            label     = $"installation '{inst.Name}'";
        }

        var zipPath = Path.Combine(sourceDir ?? string.Empty, "backups", backupName);
        if (!File.Exists(zipPath))
            return new AICommandResult
            {
                Success = false,
                Message = $"Backup '{backupName}' not found. Use ListBackups to see available backups."
            };

        await Task.Run(() =>
        {
            // Extract and overwrite — skip the backups folder
            using var archive = System.IO.Compression.ZipFile.OpenRead(zipPath);
            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.StartsWith("backups/", StringComparison.OrdinalIgnoreCase))
                    continue;
                var destPath = Path.Combine(sourceDir!, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                entry.ExtractToFile(destPath, overwrite: true);
            }
        });

        return new AICommandResult
        {
            Success = true,
            Message = $"Backup '{backupName}' restored to {label}."
        };
    }

    // =========================================================================
    // Config editor
    // =========================================================================

    private async Task<AICommandResult> EditConfigAsync(AICommand cmd)
    {
        var targetName = cmd.Parameters.GetValueOrDefault("targetName")
                      ?? cmd.Parameters.GetValueOrDefault("name")
                      ?? Require(cmd, "targetName");
        var key   = Require(cmd, "key");
        var value = Require(cmd, "value");
        // Optional: which file to edit; defaults to server.properties for servers
        var file  = cmd.Parameters.GetValueOrDefault("file", "server.properties");

        // Find target directory
        string? dir   = null;
        string  label = string.Empty;

        var server = await FindServerByNameAsync(targetName);
        if (server != null)
        {
            dir   = server.ServerDirectory;
            label = $"server '{server.Name}'";
        }
        else
        {
            var inst = await FindInstallationByNameAsync(targetName);
            if (inst != null)
            {
                dir   = inst.GameDirectory;
                label = $"installation '{inst.Name}'";
                // For installations default to options.txt
                if (file == "server.properties") file = "options.txt";
            }
        }

        if (string.IsNullOrWhiteSpace(dir))
            return NotFound("Installation or server", targetName);

        var filePath = Path.IsPathRooted(file) ? file : Path.Combine(dir, file);
        if (!File.Exists(filePath))
            return new AICommandResult
            {
                Success = false,
                Message = $"Config file not found: {filePath}\n" +
                          $"Available files: " +
                          string.Join(", ", Directory.GetFiles(dir, "*.properties")
                              .Concat(Directory.GetFiles(dir, "*.txt"))
                              .Concat(Directory.GetFiles(dir, "*.cfg"))
                              .Select(Path.GetFileName))
            };

        // Read, find key, replace or append
        var lines   = (await File.ReadAllLinesAsync(filePath)).ToList();
        var oldValue = string.Empty;
        var found    = false;

        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("#")) continue;

            var sep = trimmed.IndexOf('=');
            if (sep < 0) continue;

            if (trimmed[..sep].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                oldValue = trimmed[(sep + 1)..].Trim();
                lines[i] = $"{key}={value}";
                found    = true;
                break;
            }
        }

        if (!found)
            lines.Add($"{key}={value}");

        await File.WriteAllLinesAsync(filePath, lines);

        // If server is running and it's server.properties, send the command live
        if (server?.Status == ServerStatus.Running &&
            file.Equals("server.properties", StringComparison.OrdinalIgnoreCase))
        {
            // Some properties can be reloaded live
            var liveReloadable = new[] { "max-players", "motd", "pvp", "difficulty",
                "allow-flight", "view-distance", "simulation-distance" };
            if (liveReloadable.Contains(key, StringComparer.OrdinalIgnoreCase))
                await _servers.SendServerCommandAsync(server.Id, $"/{key.Replace("-", "")} {value}");
        }

        return new AICommandResult
        {
            Success = true,
            Message = found
                ? $"Updated '{key}' in {Path.GetFileName(filePath)} for {label}:\n  {oldValue} → {value}"
                : $"Added '{key}={value}' to {Path.GetFileName(filePath)} for {label}."
        };
    }

    // =========================================================================
    // Player management
    // =========================================================================

    private async Task<AICommandResult> WhitelistPlayerAsync(AICommand cmd)
    {
        var serverName = cmd.Parameters.GetValueOrDefault("serverName")
                      ?? cmd.Parameters.GetValueOrDefault("name")
                      ?? Require(cmd, "serverName");
        var player     = Require(cmd, "player");

        var server = await FindServerByNameAsync(serverName);
        if (server == null) return NotFound("Server", serverName);

        // Add to server's whitelist field
        var existing = (server.WhitelistedPlayers ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (existing.Contains(player))
            return new AICommandResult
            {
                Success = true,
                Message = $"'{player}' is already whitelisted on '{serverName}'."
            };

        existing.Add(player);
        server.WhitelistedPlayers = string.Join("\n", existing.OrderBy(n => n));
        await _servers.UpdateServerAsync(server);

        // If running, also send the live command
        if (server.Status == ServerStatus.Running)
        {
            await _servers.SendServerCommandAsync(server.Id, $"whitelist add {player}");
            await _servers.SendServerCommandAsync(server.Id, "whitelist reload");
        }

        return new AICommandResult
        {
            Success = true,
            Message = $"Added '{player}' to the whitelist for '{serverName}'." +
                      (server.Status == ServerStatus.Running ? " Reloaded live." : " Restart the server to apply.")
        };
    }

    private async Task<AICommandResult> UnwhitelistPlayerAsync(AICommand cmd)
    {
        var serverName = cmd.Parameters.GetValueOrDefault("serverName")
                      ?? cmd.Parameters.GetValueOrDefault("name")
                      ?? Require(cmd, "serverName");
        var player     = Require(cmd, "player");

        var server = await FindServerByNameAsync(serverName);
        if (server == null) return NotFound("Server", serverName);

        var existing = (server.WhitelistedPlayers ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !s.Equals(player, StringComparison.OrdinalIgnoreCase))
            .ToList();

        server.WhitelistedPlayers = string.Join("\n", existing);
        await _servers.UpdateServerAsync(server);

        if (server.Status == ServerStatus.Running)
        {
            await _servers.SendServerCommandAsync(server.Id, $"whitelist remove {player}");
            await _servers.SendServerCommandAsync(server.Id, "whitelist reload");
        }

        return new AICommandResult
        {
            Success = true,
            Message = $"Removed '{player}' from the whitelist for '{serverName}'." +
                      (server.Status == ServerStatus.Running ? " Reloaded live." : " Restart to apply.")
        };
    }

    private async Task<AICommandResult> OpPlayerAsync(AICommand cmd)
    {
        var serverName = cmd.Parameters.GetValueOrDefault("serverName")
                      ?? cmd.Parameters.GetValueOrDefault("name")
                      ?? Require(cmd, "serverName");
        var player     = Require(cmd, "player");

        var server = await FindServerByNameAsync(serverName);
        if (server == null) return NotFound("Server", serverName);

        // Update the OpPlayers field
        var existing = (server.OpPlayers ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        existing.Add(player);
        server.OpPlayers = string.Join("\n", existing.OrderBy(n => n));
        await _servers.UpdateServerAsync(server);

        // Send live if running
        if (server.Status == ServerStatus.Running)
            await _servers.SendServerCommandAsync(server.Id, $"op {player}");

        return new AICommandResult
        {
            Success = true,
            Message = $"Opped '{player}' on '{serverName}'." +
                      (server.Status == ServerStatus.Running ? " Applied live." : " Restart to apply.")
        };
    }

    private async Task<AICommandResult> DeopPlayerAsync(AICommand cmd)
    {
        var serverName = cmd.Parameters.GetValueOrDefault("serverName")
                      ?? cmd.Parameters.GetValueOrDefault("name")
                      ?? Require(cmd, "serverName");
        var player     = Require(cmd, "player");

        var server = await FindServerByNameAsync(serverName);
        if (server == null) return NotFound("Server", serverName);

        var existing = (server.OpPlayers ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !s.Equals(player, StringComparison.OrdinalIgnoreCase))
            .ToList();

        server.OpPlayers = string.Join("\n", existing);
        await _servers.UpdateServerAsync(server);

        if (server.Status == ServerStatus.Running)
            await _servers.SendServerCommandAsync(server.Id, $"deop {player}");

        return new AICommandResult
        {
            Success = true,
            Message = $"De-opped '{player}' on '{serverName}'." +
                      (server.Status == ServerStatus.Running ? " Applied live." : " Restart to apply.")
        };
    }

    // =========================================================================
    // Server optimisation
    // =========================================================================

    private async Task<AICommandResult> OptimizeServerAsync(AICommand cmd)
    {
        var serverName = cmd.Parameters.GetValueOrDefault("targetName")
                      ?? cmd.Parameters.GetValueOrDefault("name")
                      ?? Require(cmd, "targetName");

        var server = await FindServerByNameAsync(serverName);
        if (server == null) return NotFound("Server", serverName);

        var propsPath = server.PropertiesPath
                     ?? Path.Combine(server.ServerDirectory ?? string.Empty, "server.properties");
        if (!File.Exists(propsPath))
            return new AICommandResult { Success = false, Message = "server.properties not found." };

        // Compute optimal view/simulation distance based on available RAM
        var ramMb       = server.MaxMemoryMB > 0 ? server.MaxMemoryMB : 2048;
        var viewDist    = ramMb >= 8192 ? 12 : ramMb >= 4096 ? 10 : 8;
        var simDist     = Math.Max(4, viewDist - 2);
        var maxTickTime = server.Type is ServerType.Paper or ServerType.Purpur ? 60000 : 6000;

        var changes = new Dictionary<string, string>
        {
            ["view-distance"]        = viewDist.ToString(),
            ["simulation-distance"]  = simDist.ToString(),
            ["max-tick-time"]        = maxTickTime.ToString(),
            ["entity-broadcast-range-percentage"] = "75",
            ["network-compression-threshold"]     = "256",
        };

        var lines = (await File.ReadAllLinesAsync(propsPath)).ToList();
        var applied = new List<string>();

        foreach (var kv in changes)
        {
            var found = false;
            for (var i = 0; i < lines.Count; i++)
            {
                var t = lines[i].TrimStart();
                if (t.StartsWith("#")) continue;
                var sep = t.IndexOf('=');
                if (sep < 0) continue;
                if (t[..sep].Trim().Equals(kv.Key, StringComparison.OrdinalIgnoreCase))
                {
                    var old = t[(sep + 1)..].Trim();
                    lines[i] = $"{kv.Key}={kv.Value}";
                    applied.Add($"  {kv.Key}: {old} → {kv.Value}");
                    found = true;
                    break;
                }
            }
            if (!found) { lines.Add($"{kv.Key}={kv.Value}"); applied.Add($"  {kv.Key}: (new) = {kv.Value}"); }
        }

        await File.WriteAllLinesAsync(propsPath, lines);

        // Aikar's flags for Paper/Purpur
        var jvmNote = string.Empty;
        if (server.Type is ServerType.Paper or ServerType.Purpur)
        {
            var aikarFlags =
                "-XX:+UseG1GC -XX:+ParallelRefProcEnabled -XX:MaxGCPauseMillis=200 " +
                "-XX:+UnlockExperimentalVMOptions -XX:+DisableExplicitGC -XX:+AlwaysPreTouch " +
                "-XX:G1NewSizePercent=30 -XX:G1MaxNewSizePercent=40 -XX:G1HeapRegionSize=8M " +
                "-XX:G1ReservePercent=20 -XX:G1HeapWastePercent=5 -XX:G1MixedGCCountTarget=4 " +
                "-XX:InitiatingHeapOccupancyPercent=15 -XX:G1MixedGCLiveThresholdPercent=90 " +
                "-XX:G1RSetUpdatingPauseTimePercent=5 -XX:SurvivorRatio=32 -XX:+PerfDisableSharedMem " +
                "-XX:MaxTenuringThreshold=1";
            server.CustomJvmArgs = aikarFlags;
            await _servers.UpdateServerAsync(server);
            jvmNote = "\n\nAikar's Flags applied to JVM arguments (restart required).";
        }

        return new AICommandResult
        {
            Success = true,
            Message = $"Optimised '{serverName}' (RAM: {ramMb} MB, type: {server.Type}):\n" +
                      string.Join("\n", applied) + jvmNote
        };
    }

    // =========================================================================
    // Mod suggestions
    // =========================================================================

    private async Task<AICommandResult> SuggestModsAsync(AICommand cmd)
    {
        var targetName = cmd.Parameters.GetValueOrDefault("targetName")
                      ?? cmd.Parameters.GetValueOrDefault("name")
                      ?? Require(cmd, "targetName");
        var theme      = cmd.Parameters.GetValueOrDefault("theme", string.Empty);

        var installation = await FindInstallationByNameAsync(targetName);
        if (installation == null) return NotFound("Installation", targetName);

        var installed = await _mods.GetInstalledModsAsync(installation.Id);
        var installedIds = installed
            .Where(m => !string.IsNullOrWhiteSpace(m.ModrinthId))
            .Select(m => m.ModrinthId!)
            .ToHashSet();

        var query = string.IsNullOrWhiteSpace(theme)
            ? installation.MinecraftVersion
            : theme;

        var results = await _mods.SearchModsAsync(
            query, installation.MinecraftVersion, installation.Loader, limit: 20);

        var suggestions = results.Hits
            .Where(h => !installedIds.Contains(h.ModrinthId))
            .Take(8)
            .ToList();

        if (suggestions.Count == 0)
            return new AICommandResult
            {
                Success = true,
                Message = $"No new mod suggestions found for '{targetName}'" +
                          (string.IsNullOrWhiteSpace(theme) ? "." : $" with theme '{theme}'.")
            };

        var lines = suggestions.Select((s, i) =>
        {
            var desc = s.Description != null && s.Description.Length > 80
                ? s.Description[..80] + "…"
                : s.Description ?? string.Empty;
            return $"  {i + 1}. **{s.Name}** by {s.Author} — {desc}\n" +
                   $"     Downloads: {s.Downloads:N0} | ID: {s.ModrinthId}";
        });

        return new AICommandResult
        {
            Success = true,
            Message = $"Suggested mods for '{targetName}'" +
                      (string.IsNullOrWhiteSpace(theme) ? "" : $" (theme: {theme})") + ":\n\n" +
                      string.Join("\n\n", lines) + "\n\n" +
                      "Use InstallMod to install any of these."
        };
    }

    // =========================================================================
    // Mod conflict scan
    // =========================================================================

    private async Task<AICommandResult> ScanModConflictsAsync(AICommand cmd)
    {
        var targetName = cmd.Parameters.GetValueOrDefault("targetName")
                      ?? cmd.Parameters.GetValueOrDefault("name")
                      ?? Require(cmd, "targetName");

        var installation = await FindInstallationByNameAsync(targetName);
        string? modsDir = null;
        string  label   = string.Empty;
        string  mcVersion = string.Empty;

        if (installation != null)
        {
            modsDir   = Path.Combine(installation.GameDirectory ?? string.Empty, "mods");
            label     = $"installation '{installation.Name}'";
            mcVersion = installation.MinecraftVersion;
        }
        else
        {
            var server = await FindServerByNameAsync(targetName);
            if (server == null) return NotFound("Installation or server", targetName);
            modsDir   = Path.Combine(server.ServerDirectory ?? string.Empty, "mods");
            label     = $"server '{server.Name}'";
            mcVersion = server.MinecraftVersion;
        }

        if (!Directory.Exists(modsDir))
            return new AICommandResult { Success = true, Message = $"No mods folder found for {label}." };

        var jars = Directory.GetFiles(modsDir, "*.jar", SearchOption.TopDirectoryOnly);
        if (jars.Length == 0)
            return new AICommandResult { Success = true, Message = $"No mods found in {label}." };

        // Scan jar manifests for mod IDs and detect duplicates/conflicts
        var modInfos = new List<(string FileName, string ModId, string Version)>();
        var conflicts = new List<string>();

        foreach (var jar in jars)
        {
            try
            {
                using var zip = System.IO.Compression.ZipFile.OpenRead(jar);

                // Try fabric.mod.json
                var fabricEntry = zip.GetEntry("fabric.mod.json");
                if (fabricEntry != null)
                {
                    using var sr = new StreamReader(fabricEntry.Open());
                    var json = await sr.ReadToEndAsync();
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    var id  = doc.RootElement.TryGetProperty("id",      out var idProp)      ? idProp.GetString()      : null;
                    var ver = doc.RootElement.TryGetProperty("version", out var verProp)     ? verProp.GetString()     : "?";
                    if (!string.IsNullOrWhiteSpace(id))
                        modInfos.Add((Path.GetFileName(jar), id, ver ?? "?"));
                    continue;
                }

                // Try mods.toml (Forge/NeoForge)
                var forgeEntry = zip.GetEntry("META-INF/mods.toml");
                if (forgeEntry != null)
                {
                    using var sr = new StreamReader(forgeEntry.Open());
                    var toml = await sr.ReadToEndAsync();
                    var modIdMatch = System.Text.RegularExpressions.Regex.Match(toml, @"modId\s*=\s*""([^""]+)""");
                    var verMatch   = System.Text.RegularExpressions.Regex.Match(toml, @"version\s*=\s*""([^""]+)""");
                    if (modIdMatch.Success)
                        modInfos.Add((Path.GetFileName(jar), modIdMatch.Groups[1].Value,
                            verMatch.Success ? verMatch.Groups[1].Value : "?"));
                }
            }
            catch { /* can't read jar, skip */ }
        }

        // Detect duplicate mod IDs (same mod loaded twice with different filenames)
        var grouped = modInfos.GroupBy(m => m.ModId, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToList();

        foreach (var g in grouped)
            conflicts.Add($"⚠ Duplicate mod ID '{g.Key}' found in:\n" +
                          string.Join("\n", g.Select(m => $"  • {m.FileName} (v{m.Version})")));

        // Check for known incompatible mod pairs
        var modIds = modInfos.Select(m => m.ModId.ToLowerInvariant()).ToHashSet();
        var knownConflicts = new[]
        {
            ("optifine",      "iris",          "OptiFine and Iris both replace the shader pipeline — use one, not both."),
            ("optifine",      "sodium",        "OptiFine conflicts with Sodium on Fabric — use Sodium + Iris instead."),
            ("lithium",       "c2me",          "Lithium and C2ME can conflict on chunk threading — test carefully."),
            ("worldedit",     "fastasyncworldedit", "WorldEdit and FastAsyncWorldEdit conflict — use FAWE alone."),
        };

        foreach (var (a, b, note) in knownConflicts)
        {
            if (modIds.Contains(a) && modIds.Contains(b))
                conflicts.Add($"⚠ Known conflict: {a} + {b} — {note}");
        }

        if (conflicts.Count == 0)
            return new AICommandResult
            {
                Success = true,
                Message = $"No conflicts detected in {label} ({modInfos.Count} mods scanned, {jars.Length} jars).\n" +
                          "Note: runtime conflicts (block IDs, event hooks) can only be detected by launching."
            };

        return new AICommandResult
        {
            Success = false,
            Message = $"Found {conflicts.Count} conflict(s) in {label}:\n\n" +
                      string.Join("\n\n", conflicts) + "\n\n" +
                      "Use RemoveMod to remove the conflicting mod."
        };
    }

    // =========================================================================
    // Sync server mods from installation
    // =========================================================================

    private async Task<AICommandResult> SyncServerModsAsync(AICommand cmd)
    {
        var installationName = cmd.Parameters.GetValueOrDefault("installationName")
                            ?? Require(cmd, "installationName");
        var serverName       = cmd.Parameters.GetValueOrDefault("serverName")
                            ?? Require(cmd, "serverName");
        var autoInstall      = string.Equals(
            cmd.Parameters.GetValueOrDefault("autoInstall", "false"), "true",
            StringComparison.OrdinalIgnoreCase);

        var installation = await FindInstallationByNameAsync(installationName);
        if (installation == null) return NotFound("Installation", installationName);

        var server = await FindServerByNameAsync(serverName);
        if (server == null) return NotFound("Server", serverName);

        var clientMods = await _mods.GetInstalledModsAsync(installation.Id);
        var serverMods = await _mods.GetInstalledModsAsync(server.Id);

        var serverModIds = serverMods
            .Where(m => !string.IsNullOrWhiteSpace(m.ModrinthId))
            .Select(m => m.ModrinthId!)
            .ToHashSet();

        // Mods on client but not on server
        var missingOnServer = clientMods
            .Where(m => !string.IsNullOrWhiteSpace(m.ModrinthId) && !serverModIds.Contains(m.ModrinthId!))
            .ToList();

        // Mods on server but not on client
        var clientModIds = clientMods
            .Where(m => !string.IsNullOrWhiteSpace(m.ModrinthId))
            .Select(m => m.ModrinthId!)
            .ToHashSet();
        var extraOnServer = serverMods
            .Where(m => !string.IsNullOrWhiteSpace(m.ModrinthId) && !clientModIds.Contains(m.ModrinthId!))
            .ToList();

        if (missingOnServer.Count == 0 && extraOnServer.Count == 0)
            return new AICommandResult
            {
                Success = true,
                Message = $"'{installationName}' and '{serverName}' are already in sync — same {clientMods.Count} mod(s)."
            };

        var report = new System.Text.StringBuilder();
        report.AppendLine($"Mod mismatch between '{installationName}' (client) and '{serverName}' (server):\n");

        if (missingOnServer.Count > 0)
        {
            report.AppendLine($"Missing on server ({missingOnServer.Count}):");
            foreach (var m in missingOnServer) report.AppendLine($"  • {m.Name} v{m.Version}");
        }

        if (extraOnServer.Count > 0)
        {
            report.AppendLine($"\nOnly on server ({extraOnServer.Count}):");
            foreach (var m in extraOnServer) report.AppendLine($"  • {m.Name} v{m.Version}");
        }

        if (autoInstall && missingOnServer.Count > 0)
        {
            report.AppendLine("\nInstalling missing mods on server…");
            var serverProxy = new Installation
            {
                Id               = server.Id,
                Name             = server.Name,
                MinecraftVersion = server.MinecraftVersion,
                Loader           = server.Type switch
                {
                    ServerType.Fabric   => LoaderType.Fabric,
                    ServerType.Quilt    => LoaderType.Quilt,
                    ServerType.Forge    => LoaderType.Forge,
                    ServerType.NeoForge => LoaderType.NeoForge,
                    _                   => LoaderType.Fabric
                },
                GameDirectory = server.ServerDirectory ?? string.Empty
            };

            var installed = 0;
            foreach (var mod in missingOnServer)
            {
                if (string.IsNullOrWhiteSpace(mod.ModrinthId)) continue;
                var search = await _mods.SearchModsAsync(mod.Name,
                    server.MinecraftVersion, serverProxy.Loader, limit: 1);
                var hit = search.Hits.FirstOrDefault(h => h.ModrinthId == mod.ModrinthId);
                if (hit == null) hit = search.Hits.FirstOrDefault();
                if (hit == null) { report.AppendLine($"  ✗ {mod.Name} — not found on Modrinth for this server."); continue; }
                var result = await _mods.InstallModFromSearchAsync(serverProxy, hit);
                if (result.Success) { report.AppendLine($"  ✓ Installed {mod.Name}"); installed++; }
                else report.AppendLine($"  ✗ {mod.Name} — {result.Error}");
            }
            report.AppendLine($"\nInstalled {installed}/{missingOnServer.Count} mods.");
        }
        else if (missingOnServer.Count > 0)
        {
            report.AppendLine("\nSet autoInstall=true to automatically install the missing mods on the server.");
        }

        return new AICommandResult { Success = true, Message = report.ToString().Trim() };
    }

    // =========================================================================
    // Clean cache
    // =========================================================================

    private async Task<AICommandResult> CleanCacheAsync(AICommand cmd)
    {
        var targetName = cmd.Parameters.GetValueOrDefault("targetName")
                      ?? cmd.Parameters.GetValueOrDefault("name")
                      ?? Require(cmd, "targetName");

        var installation = await FindInstallationByNameAsync(targetName);
        string? gameDir = null;
        string  label   = string.Empty;

        if (installation != null)
        {
            gameDir = installation.GameDirectory;
            label   = $"installation '{installation.Name}'";
        }
        else
        {
            var server = await FindServerByNameAsync(targetName);
            if (server != null)
            {
                gameDir = server.ServerDirectory;
                label   = $"server '{server.Name}'";
            }
        }

        if (string.IsNullOrWhiteSpace(gameDir) || !Directory.Exists(gameDir))
            return NotFound("Installation or server", targetName);

        // Safe folders/patterns to delete (never worlds/saves/screenshots/mods/config)
        var safeToDelete = new[]
        {
            "cache", "logs", "crash-reports", "debug",
            Path.Combine("shaderpacks", ".cache"),
            ".fabric", // Fabric intermediary cache
        };

        var deleted    = new List<string>();
        var totalBytes = 0L;

        foreach (var rel in safeToDelete)
        {
            var full = Path.Combine(gameDir, rel);
            if (Directory.Exists(full))
            {
                var size = DirSize(full);
                try
                {
                    Directory.Delete(full, recursive: true);
                    deleted.Add($"  • {rel}/ ({size / 1024 / 1024} MB)");
                    totalBytes += size;
                }
                catch (Exception ex) { deleted.Add($"  • {rel}/ (could not delete: {ex.Message})"); }
            }
        }

        if (deleted.Count == 0)
            return new AICommandResult { Success = true, Message = $"Nothing to clean for {label}." };

        return new AICommandResult
        {
            Success = true,
            Message = $"Cleaned {label} ({totalBytes / 1024 / 1024} MB freed):\n" +
                      string.Join("\n", deleted) + "\n\n" +
                      "Worlds, screenshots, mods, and configs were left untouched."
        };
    }

    private static long DirSize(string path)
    {
        try { return Directory.GetFiles(path, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length); }
        catch { return 0; }
    }

    // =========================================================================
    // Performance diagnostics
    // =========================================================================

    private async Task<AICommandResult> DiagnosePerformanceAsync(AICommand cmd)
    {
        var serverName = cmd.Parameters.GetValueOrDefault("targetName")
                      ?? cmd.Parameters.GetValueOrDefault("name")
                      ?? Require(cmd, "targetName");
        var lines      = int.TryParse(cmd.Parameters.GetValueOrDefault("lines", "200"), out var n) ? n : 200;

        var server = await FindServerByNameAsync(serverName);
        if (server == null) return NotFound("Server", serverName);

        var logPath = Path.Combine(server.ServerDirectory ?? string.Empty, "logs", "debug.log");
        if (!File.Exists(logPath))
            logPath = Path.Combine(server.ServerDirectory ?? string.Empty, "logs", "latest.log");

        if (!File.Exists(logPath))
            return new AICommandResult
            {
                Success = false,
                Message = $"No log file found for '{serverName}'. Start the server first."
            };

        var all  = await File.ReadAllLinesAsync(logPath);
        var tail = all.Length > lines ? all[^lines..] : all;
        var text = string.Join("\n", tail);

        // Look for TPS warnings
        var tpsMatch = System.Text.RegularExpressions.Regex.Matches(text,
            @"Can't keep up!.*?Did.*?(\d+)ms.*?skipping.*?(\d+)");

        var report = new System.Text.StringBuilder();
        report.AppendLine($"Performance analysis for '{serverName}' (last {tail.Length} log lines):\n");

        if (tpsMatch.Count > 0)
            report.AppendLine($"⚠ Server fell behind {tpsMatch.Count} time(s) — 'Can't keep up' detected.");
        else
            report.AppendLine("✓ No 'Can't keep up' warnings found.");

        // Entity warnings
        var entityWarnings = tail.Count(l =>
            l.Contains("entities", StringComparison.OrdinalIgnoreCase) &&
            (l.Contains("overflow", StringComparison.OrdinalIgnoreCase) ||
             l.Contains("pathfinding", StringComparison.OrdinalIgnoreCase)));
        if (entityWarnings > 0)
            report.AppendLine($"⚠ {entityWarnings} entity-related warning(s) found (possible mob overload).");

        // Out of memory
        var oomCount = tail.Count(l => l.Contains("OutOfMemoryError", StringComparison.OrdinalIgnoreCase));
        if (oomCount > 0)
            report.AppendLine($"🔴 {oomCount} OutOfMemoryError(s) found — increase MaxMemoryMB.");

        // Crash indicators
        if (tail.Any(l => l.Contains("EXCEPTION", StringComparison.OrdinalIgnoreCase) ||
                          l.Contains("Caused by:", StringComparison.OrdinalIgnoreCase)))
            report.AppendLine("🔴 Exceptions detected — use GetLogs(logType=crash) for the full crash report.");

        report.AppendLine($"\nRecommendations based on server type ({server.Type}):");
        if (server.Type is ServerType.Paper or ServerType.Purpur)
            report.AppendLine("  • Run OptimizeServer to apply Aikar's Flags and tuned view-distance.");
        else if (server.Type is ServerType.Forge or ServerType.NeoForge)
            report.AppendLine("  • Consider switching to Paper+Forge (NeoForge) for better TPS.");
        report.AppendLine("  • Use OptimizeServer to tune view-distance based on your RAM.");

        // Include last 30 lines of raw log for context
        report.AppendLine($"\nLast 30 log lines:\n```\n{string.Join("\n", tail[^30..])}\n```");

        return new AICommandResult { Success = true, Message = report.ToString().Trim() };
    }

    // =========================================================================
    // Switch loader
    // =========================================================================

    private async Task<AICommandResult> SwitchLoaderAsync(AICommand cmd)
    {
        var targetName = cmd.Parameters.GetValueOrDefault("targetName")
                      ?? cmd.Parameters.GetValueOrDefault("name")
                      ?? Require(cmd, "targetName");
        var newLoaderStr = Require(cmd, "loader");
        var newLoader    = GetLoader(newLoaderStr);

        var installation = await FindInstallationByNameAsync(targetName);
        if (installation == null) return NotFound("Installation", targetName);

        if (installation.Loader == newLoader)
            return new AICommandResult
            {
                Success = true,
                Message = $"'{targetName}' is already using {newLoader}."
            };

        var oldLoader = installation.Loader;

        // Check mod compatibility: find which mods likely don't support the new loader
        var installedMods = await _mods.GetInstalledModsAsync(installation.Id);
        var warnings      = new List<string>();

        if (installedMods.Count > 0)
        {
            foreach (var mod in installedMods.Take(20)) // check first 20 to avoid rate limiting
            {
                if (string.IsNullOrWhiteSpace(mod.ModrinthId)) continue;
                var versions = await _mods.GetModVersionsAsync(
                    mod.ModrinthId, installation.MinecraftVersion, newLoader);
                if (versions.Count == 0)
                    warnings.Add($"  • {mod.Name} — no {newLoader} version found on Modrinth");
            }
        }

        if (warnings.Count > 0 && !string.Equals(
            cmd.Parameters.GetValueOrDefault("force", "false"), "true",
            StringComparison.OrdinalIgnoreCase))
        {
            return new AICommandResult
            {
                Success = false,
                Message = $"Cannot switch '{targetName}' from {oldLoader} to {newLoader} — " +
                          $"{warnings.Count} mod(s) have no {newLoader} version:\n" +
                          string.Join("\n", warnings) + "\n\n" +
                          "Remove incompatible mods first, or set force=true to switch anyway " +
                          "(incompatible mods will remain but may crash)."
            };
        }

        installation.Loader        = newLoader;
        installation.LoaderVersion = null; // reset to latest
        await _installations.UpdateInstallationAsync(installation);

        var warnNote = warnings.Count > 0
            ? $"\n\n⚠ {warnings.Count} mod(s) may be incompatible:\n{string.Join("\n", warnings)}"
            : string.Empty;

        return new AICommandResult
        {
            Success = true,
            Message = $"Switched '{targetName}' from {oldLoader} to {newLoader}. " +
                      $"Loader version reset to Latest." + warnNote
        };
    }

    private async Task<AICommandResult> EnableTunnelAsync(AICommand cmd)
    {
        var serverName   = cmd.Parameters.GetValueOrDefault("serverName")
                        ?? cmd.Parameters.GetValueOrDefault("name", string.Empty);
        var providerId   = (cmd.Parameters.GetValueOrDefault("provider") ?? "ngrok")
                           .ToLowerInvariant();

        // Find server
        var server = await FindServerByNameAsync(serverName);
        if (server == null)
            return NotFound("Server", serverName);

        if (server.Status == ServerStatus.Provisioning)
            return new AICommandResult
            {
                Success = false,
                Message = $"Server '{serverName}' is still downloading its jar. " +
                          $"Wait for the status to change to Stopped before enabling a tunnel."
            };

        var provider = TunnelProviderRegistry.GetById(providerId)
                    ?? TunnelProviderRegistry.GetById("ngrok")!;

        var settings = _settings.Settings;

        // Check exe path
        var exePath = settings.TunnelExePaths.GetValueOrDefault(provider.Id)
                   ?? string.Empty;
        if (provider.RequiresExternalBinary && string.IsNullOrWhiteSpace(exePath))
        {
            // Try PATH fallback name
            var fallback = provider.Id switch
            {
                "ngrok" or "ngrok-pro" => "ngrok",
                "bore"                 => "bore",
                "frp"                  => "frpc",
                _                      => provider.Id
            };
            exePath = fallback;
        }

        // Check API key
        string? apiKey = null;
        if (provider.RequiresApiKey)
        {
            apiKey = settings.TunnelApiKeys.GetValueOrDefault(provider.Id);
            if (string.IsNullOrWhiteSpace(apiKey))
                return new AICommandResult
                {
                    Success = false,
                    Message = $"{provider.DisplayName} requires an API key (authtoken) that isn't configured yet. " +
                              $"Go to Settings → Tunnel, enter your {provider.DisplayName} authtoken, and save. " +
                              $"You can get a free token at {provider.WebsiteUrl}. " +
                              $"Once saved, ask me again and I'll start the tunnel."
                };
        }

        // Build the port assignment for this server's Minecraft port
        var port       = server.Port ?? 25565;
        var portInfo   = new PortInfo
        {
            Port      = port,
            Protocol  = PortProtocol.TCP,
            Purpose   = "Minecraft",
            IsEnabled = true
        };

        var assignment = new PortTunnelAssignment
        {
            Port                = portInfo,
            RecommendedProvider = provider,
            SelectedProvider    = provider
        };

        await _tunnelService.StartOneAsync(assignment, exePath, apiKey);

        // Wait briefly for the tunnel to get an address
        string? address = null;
        for (var i = 0; i < 15; i++)
        {
            await Task.Delay(500);
            var session = _tunnelService.Sessions
                .FirstOrDefault(s => s.Assignment.Port.Port == port);
            if (session?.PublicAddress != null)
            {
                address = session.PublicAddress;
                break;
            }
            if (session?.State == TunnelSessionState.Error)
            {
                return new AICommandResult
                {
                    Success = false,
                    Message = $"Tunnel failed to start: {session.ErrorMessage ?? "unknown error"}. " +
                              $"Make sure {provider.DisplayName} is installed and the API key is correct."
                };
            }
        }

        return new AICommandResult
        {
            Success = true,
            Message = address != null
                ? $"Tunnel started for '{serverName}' via {provider.DisplayName}. " +
                  $"Players can connect using: {address}"
                : $"Tunnel is starting for '{serverName}' via {provider.DisplayName} on port {port}. " +
                  $"Check the Tunnel page for the public address once it's ready."
        };
    }

    private AICommandResult DisableTunnel(AICommand cmd)
    {
        var serverName = cmd.Parameters.GetValueOrDefault("serverName")
                      ?? cmd.Parameters.GetValueOrDefault("name", "your server");

        // Find the running session by looking for any session — we stop by port
        var server = _servers.GetAllServersAsync().GetAwaiter().GetResult()
                             .FirstOrDefault(s => s.Name.Equals(serverName, StringComparison.OrdinalIgnoreCase));

        if (server == null)
            return NotFound("Server", serverName);

        var port    = server.Port ?? 25565;
        var session = _tunnelService.Sessions.FirstOrDefault(s => s.Assignment.Port.Port == port);

        if (session == null)
            return new AICommandResult
            {
                Success = false,
                Message = $"No active tunnel found for '{serverName}' (port {port})."
            };

        _tunnelService.StopOne(port);

        return new AICommandResult
        {
            Success = true,
            Message = $"Tunnel for '{serverName}' stopped."
        };
    }

    private async Task<Installation?> FindInstallationByNameAsync(string name)
    {
        var all = await _installations.GetAllInstallationsAsync();
        return all.FirstOrDefault(i =>
            i.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<Server?> FindServerByNameAsync(string name)
    {
        var all = await _servers.GetAllServersAsync();
        return all.FirstOrDefault(s =>
            s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private static string Require(AICommand cmd, string key)
    {
        if (cmd.Parameters.TryGetValue(key, out var val) && !string.IsNullOrWhiteSpace(val))
            return val;

        throw new InvalidOperationException(
            $"Required parameter '{key}' is missing or empty for action '{cmd.Action}'.");
    }

    private static AICommandResult NotFound(string type, string name) =>
        new()
        {
            Success = false,
            Message = $"{type} '{name}' was not found."
        };

    private static AICommandResult Unsupported(string action, string hint) =>
        new()
        {
            Success = false,
            Message = $"Action '{action}' cannot be executed from the AI Terminal. {hint}"
        };

    private static LoaderType GetLoader(string loader) =>
        loader.ToLowerInvariant() switch
        {
            "forge"    => LoaderType.Forge,
            "fabric"   => LoaderType.Fabric,
            "neoforge" => LoaderType.NeoForge,
            "quilt"    => LoaderType.Quilt,
            _          => LoaderType.Vanilla
        };

    private static ServerType GetServerType(string type) =>
        type.ToLowerInvariant() switch
        {
            "paper"    => ServerType.Paper,
            "purpur"   => ServerType.Purpur,
            "fabric"   => ServerType.Fabric,
            "forge"    => ServerType.Forge,
            "neoforge" => ServerType.NeoForge,
            "quilt"    => ServerType.Quilt,
            _          => ServerType.Vanilla
        };
}
