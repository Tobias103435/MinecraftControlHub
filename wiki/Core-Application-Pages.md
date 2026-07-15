# Core Application Pages

## Home Page

`HomePage.xaml` + `HomePageViewModel`

The landing page. Shows all Minecraft installations and health cards.

**Features:**
- Installation cards with name, loader badge, version, and health indicator
- Play button per installation (launches the game)
- "New Installation" button → opens creation dialog
- Health card per installation (score, Java status, RAM, mod updates, crash indicator)
- Import from official Minecraft launcher

**ViewModel key properties:**
```csharp
ObservableCollection<InstallationViewModel> Installations
ICommand CreateInstallationCommand
ICommand LaunchCommand
ICommand OpenSettingsCommand
bool IsLoading
```

---

## Servers Page

`ServersPage.xaml` + `ServersPageViewModel`

Manages Minecraft servers with a live terminal.

**Features:**
- Server list with status indicators (Running / Stopped / Provisioning)
- Start / Stop / Delete buttons
- Live terminal with console output and command input
- Whitelist and operator management panels
- server.properties editor
- Backup and restore buttons
- Plugin/mod browser tab

---

## Mods Page

`ModsPage.xaml` + `ModsPageViewModel`

Search and manage mods for the selected installation.

**Features:**
- Installation selector dropdown
- Search bar (queries Modrinth + CurseForge simultaneously)
- Installed mods list with enable/disable toggles and update badges
- "Update All" button
- Conflict and dependency scanner
- Import from local JAR

---

## AI Page

`AiPage.xaml` + `AiTerminalViewModel`

The AI terminal chat interface.

**Features:**
- Message history with streaming rendering
- Action plan confirmation cards (Execute / Cancel)
- Provider and model shown in footer
- Warning shown when AI is not configured
- Crash reports auto-submitted here from ServerService

---

## Tunnel Page

`TunnelPage.xaml` + `TunnelPageViewModel`

Manages tunnel assignments per server port.

**Features:**
- Port/provider assignment list
- Per-assignment: provider selector, create/stop tunnel button, status, public address
- "Share with friends" button (opens ShareTunnelWindow)
- "Configure" button → navigates to Settings if provider is not set up
- Incoming tunnel notification list (from friends)

---

## Settings Page

`SettingsPage.xaml` + `SettingsPageViewModel`

Application settings organized in partial views:

| Partial | File | Contents |
|---|---|---|
| Main | `SettingsPage.xaml` | Theme toggle, auto-update, mod backup |
| AI | `SettingsPage.NexoraSection.xaml` | Provider, API key, model, custom endpoint, Gemini model discovery |
| Tunnels | `TunnelSettings.partial.xaml` | Exe paths and API keys per provider |
| Nexora | `SettingsPage.NexoraSection.xaml` | Notification preferences |
