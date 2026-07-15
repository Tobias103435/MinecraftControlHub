# Configuration and Settings Management

## Overview

All application settings are stored in a single JSON file managed by `SettingsService`. The service handles safe loading (with defaults on missing fields), persistence, and exposes settings to the rest of the app via `ISettingsService`.

---

## Settings File Location

```
%LocalAppData%\MinecraftControlHub\settings.json
```

---

## AppSettings Schema

| Property | Type | Default | Description |
|---|---|---|---|
| `Theme` | `string` | `"Dark"` | `"Dark"` or `"Light"` |
| `AutoUpdateMods` | `bool` | `false` | Check for mod updates on startup |
| `KeepModsBackup` | `bool` | `false` | Keep previous mod version before updating |
| `OfflineUsername` | `string` | `""` | Username for offline/demo mode launches |
| `AiProvider` | `string` | `"OpenAI"` | `"OpenAI"`, `"Gemini"`, or `"Custom"` |
| `AiApiKey` | `string` | `""` | API key for the selected AI provider |
| `AiModel` | `string` | `"gpt-4o-mini"` | Model identifier |
| `AiApiEndpoint` | `string` | `null` | Custom base URL (only used when provider is `"Custom"`) |
| `TunnelExePaths` | `Dictionary<string, string>` | `{}` | Provider ID → path to executable |
| `TunnelApiKeys` | `Dictionary<string, string>` | `{}` | Provider ID → authtoken |

---

## Safe Loading

`SettingsService` uses `JsonSerializerOptions` with `PropertyNameCaseInsensitive = true` and handles missing files and malformed JSON gracefully — returning a default `AppSettings` instance rather than throwing.

```csharp
public AppSettings Settings { get; private set; } = new();

public void Load()
{
    if (!File.Exists(AppPaths.SettingsFile)) return;
    try
    {
        var json = File.ReadAllText(AppPaths.SettingsFile);
        Settings = JsonSerializer.Deserialize<AppSettings>(json, _options) ?? new AppSettings();
    }
    catch { Settings = new AppSettings(); }
}
```

---

## Persistence

```csharp
public void Save()
{
    var json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(AppPaths.SettingsFile, json);
}
```

Settings are saved immediately after any change from the UI. There is no "pending changes" state — every toggle or text field change calls `Save()` on the spot.

---

## AppPaths — Central Path Registry

`AppPaths` is a static class that provides all file system paths used by the app. All paths are under `%LocalAppData%\MinecraftControlHub\`.

| Property | Path |
|---|---|
| `BaseDir` | `%LocalAppData%\MinecraftControlHub\` |
| `SettingsFile` | `BaseDir\settings.json` |
| `NexoraAccountFile` | `BaseDir\nexora_account.json` |
| `MinecraftAccountFile` | `BaseDir\minecraft_account.json` |
| `InstancesDir` | `BaseDir\instances\` |
| `ServersDir` | `BaseDir\Servers\` |
| `MinecraftDir` | `BaseDir\minecraft\` |
| `LogsDir` | `BaseDir\logs\` |
| `DiagnosticsLog` | `BaseDir\logs\diagnostics.log` |
| `TunnelConfigsDir` | `BaseDir\tunnel-configs\` |

---

## Theme System

Themes are applied via WPF `ResourceDictionary` merging. `ThemeService` swaps the active dictionary at runtime without restarting the app.

```csharp
public static void ApplySavedTheme(ISettingsService settings)
{
    var theme = settings.Settings.Theme == "Light" ? "LightTheme" : "Theme";
    ApplyTheme(theme);
}

private static void ApplyTheme(string name)
{
    var dict = new ResourceDictionary
    {
        Source = new Uri($"/Styles/{name}.xaml", UriKind.Relative)
    };
    Application.Current.Resources.MergedDictionaries[0] = dict;
}
```

The theme is applied in `App.OnStartup` before the main window renders, avoiding any flash of the wrong theme.

---

## DI Registration

```csharp
services.AddSingleton<ISettingsService, SettingsService>();
```

`SettingsService` is a singleton — all services share the same settings instance. Changes made in the Settings page are immediately visible to all other services.
