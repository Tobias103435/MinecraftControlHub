# Settings and Configuration Service

## Overview

`SettingsService` is the single source of truth for all user-configurable settings. It loads and saves `AppSettings` as a JSON file and exposes it to all other services via `ISettingsService`.

---

## ISettingsService

```csharp
public interface ISettingsService
{
    AppSettings Settings { get; }
    void Save();
    void Reload();
}
```

---

## AppSettings

```csharp
public class AppSettings
{
    // General
    public string Theme              { get; set; } = "Dark";
    public bool   AutoUpdateMods     { get; set; } = false;
    public bool   KeepModsBackup     { get; set; } = false;
    public string OfflineUsername    { get; set; } = "";

    // AI Terminal
    public string  AiProvider        { get; set; } = "OpenAI";
    public string  AiApiKey          { get; set; } = "";
    public string  AiModel           { get; set; } = "gpt-4o-mini";
    public string? AiApiEndpoint     { get; set; } = null;

    // Tunnels
    public Dictionary<string, string> TunnelExePaths { get; set; } = new();
    public Dictionary<string, string> TunnelApiKeys  { get; set; } = new();
}
```

---

## File Location

```
%LocalAppData%\MinecraftControlHub\settings.json
```

---

## Safe Loading

If `settings.json` is missing or contains invalid JSON, a default `AppSettings` instance is returned. No exception is thrown.

```csharp
public void Reload()
{
    if (!File.Exists(AppPaths.SettingsFile))
    {
        Settings = new AppSettings();
        return;
    }
    try
    {
        var json = File.ReadAllText(AppPaths.SettingsFile);
        Settings = JsonSerializer.Deserialize<AppSettings>(json, _options) ?? new AppSettings();
    }
    catch { Settings = new AppSettings(); }
}
```

---

## Saving

Settings are saved immediately after any change. The Settings page calls `_settingsService.Save()` on every toggle, text field change, or dropdown selection.

---

## Usage in AIService

`AIService` reads settings on every API call — it never caches the key or endpoint. This means changes made in Settings are picked up immediately without restarting the AI terminal.

```csharp
var endpoint = (_settings.Settings.AiApiEndpoint ?? "https://api.openai.com/v1").TrimEnd('/');
var key      = _settings.Settings.AiApiKey;
var model    = _settings.Settings.AiModel ?? "gpt-4o-mini";
```
