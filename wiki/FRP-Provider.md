# FRP Provider

## Overview

frp (Fast Reverse Proxy) is a self-hosted tunnel solution. You need your own server (VPS, homelab, etc.) running the frp server component. The launcher runs the frp client (`frpc`) on your local machine.

---

## Characteristics

| Property | Value |
|---|---|
| TCP | ✓ |
| UDP | Depends on server config |
| Auth required | Server token |
| Address discovery | StdoutRegex |
| Hosting | Self-hosted only |
| Tier | Free (but requires a server) |

---

## Architecture

```
Local machine                    Your VPS
─────────────                    ──────────
frpc.exe ──── connects to ────► frps (frp server)
              TCP tunnel          Port 25565 exposed publicly
```

The VPS needs `frps` (frp server) running and a public IP. The launcher runs `frpc` (frp client) that connects to your VPS and creates a TCP tunnel.

---

## Setup

1. Set up `frps` on your VPS — see the [frp documentation](https://github.com/fatedier/frp)
2. Download `frpc.exe` for Windows
3. In **Settings → Tunnels → frp**: set the path to `frpc.exe`, the VPS address, and the authentication token
4. Create a tunnel from the Tunnel page

---

## Command Template

```
{exe} tcp --server_addr {serverAddr}:{serverPort} --token {token} --local_port {port} --remote_port {port}
```

---

## Advantages Over Hosted Providers

- The public IP/port is stable and under your control
- No rate limits or session timeouts
- Can support UDP if your frp server is configured for it
- Traffic stays on your own infrastructure

---

## Notes

- Requires maintaining your own server — not suitable for casual sharing
- Server cost is typically €5–10/month for a basic VPS
- If the VPS goes down, the tunnel stops working
