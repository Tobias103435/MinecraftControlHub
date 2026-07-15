# serveo.net Provider

## Overview

serveo is an SSH-based tunnel service. No binary download is required — it uses the system's built-in SSH client.

---

## Characteristics

| Property | Value |
|---|---|
| TCP | ✓ |
| UDP | ✗ |
| Auth required | None |
| Address discovery | StdoutRegex |
| Installation | None (uses system SSH) |
| Tier | Free |

---

## How It Works

serveo uses SSH remote port forwarding. When the launcher creates a serveo tunnel, it runs:

```
ssh -R 0:localhost:{port} serveo.net
```

SSH is available on all modern Windows 10/11 systems. No binary download is needed.

The serveo server assigns a random public address and prints it to stdout:

```
Forwarding TCP connections from tcp://serveo.net:XXXXX
```

---

## Setup

No setup required beyond having SSH available on your system (it is included in Windows 10/11 by default). Simply select serveo.net as your tunnel provider in the Tunnel page and create a tunnel.

---

## Notes

- serveo.net has occasional downtime — if the tunnel fails to connect, try another provider
- TCP only — not suitable for Bedrock Edition or Geyser
- The assigned port changes each session
