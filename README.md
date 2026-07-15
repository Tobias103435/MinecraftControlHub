# Nexora Launcher

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/nexoralauncher)

An all-in-one Minecraft management suite for Windows. Launcher, server manager, mod browser, AI assistant, friend system, and tunnel manager — unified in a single desktop application built with .NET 8 and WPF.

**[Download](https://nexoragames.nl/desktop/launcher/download.php)** · **[Documentation](https://nexoragames.nl/desktop/launcher/documentation.php)** · **[Website](https://nexoragames.nl/desktop/launcher/index.php)** · **[GitHub](https://github.com/Tobias103435/MinecraftControlHub)**

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

**Friend whitelisting** — restrict a server to selected Nexora friends. Toggling a friend on writes their UUID/username directly into `whitelist.json` in the format Minecraft expects, with live `/whitelist reload` support while the server is running — no manual entry required.

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

**Playit.gg** is supported via manual address input: its official Windows client runs as a background service outside the app's control and its remote API requires undocumented HMAC-signed requests, so the app asks for the address it already displays rather than trying to drive it directly.

**Address sharing** — one-click sharing of tunnel addresses with selected Nexora friends, with in-app notifications for recipients.

**Port awareness** — automatic detection of additional ports needed by voice chat mods (Simple Voice Chat), Bedrock compatibility (Geyser), map plugins (Dynmap, BlueMap), and analytics (Plan).

### AI Command Layer

Natural language interface to control the application: _"install a shader mod"_, _"set up a server for my friends"_. Commands are parsed into structured actions and dispatched to existing services — the AI never directly touches files or processes. All actions require explicit user confirmation before execution, and identical commands are capped at 5 repeats to prevent runaway retry loops.

**Chaining** — the AI can inspect whether a previous command succeeded or failed and adjust its next action accordingly (e.g. recognizing a dependency conflict and proposing an alternative), rather than firing commands blind.

Powered by Gemini (user-provided API key, free tier) with a guided setup flow for non-technical users — a short walkthrough video shows exactly how to generate a free key at Google AI Studio, and the app auto-selects a default model so there's nothing else to configure. Advanced model/parameter options are tucked away in Settings for anyone who wants them.

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

| Layer            | Technology                                                      |
| ---------------- | --------------------------------------------------------------- |
| UI Framework     | WPF (.NET 8, Windows only)                                      |
| DI Container     | `Microsoft.Extensions.DependencyInjection`                      |
| HTTP             | `HttpClient` via DI with named factories                        |
| JSON             | `System.Text.Json`                                              |
| Image Processing | `SixLabors.ImageSharp` + custom `AsyncImageLoader`              |
| Minecraft Data   | Mojang version manifest, Modrinth REST API, CurseForge REST API |
| Authentication   | Microsoft/Xbox Live OAuth (device code flow)                    |
| Backend Proxy    | PHP proxy for CurseForge API (ToS compliance)                   |

The codebase follows strict separation between **Core** (pure business logic, no WPF dependencies) and **UI** (Pages, ViewModels, Windows), connected via dependency injection.

---

## Roadmap

- **Cloud sync** — syncing servers, friends, and modpacks across devices via the Nexora backend.

---

## Building from Source

```powershell
# Requirements: .NET 8 SDK, Windows
cd MinecraftControlHub
dotnet build
dotnet run --project MinecraftControlHub
```

---

## Contributing

Nexora Launcher is open source and contributions are welcome — bug reports, feature requests, and pull requests can all be opened on [GitHub](https://github.com/Tobias103435/MinecraftControlHub). Check the [documentation](https://nexoragames.nl/desktop/launcher/documentation.php) for architecture notes before diving into a larger change.

## Support

Nexora Launcher is free, with no ads and no paid tiers — and it'll stay that way. If you'd like to support development, donations on [Ko-fi](https://ko-fi.com/nexoralauncher) go directly toward hosting costs for the website and backend. Nothing is gated behind a donation; it's entirely optional.

## License

This project is open source under the MIT License. See the [LICENSE](LICENSE) file for details.

CurseForge API integration is subject to the [CurseForge API Terms of Service](https://support.curseforge.com/en/support/solutions/articles/9000207405) — see the Third-Party Notice in [LICENSE](LICENSE) for details.
