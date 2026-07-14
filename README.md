# Minecraft Control Hub

An all-in-one Minecraft management suite for Windows. Launcher, server manager, mod browser, AI assistant, friend system, and tunnel manager — unified in a single desktop application built with .NET 8 and WPF.

---

## Features

### Launcher & Installations

Manage multiple independent Minecraft installations, each with its own directory structure, settings, and mod list. A setup wizard creates vanilla, Fabric, Forge, NeoForge, or Quilt installations in a few clicks. The launcher handles the full startup chain: version manifest retrieval, asset and library downloads, client jar acquisition, and JVM argument assembly per loader — including NeoForge-specific classpath handling.

A built-in **Java detection system** automatically matches installed Java versions (8, 17, 21) to the appropriate Minecraft version.

Each installation has a dedicated settings panel with sidebar navigation (Home / Mods / Advanced), including a **RAM calculator** that estimates optimal memory allocation based on Minecraft version, loader, mod count/weight, and render distance.

### Dual-Platform Mod Management

Search, install, update, and manage mods from **both Modrinth and CurseForge** simultaneously. A unified search combines results from both platforms with automatic deduplication. Source badges clearly indicate which platform each mod comes from.

- **Search tab** — filter by source (Modrinth, CurseForge, or Both) with sorting options
- **Installed tab** — update, remove, and manage dependencies with automatic conflict resolution
- **Modpacks tab** — full inline browser with search, sorting, and one-click installs
- **Import/Export** — compatible with `.mrpack` files from Prism Launcher and Modrinth App

CurseForge API calls route through the Nexora backend proxy to comply with ToS requirements — the API key stays server-side and is never embedded in the client.

### Server Manager

Create, start, stop, restart, and delete Minecraft servers with real process management. Full directory provisioning including EULA acceptance, `server.properties` generation, startup scripts, and jar downloads with fallback handling.

The server preview window features a sidebar with live status indicators, a terminal with live console output and command history, log viewer, plugin/mod management with compatibility checks, and settings panels.

### Microsoft Account Integration

Full Microsoft/Xbox Live authentication via device code flow (Xbox Live → XSTS → Minecraft Services). Supports account switching, profile display, skin/cape management including skin uploads from disk.

### Friend System (Nexora Integration)

Integrated friend system linked to the Nexora web platform. Users authenticate via a Nexora account (supporting 2FA/TOTP), then link their Microsoft/Xbox account. The system enables friend requests, acceptance/decline flows, and displays friends with their Minecraft usernames.

### Tunnel Manager

Expose local servers publicly without manual port forwarding. Built-in support for four tunnel providers with automatic startup and address detection:

- **ngrok** — popular provider with stable IPs
- **bore** — lightweight, open-source tunneling
- **serveo** — SSH-based tunneling
- **frp** — self-hosted option for full control

**Playit.gg** is supported via manual address input due to undocumented API requirements.

**Address sharing** — one-click sharing of tunnel addresses with selected Nexora friends, with in-app notifications for recipients.

**Port awareness** — automatic detection of additional ports needed by voice chat mods (Simple Voice Chat), Bedrock compatibility (Geyser), map plugins (Dynmap, BlueMap), and analytics (Plan).

### AI Command Layer

Natural language interface to control the application: *"install a shader mod"*, *"set up a server for my friends"*. Commands are parsed into structured actions and dispatched to existing services — the AI never directly touches files or processes. All actions require explicit user confirmation before execution.

Powered by Gemini (user-provided API key) with a guided setup flow for non-technical users.

### Installation Health

A unified health dashboard combining:

- **Java version check** — verifies correct Java for the Minecraft version
- **Mod status** — dependency and update checks
- **RAM analysis** — post-session memory usage analysis via GC logs
- **Overall score** — composite health rating with transparent breakdown

### Content Browser

Dedicated browser for resource packs, shader packs, and world saves with drag-and-drop support, all powered by the Modrinth API.

---

## Architecture

| Layer | Technology |
|-------|-----------|
| UI Framework | WPF (.NET 8, Windows only) |
| DI Container | `Microsoft.Extensions.DependencyInjection` |
| HTTP | `HttpClient` via DI with named factories |
| JSON | `System.Text.Json` |
| Image Processing | `SixLabors.ImageSharp` + custom `AsyncImageLoader` |
| Minecraft Data | Mojang version manifest, Modrinth REST API, CurseForge REST API |
| Authentication | Microsoft/Xbox Live OAuth (device code flow) |
| Backend Proxy | PHP proxy for CurseForge API (ToS compliance) |

The codebase follows strict separation between **Core** (pure business logic, no WPF dependencies) and **UI** (Pages, ViewModels, Windows), connected via dependency injection.

---

## Building from Source

```powershell
# Requirements: .NET 8 SDK, Windows
cd MinecraftControlHub
dotnet build
dotnet run --project MinecraftControlHub
```

---

## License

This project is open source. See the [LICENSE](LICENSE) file for details.
