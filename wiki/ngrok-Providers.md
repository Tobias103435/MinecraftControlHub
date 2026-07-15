# ngrok Providers (Free & Pro)

## Overview

ngrok is a widely used general-purpose tunnel service. The launcher supports both the free and Pro tiers. Both require an authtoken from [dashboard.ngrok.com](https://dashboard.ngrok.com).

---

## Free Tier

- **Auth**: Authtoken required
- **TCP**: ✓
- **UDP**: ✗ (TCP only on free plan)
- **Address changes**: Each session (random subdomain)
- **Address discovery**: LocalApi — the ngrok agent exposes a local REST API on port 4040

### Setup

1. Sign up at [ngrok.com](https://ngrok.com) and copy your authtoken
2. Download `ngrok.exe` and note its path
3. In **Settings → Tunnels → ngrok**: set the executable path and paste the authtoken
4. Create a tunnel from the Tunnel page — the address appears automatically

---

## LocalApi Discovery

ngrok's agent runs a local management API at `http://localhost:4040`. After the tunnel process starts, `TunnelSession` polls this endpoint to get the public address:

```
GET http://localhost:4040/api/tunnels
→ {
    "tunnels": [{
        "public_url": "tcp://0.tcp.ngrok.io:12345",
        "config": { "addr": "localhost:25565" }
    }]
  }
```

The dot-path `tunnels[0].public_url` is extracted and displayed in the Tunnel page.

---

## ngrok Pro

| Feature | Free | Pro |
|---|---|---|
| Named domains | ✗ | ✓ |
| Reserved TCP addresses | ✗ | ✓ |
| Simultaneous tunnels | 1 | Multiple |
| Auth | Authtoken | Same |

ngrok Pro uses the same `LocalApi` discovery mechanism as the free tier — only the tunnel's address will be a stable named address instead of a random subdomain.

---

## Limitations

- TCP only — Bedrock Edition / Geyser (UDP) cannot use ngrok on the free or paid plan
- The local management API (port 4040) must not be blocked by another process
- The random subdomain changes every time the tunnel is restarted (free tier)
