# playit.gg Provider

## Overview

playit.gg is the recommended tunnel provider for Minecraft servers. It is purpose-built for game hosting, supports both TCP and UDP (enabling Bedrock Edition connections via Geyser), and the free tier requires no account or authentication.

---

## Why playit.gg is Recommended

- **UDP support** — the only free provider that supports UDP. Required for Bedrock Edition and Geyser.
- **No authentication** — the free tier works immediately without signing up.
- **Game-focused** — addresses are on `joinmc.link` and `playit.gg` domains, which Minecraft clients recognize.
- **Recommendation score bonus** — +20 points in the provider scoring algorithm, making it rank first for game servers.

---

## Free Tier — Manual Mode

The free tier works through the official playit Windows wizard, which runs as a persistent background service. The launcher does not launch or manage the playit process in this mode — the wizard must already be running.

**Setup:**
1. Download and install the official playit wizard from [playit.gg](https://playit.gg)
2. Run the wizard; it will show your public tunnel addresses
3. In the launcher Tunnel page, click **Create Tunnel** and select playit.gg
4. Paste the address shown in the wizard into the launcher

This is the `Manual` address source mode — the launcher displays the address the user provides, and optionally shares it with friends.

---

## Premium Mode — StdoutRegex

The premium playit binary supports being launched with a secret key. The launcher starts the process and discovers the address by parsing stdout:

**Known stdout patterns:**
```
connect address: whacking-unbroken.gl.joinmc.link
Connect address: something.playit.gg:25565
tunnel address: something.mc.gl:19132
Allocated address: foo.joinmc.link:25565
● whacking-unbroken.gl.joinmc.link => 127.0.0.1:25565
```

The regex is broad enough to match all observed formats from different playit versions.

---

## TunnelProvider Configuration

```json
{
    "id": "playit",
    "displayName": "playit.gg",
    "tier": "freemiumLimited",
    "supportsTcp": true,
    "supportsUdp": true,
    "requiresAuth": false,
    "addressSource": "Manual",
    "remoteApiUrl": "https://api.playit.gg",
    "secretKeyPath": "%LOCALAPPDATA%\\MinecraftControlHub\\tunnel-configs\\playit.toml",
    "commandTemplate": "{exe} --port {port} --protocol {protocol}"
}
```

---

## Bedrock / Geyser

playit.gg is the only provider in the registry that supports UDP on the free tier. When a server uses Geyser (which listens on a separate UDP port), add a second tunnel assignment for the Bedrock port and select playit.gg.
