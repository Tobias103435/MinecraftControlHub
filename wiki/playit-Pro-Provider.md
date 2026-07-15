# playit Pro Provider

## Overview

playit Pro is the premium tier of playit.gg, offering a dedicated IP address, custom domain support, and automated address discovery via the playit REST API.

---

## Features vs Free Tier

| Feature | Free | Pro |
|---|---|---|
| TCP support | ✓ | ✓ |
| UDP support | ✓ | ✓ |
| Address changes each session | Yes | No (dedicated IP) |
| Custom domain | ✗ | ✓ |
| Auth required | No | Yes (`playit.toml`) |
| Address discovery | Manual (paste) | RemoteApi (automatic) |

---

## Authentication

The playit Pro binary uses a secret key stored in `playit.toml`. This file is created and managed by the official playit wizard after linking your account.

File location:
```
%LocalAppData%\MinecraftControlHub\tunnel-configs\playit.toml
```

The launcher reads the secret key from this file and passes it to the playit binary at launch.

---

## RemoteApi Mode

In Pro mode, `TunnelSession` uses `RemoteApi` address discovery — it queries the playit REST API to retrieve the current tunnel address instead of parsing stdout:

```
GET https://api.playit.gg/claim/details
Authorization: Bearer <secret_key>
→ { "tunnel_addr": "xxx.dedicated.playit.gg:25565" }
```

This is more reliable than stdout parsing for long-running dedicated servers.

---

## TunnelProvider Configuration

```json
{
    "id": "playit-pro",
    "displayName": "playit Pro",
    "tier": "paid",
    "supportsTcp": true,
    "supportsUdp": true,
    "requiresAuth": true,
    "addressSource": "RemoteApi",
    "remoteApiUrl": "https://api.playit.gg",
    "secretKeyPath": "%LOCALAPPDATA%\\MinecraftControlHub\\tunnel-configs\\playit.toml"
}
```
