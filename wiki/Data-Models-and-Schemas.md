# Data Models and Schemas

## Installation

```csharp
public class Installation
{
    public string  Id               { get; init; }   // GUID
    public string  Name             { get; set; }
    public string  MinecraftVersion { get; set; }
    public string  Loader           { get; set; }    // Vanilla | Fabric | Forge | NeoForge | Quilt
    public string? LoaderVersion    { get; set; }
    public string? JavaPath         { get; set; }    // null = auto-detect
    public int     MinRam           { get; set; }    // MB
    public int     MaxRam           { get; set; }    // MB
    public string? JvmArgs          { get; set; }
}
```

## Server

```csharp
public class Server
{
    public string Id               { get; init; }   // GUID
    public string Name             { get; set; }
    public string Type             { get; set; }    // Vanilla | Paper | Purpur | Fabric | Forge | NeoForge | Quilt
    public string MinecraftVersion { get; set; }
    public int    Ram              { get; set; }    // MB
    public int    Port             { get; set; }
    public bool   IsRunning        { get; set; }
}
```

## Mod

```csharp
public class Mod
{
    public string  Id        { get; init; }
    public string  Name      { get; init; }
    public string  Version   { get; init; }
    public string  FileName  { get; init; }
    public string  Platform  { get; init; }   // modrinth | curseforge | local
    public string? ProjectId { get; init; }
    public bool    IsEnabled { get; init; }
}
```

## AppSettings

```csharp
public class AppSettings
{
    public string  Theme           { get; set; } = "Dark";
    public bool    AutoUpdateMods  { get; set; } = false;
    public bool    KeepModsBackup  { get; set; } = false;
    public string  OfflineUsername { get; set; } = "";
    public string  AiProvider      { get; set; } = "OpenAI";
    public string  AiApiKey        { get; set; } = "";
    public string  AiModel         { get; set; } = "gpt-4o-mini";
    public string? AiApiEndpoint   { get; set; }
    public Dictionary<string, string> TunnelExePaths { get; set; } = new();
    public Dictionary<string, string> TunnelApiKeys  { get; set; } = new();
}
```

## NexoraAccount

```csharp
public class NexoraAccount
{
    public int            UserId        { get; set; }
    public string         Username      { get; set; }
    public string         Token         { get; set; }
    public MinecraftLink? MinecraftLink { get; set; }
}

public class MinecraftLink
{
    public string Uuid     { get; set; }
    public string Username { get; set; }
}
```

## MinecraftAccount

```csharp
public class MinecraftAccount
{
    public string   Uuid         { get; init; }
    public string   Username     { get; init; }
    public string   AccessToken  { get; init; }
    public string   RefreshToken { get; init; }
    public DateTime ExpiresAt    { get; init; }
}
```

## AICommand / AICommandBatch

```csharp
public class AICommand
{
    public string Action     { get; init; }
    public Dictionary<string, string> Parameters { get; init; } = new();
}

public class AICommandBatch
{
    public List<AICommand> Commands { get; init; } = new();
}

public class AICommandResult
{
    public bool   Success { get; init; }
    public string Message { get; init; }

    public static AICommandResult Ok(string msg)      => new() { Success = true,  Message = msg };
    public static AICommandResult Failure(string msg) => new() { Success = false, Message = msg };
}
```

## TunnelProvider

```csharp
public class TunnelProvider
{
    public string             Id              { get; init; }
    public string             DisplayName     { get; init; }
    public string             Tier            { get; init; }
    public bool               SupportsTcp     { get; init; }
    public bool               SupportsUdp     { get; init; }
    public bool               RequiresAuth    { get; init; }
    public TunnelAddressSource AddressSource  { get; init; }
    public string?            StdoutRegex     { get; init; }
    public string?            LocalApiUrl     { get; init; }
    public string?            LocalApiJsonPath { get; init; }
    public string?            RemoteApiUrl    { get; init; }
    public string?            SecretKeyPath   { get; init; }
    public string?            CommandTemplate { get; init; }
}
```

## HealthReport

```csharp
public class HealthReport
{
    public int    Score           { get; init; }   // 0–100
    public bool   JavaOk          { get; init; }
    public bool   RamOk           { get; init; }
    public bool   HasModUpdates   { get; init; }
    public bool   HasRecentCrash  { get; init; }
    public string? JavaIssueDetail { get; init; }
    public string? RamIssueDetail  { get; init; }
}
```
