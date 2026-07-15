# Settings and Configuration Pages

## Settings Page

`SettingsPage.xaml` is divided into multiple partial views loaded as sections.

---

## General Section

| Setting | Control | Default |
|---|---|---|
| Theme | Toggle (Dark / Light) | Dark |
| Auto-update mods | Toggle | Off |
| Keep mod backups | Toggle | Off |
| Offline username | TextBox | "" |

Changes are saved immediately via `ISettingsService.Save()`.

---

## AI Terminal Section

| Setting | Control | Notes |
|---|---|---|
| Provider | ComboBox | OpenAI, Gemini, Custom |
| API Key | PasswordBox | Stored in settings.json |
| Model | ComboBox (editable) | Free text + dropdown |
| Custom endpoint | TextBox | Only shown when provider is Custom |
| Gemini model discovery | Button | Fetches available models for the entered API key |

The **Gemini model discovery** button calls `AIService.GetGeminiModelsAsync()` and populates the model dropdown with models that support `generateContent`. Models are sorted: stable → preview → experimental.

---

## Tunnels Section

Shows one row per registered tunnel provider:

| Column | Content |
|---|---|
| Provider name | Logo + display name |
| Executable path | TextBox + Browse button |
| Auth token / key | PasswordBox (where applicable) |
| Status | "Configured" / "Not configured" badge |

For **playit.gg free tier**: no path or key needed — the row shows an info note that the official wizard must be running.

For **frp**: shows additional fields for server address and port.

---

## Nexora Section

`SettingsPage.NexoraSection.xaml`

| Setting | Control | Notes |
|---|---|---|
| Instance notification badge | Toggle | Show/hide sidebar badge for modpack shares |
| Tunnel notification badge | Toggle | Show/hide sidebar badge for tunnel shares |

---

## Path Information

A read-only section at the bottom of the Settings page shows the paths of important files and folders:

- App data directory (`%LocalAppData%\MinecraftControlHub\`)
- Settings file
- Diagnostics log
- Instances directory

Each path has an "Open in Explorer" button.
