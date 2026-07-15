# bore.pub Provider

## Overview

bore is a lightweight open-source TCP tunnel. No account, no authentication, no configuration — just run the binary and share the address.

---

## Characteristics

| Property | Value |
|---|---|
| TCP | ✓ |
| UDP | ✗ |
| Auth required | None |
| Address discovery | StdoutRegex |
| Tier | Free |

---

## Setup

1. Download the bore binary for Windows from the [bore GitHub releases](https://github.com/ekzhang/bore)
2. In **Settings → Tunnels → bore**: set the path to `bore.exe`
3. Create a tunnel from the Tunnel page — bore connects to `bore.pub` and the public address appears automatically

---

## Command Template

```
{exe} local {port} --to bore.pub:7835
```

## Address Discovery

bore prints the public address to stdout on startup:

```
listening at bore.pub:XXXXX
```

`TunnelSession` captures this with a StdoutRegex match.

---

## Notes

- bore.pub is a public community server — do not use it for sensitive traffic
- TCP only — not suitable for Bedrock Edition or Geyser
- The binary is a single self-contained executable (~5 MB)
