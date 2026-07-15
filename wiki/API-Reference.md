# API Reference

## Overview

This section documents all internal service interfaces and external API integrations used in MinecraftControlHub.

---

## Internal Service Interfaces

See [Internal Service Interfaces](Internal-Service-Interfaces) for the full contract reference of all `I`-prefixed interfaces.

Quick index:

| Interface | Category |
|---|---|
| `IInstallationService` | Installations |
| `IServerService` | Servers |
| `IModService` | Mods |
| `ILoaderService` | Mods |
| `IMinecraftLauncherService` | Launcher |
| `IJavaService` | Launcher |
| `IRamCalculatorService` | Launcher |
| `IMinecraftAccountService` | Auth |
| `INexoraAccountService` | Nexora |
| `INexoraApiService` | Nexora |
| `IInstanceShareService` | Sharing |
| `ITunnelShareService` | Sharing |
| `ITunnelService` | Tunnels |
| `IPortScanService` | Tunnels |
| `IContentService` | Content |
| `IModrinthApiClient` | Mods |
| `IModpackExportImportService` | Modpacks |
| `IServerProvisioningService` | Servers |
| `IHealthCheckService` | Health |
| `ISettingsService` | Configuration |
| `IAppLogService` | Infrastructure |
| `IAIService` | AI |
| `IKnowledgeService` | AI |

---

## External APIs

See [External API Integrations](External-API-Integrations) for full details on every third-party API.

Quick index:

| API | Used for |
|---|---|
| Mojang Manifest API | Minecraft version metadata |
| Modrinth REST v2 | Mod/content search, install, fingerprint update check |
| CurseForge (via Nexora proxy) | Mod search and install |
| Microsoft OAuth 2.0 | Minecraft account authentication |
| Xbox Live + XSTS | Xbox authentication chain |
| Minecraft Services | Minecraft token and profile |
| Nexora Launcher API | Nexora account, friends, sharing, notifications |
| OpenAI-compatible | AI chat completions (streaming + non-streaming) |
| Google Gemini | AI chat completions + model discovery |
| Fabric Meta | Fabric loader versions |
| Quilt Meta | Quilt loader versions |
| Forge / NeoForge Maven | Loader versions |
| PaperMC / Purpur API | Server jar downloads |
| Adoptium | Java runtime downloads |

---

## Nexora Launcher API

See [Nexora API Reference](Nexora-API-Reference) for the complete endpoint reference.

Base URL: `https://nexoragames.nl/api/launcher/`

---

## Data Models

See [Data Models and Schemas](Data-Models-and-Schemas) for all C# model definitions.
