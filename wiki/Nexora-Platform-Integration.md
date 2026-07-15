# Nexora Platform Integration

## Overview

Nexora is the cloud platform powering the social and sharing features of MinecraftControlHub. The integration covers four areas:

- **Account management** — sign-in, 2FA, token lifecycle, Minecraft account linking
- **Social features** — friend requests, friend lists, real-time presence
- **Content sharing** — sharing Minecraft installations (`.mrpack`) and active tunnel addresses
- **Notifications** — polling-based delivery for received instances and tunnel invites

---

## Architecture

```
WPF Desktop App
  ├── UI / Pages
  ├── ViewModels
  └── Service Layer
       ├── NexoraApiService       (HTTP client wrapper)
       ├── NexoraAccountService   (sign-in, token, profile)
       ├── InstanceShareService   (modpack sharing)
       ├── TunnelShareService     (tunnel sharing)
       ├── InstanceNotificationManager (polls every 30s)
       └── TunnelNotificationManager   (polls every 30s)
            │
            │ HTTPS
            ▼
       PHP Launcher API
       https://nexoragames.nl/api/launcher/
            │
            ▼
       MySQL + Redis
```

---

## Components

| Component | Location | Role |
|---|---|---|
| `NexoraApiService` | `Core/Services/NexoraApiService.cs` | HTTP client for all Launcher API calls |
| `NexoraAccountService` | `Core/Services/NexoraAccountService.cs` | Sign-in, token persistence, profile, Minecraft linking |
| `InstanceShareService` | `Core/Services/InstanceShareService.cs` | Modpack export, friend sharing, share codes, notifications |
| `TunnelShareService` | `Core/Services/TunnelShareService.cs` | Tunnel address sharing with friends |
| `InstanceNotificationManager` | `Core/Services/InstanceNotificationManager.cs` | Polls and manages instance-share notifications |
| `TunnelNotificationManager` | `Core/Services/TunnelNotificationManager.cs` | Polls and manages tunnel-share notifications |
| `NexoraAccount` | `Core/Models/NexoraAccount.cs` | Local model for the authenticated account |

---

## API Base URL

```
https://nexoragames.nl/api/launcher/
```

---

## Required HTTP Headers

Every request from the launcher:

```
Accept: application/json
X-Launcher-Secret: mch-launcher-2026
User-Agent: MinecraftControlHub/1.0
```

---

## Response Format

All responses are flat JSON — payload fields sit at the root alongside `success`:

```json
{ "success": true, "token": "...", "username": "alice" }
{ "success": false, "error": "Invalid or expired token" }
```

---

## DI Registration

```csharp
services.AddHttpClient<INexoraApiService, NexoraApiService>();
services.AddSingleton<INexoraAccountService, NexoraAccountService>();
services.AddHttpClient<IInstanceShareService, InstanceShareService>();
services.AddSingleton<IInstanceNotificationManager, InstanceNotificationManager>();
services.AddHttpClient<ITunnelShareService, TunnelShareService>();
services.AddSingleton<ITunnelNotificationManager, TunnelNotificationManager>();
```

---

## Related Pages

- [Authentication and Security](Nexora-Authentication-and-Security)
- [Desktop Integration Features](Desktop-Integration-Features)
- [Nexora API Reference](Nexora-API-Reference)
- [Friend System API](Friend-System-API)
- [Content Sharing API](Content-Sharing-API)
- [Notification API](Notification-API)
- [Real-time Communication System](Real-time-Communication-System)
