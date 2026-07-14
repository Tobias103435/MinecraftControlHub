# Core

Business logic layer. Strictly separated from `/UI` — no WPF types, no code-behind references.
All services are interface-backed and registered in `App.xaml.cs` via Microsoft DI.

## Models (`/Models`)
| File | Description |
|------|-------------|
| `AppSettings.cs` | Persisted user preferences: AI provider/model/key/endpoint, offline username, tunnel exe paths and API keys, feature toggles. |
| `Installation.cs` | A client Minecraft installation (name, version, loader, game directory, Java path, JVM args, mods list). |
| `Server.cs` | A server instance. `INotifyPropertyChanged` with UI-thread dispatch so status changes from background threads update bindings correctly. `ServerStatus` includes `Provisioning` for background jar download. |
| `Mod.cs` | Installed mod metadata (Modrinth ID, name, version, file path). |
| `ModpackManifest.cs` | Import/export manifest for modpacks (list of mods + metadata). |
| `MinecraftAccount.cs` | Microsoft/Xbox account (username, UUID, access token). |
| `NexoraAccount.cs` | Nexora platform account (username, token). |
| `AppSettings.cs` | All persisted settings including tunnel provider exe paths and API keys. |
| `PortInfo.cs` | Port number + protocol used by `TunnelService`. |
| `TunnelProvider.cs` | Provider descriptor (id, display name, command template, regex, API discovery). |
| `TunnelNotification.cs` | Tunnel share notification from the Nexora backend. |
| `ContentItem.cs` | Generic content browser item (mods, resource packs, shader packs). |

## Services (`/Services`)

### Installations
| File | Description |
|------|-------------|
| `InstallationService.cs` | Full CRUD for game installations, persisted to JSON. Fires `InstallationsChanged` event on create/delete/import so `HomePageViewModel` reloads automatically. Each installation gets an isolated game directory under `%LocalAppData%\MinecraftControlHub\instances\<id>`. Includes launcher profile import. |

### Servers
| File | Description |
|------|-------------|
| `ServerService.cs` | Full CRUD + start/stop for servers, persisted to JSON. Two-phase `CreateServerAsync`: Phase 1 (fast) sets up folder/eula/properties and registers the server immediately; Phase 2 (background `Task.Run`) downloads the jar/runs the installer. Fires `ServersChanged` event on create/delete. Supports `run.bat`-based startup for modern Forge/NeoForge (1.17+). Resets stuck `Provisioning` status on startup. |
| `ServerProvisioningService.cs` | Downloads server jars for all types. Validates downloaded jars via ZIP magic bytes (rejects placeholder text files). Supports: Vanilla (Mojang manifest), Paper/Purpur (PaperMC API), Fabric/Quilt (meta API + installer), Forge/NeoForge (Maven + installer). Detects `run.bat` as a valid installation result for modern Forge. |

### Mods
| File | Description |
|------|-------------|
| `ModService.cs` | Install/uninstall mods per installation or server. `InstallModFromSearchAsync` downloads the file and auto-resolves dependencies. Persists mod metadata to JSON per installation/server. |
| `ModrinthApiClient.cs` | Modrinth REST API client: search, version listing, file download. |
| `ModpackExportImportService.cs` | Export all mods from an installation to a `.mcpack` zip manifest; import from a manifest. |
| `ContentService.cs` | Lists resource packs, shader packs, and worlds in a game directory. |

### Minecraft
| File | Description |
|------|-------------|
| `MinecraftLauncherService.cs` | Launches Minecraft via the official launcher with a Microsoft account (online) or offline. Handles authentication token refresh, Java path detection, and JVM argument construction. |
| `JavaService.cs` | Detects installed Java runtimes, recommends the correct version for a given Minecraft version, and provides download links when no compatible runtime is found. |
| `LoaderService.cs` | Fetches available loader versions (Fabric, Forge, NeoForge, Quilt) from their respective Maven/meta APIs. |

### Accounts
| File | Description |
|------|-------------|
| `MinecraftAccountService.cs` | Microsoft device-code OAuth flow. Stores and refreshes access tokens. |
| `NexoraAccountService.cs` | Nexora platform login (username + password). |
| `NexoraApiService.cs` | HTTP client for the Nexora backend API (friends, tunnel sharing, notifications). |

### Tunnels
| File | Description |
|------|-------------|
| `TunnelProviderRegistry.cs` | Static registry of all supported tunnel providers with their command templates, stdout regexes, and API discovery configs. |
| `TunnelShareService.cs` | Shares active tunnel addresses with friends via the Nexora API. |
| `TunnelNotificationManager.cs` | Polls the Nexora API for incoming tunnel share notifications. |

### Infrastructure
| File | Description |
|------|-------------|
| `SettingsService.cs` | Loads/saves `AppSettings` to JSON on disk. Singleton; exposes `SaveAsync()`. |
| `AppPaths.cs` | Central registry of all file system paths (`DataRoot`, `InstallationsFile`, `ServersRoot`, `InstancesRoot`, etc.). |
| `AppLogService.cs` | Append-only diagnostics log. Records install decisions, Java checks, dependency resolution. |
| `RamCalculatorService.cs` | Recommends JVM memory allocation based on render distance and installed mods. |
| `PortScanService.cs` | Scans local ports to find an available port for a new server. |

## Events for cross-ViewModel refresh
Two services fire change events so ViewModels reload without polling:
- `IInstallationService.InstallationsChanged` → `HomePageViewModel` reloads
- `IServerService.ServersChanged` → `ServersPageViewModel` reloads

## Planned
- Cloud sync via the Nexora backend (`/Cloud` folder placeholder)
- Modpack sharing / marketplace
