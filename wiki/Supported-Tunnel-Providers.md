# Supported Tunnel Providers

## At a Glance

| Provider | Free | TCP | UDP | Auth | Address Discovery | Notes |
|---|---|---|---|---|---|---|
| **playit.gg** | ✓ | ✓ | ✓ | None | Manual (free) | **Recommended** — purpose-built for game servers |
| **playit Pro** | Paid | ✓ | ✓ | `playit.toml` secret | RemoteApi | Dedicated IP, custom domain |
| **ngrok** | ✓ | ✓ | ✗ | Authtoken | LocalApi (4040) | Most widely known; TCP only on free |
| **ngrok Pro** | Paid | ✓ | ✗ | Authtoken | LocalApi | Named domains, higher limits |
| **bore.pub** | ✓ | ✓ | ✗ | None | StdoutRegex | Minimal, no setup |
| **serveo.net** | ✓ | ✓ | ✗ | None (SSH) | StdoutRegex | No binary needed |
| **frp** | ✓* | ✓ | ✗ | Server token | StdoutRegex | Self-hosted only |

*frp requires your own server to host the frp server component.

---

## Choosing a Provider

**For Minecraft Java Edition only:**
Any provider works. ngrok or bore are good for quick sharing without any setup.

**For Minecraft Bedrock Edition or Geyser (Java + Bedrock):**
Use **playit.gg** — it is the only free provider that supports UDP. Other providers are TCP-only on their free tier, which means Bedrock clients cannot connect.

**For a permanent, always-on server:**
Consider **playit Pro** (dedicated IP that never changes) or **frp** on your own VPS.

**For testing or one-time sharing:**
bore.pub or serveo.net — no setup, no account, just run and paste.

---

## Setup Summary

| Provider | What you need to download | Auth setup |
|---|---|---|
| playit.gg | [Official playit wizard](https://playit.gg) (Windows installer) | None |
| playit Pro | playit binary | Secret key in `tunnel-configs/playit.toml` (managed by wizard) |
| ngrok | `ngrok.exe` | Authtoken from dashboard.ngrok.com → Settings → Tunnels |
| bore | `bore.exe` | None |
| serveo | Nothing (uses system SSH) | None |
| frp | `frpc.exe` | Server address + token → Settings → Tunnels |

---

## Detailed Pages

- [playit.gg Provider](playit.gg-Provider)
- [playit Pro Provider](playit-Pro-Provider)
- [ngrok Providers (Free & Pro)](ngrok-Providers)
- [bore.pub Provider](bore.pub-Provider)
- [serveo.net Provider](serveo.net-Provider)
- [FRP Provider](FRP-Provider)
