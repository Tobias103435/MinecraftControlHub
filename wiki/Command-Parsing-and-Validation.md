# Command Parsing and Validation

## Overview

`AICommandExecutor` receives an `AICommandBatch` and executes each command in sequence. It validates parameters before calling Core services and returns a detailed result for each command.

---

## Routing

```csharp
public async Task<AICommandResult> ExecuteAsync(AICommand command, CancellationToken ct)
{
    return command.Action switch
    {
        "CreateInstallation" => await CreateInstallationAsync(command.Parameters, ct),
        "DeleteInstallation" => await DeleteInstallationAsync(command.Parameters, ct),
        "LaunchInstallation" => await LaunchInstallationAsync(command.Parameters, ct),
        "InstallMod"         => await InstallModAsync(command.Parameters, ct),
        "RemoveMod"          => await RemoveModAsync(command.Parameters, ct),
        "CreateServer"       => await CreateServerAsync(command.Parameters, ct),
        "StartServer"        => await StartServerAsync(command.Parameters, ct),
        "StopServer"         => await StopServerAsync(command.Parameters, ct),
        "DeleteServer"       => await DeleteServerAsync(command.Parameters, ct),
        "EnableTunnel"       => await EnableTunnelAsync(command.Parameters, ct),
        "DisableTunnel"      => await DisableTunnelAsync(command.Parameters, ct),
        _ => AICommandResult.Failure($"Unknown action: {command.Action}")
    };
}
```

---

## Parameter Validation

Each handler validates its required parameters before proceeding:

```csharp
private async Task<AICommandResult> CreateInstallationAsync(
    Dictionary<string, string> p, CancellationToken ct)
{
    if (!p.TryGetValue("name", out var name) || string.IsNullOrWhiteSpace(name))
        return AICommandResult.Failure("Missing required parameter: name");

    if (!p.TryGetValue("version", out var version))
        return AICommandResult.Failure("Missing required parameter: version");

    if (!p.TryGetValue("loader", out var loader))
        loader = "Vanilla"; // default

    // Validate loader
    if (!IsValidLoader(loader))
        return AICommandResult.Failure($"Unknown loader: {loader}. Supported: Vanilla, Fabric, Forge, NeoForge, Quilt");

    await _installationService.CreateInstallationAsync(new Installation
    {
        Name = name, MinecraftVersion = version, Loader = loader
    });

    return AICommandResult.Success($"Created installation '{name}'");
}
```

---

## InstallMod / RemoveMod — Dual Target

`InstallMod` and `RemoveMod` accept both installation names and server names as the `target` parameter. The executor checks both:

```csharp
var installation = await _installationService.FindByNameAsync(target);
if (installation != null)
{
    await _modService.InstallModAsync(installation.Id, modName);
    return AICommandResult.Success($"Installed {modName} in installation '{target}'");
}

var server = await _serverService.FindByNameAsync(target);
if (server != null)
{
    await _modService.InstallModOnServerAsync(server.Id, modName);
    return AICommandResult.Success($"Installed {modName} on server '{target}'");
}

return AICommandResult.Failure($"No installation or server named '{target}'");
```

---

## Batch Execution

```csharp
public async Task<AICommandBatchResult> ExecuteBatchAsync(AICommandBatch batch, CancellationToken ct)
{
    var results = new List<AICommandResult>();
    foreach (var command in batch.Commands)
    {
        var result = await ExecuteAsync(command, ct);
        results.Add(result);
        if (!result.Success) break; // stop on first failure
    }
    return new AICommandBatchResult(results);
}
```

Execution stops on the first failure to avoid cascading errors (e.g. don't try to install a mod on an installation that failed to be created).
