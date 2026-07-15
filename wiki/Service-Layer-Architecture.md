# Service Layer Architecture

## Overview

The Core layer contains all business logic. Every service is defined by an interface and registered in the DI container. The UI and AI layers depend only on the interfaces — never on concrete implementations.

---

## Service Categories

### Installation Management

| Service | Interface | Responsibility |
|---|---|---|
| `InstallationService` | `IInstallationService` | CRUD for client installations, isolated directories, migration, import from official launcher |
| `MinecraftLauncherService` | `IMinecraftLauncherService` | Builds launch arguments, selects Java, starts the game process |
| `JavaService` | `IJavaService` | Detects installed JREs/JDKs, recommends version per MC version |
| `LoaderService` | `ILoaderService` | Fetches available Fabric/Forge/NeoForge/Quilt versions from their APIs |
| `RamCalculatorService` | `IRamCalculatorService` | Recommends RAM based on version, loader, mod count |

### Server Management

| Service | Interface | Responsibility |
|---|---|---|
| `ServerService` | `IServerService` | Full server lifecycle: create, start, stop, delete, console streaming, crash events |
| `ServerProvisioningService` | `IServerProvisioningService` | Downloads and validates server jars for all supported server types |

### Mod and Content Management

| Service | Interface | Responsibility |
|---|---|---|
| `ModService` | `IModService` | Install, uninstall, update mods; dependency resolution |
| `ModrinthApiClient` | `IModrinthApiClient` | REST client for Modrinth v2 API |
| `ModpackExportImportService` | `IModpackExportImportService` | Export/import `.mrpack` files |
| `ContentService` | `IContentService` | Lists resource packs, shader packs, and worlds in a game directory |

### Account and Platform

| Service | Interface | Responsibility |
|---|---|---|
| `MinecraftAccountService` | `IMinecraftAccountService` | Microsoft OAuth device code flow, token storage and refresh |
| `NexoraAccountService` | `INexoraAccountService` | Nexora login, validation, profile, Minecraft linking |
| `NexoraApiService` | `INexoraApiService` | HTTP client for all Nexora Launcher API calls |
| `InstanceShareService` | `IInstanceShareService` | Modpack sharing with friends and via share codes |
| `TunnelShareService` | `ITunnelShareService` | Tunnel address sharing with friends |

### Networking and Tunnels

| Service | Interface | Responsibility |
|---|---|---|
| `TunnelService` | `ITunnelService` | Manages tunnel sessions per port/provider assignment |
| `PortScanService` | `IPortScanService` | Scans `server.properties` and configs to find active ports |

### Notifications

| Service | Interface | Responsibility |
|---|---|---|
| `InstanceNotificationManager` | `IInstanceNotificationManager` | Polls instance-share notifications every 30s |
| `TunnelNotificationManager` | `ITunnelNotificationManager` | Polls tunnel-share notifications every 30s |

### Infrastructure

| Service | Interface | Responsibility |
|---|---|---|
| `SettingsService` | `ISettingsService` | Settings persistence and access |
| `AppLogService` | `IAppLogService` | Append-only diagnostics log |
| `HealthCheckService` | `IHealthCheckService` | Aggregates health signals into a `HealthReport` |

---

## Event-Driven Updates

Services fire events when their state changes. ViewModels subscribe and update their collections:

| Event | Service | Fired when |
|---|---|---|
| `InstallationsChanged` | `IInstallationService` | Installation created, updated, or deleted |
| `ServersChanged` | `IServerService` | Server created, started, stopped, or deleted |
| `ServerOutputReceived` | `IServerService` | New line of console output from a server process |
| `ServerCrashed` | `IServerService` | Server process exits with a non-zero code |
| `AccountChanged` | `INexoraAccountService` | User signs in or out |
| `Changed` | `IInstanceNotificationManager` | New instance notification received |
| `Changed` | `ITunnelNotificationManager` | New tunnel notification received |
