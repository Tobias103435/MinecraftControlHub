# Dynamic Context Integration

## Overview

The AI's live context is a text snapshot of the user's current installations and servers, built fresh on every conversation turn. This ensures the AI never suggests actions based on stale state.

---

## What Is Included

For every turn, the live context snapshot contains:

- **Installations** — name, loader, Minecraft version, list of installed mod names
- **Servers** — name, server type, Minecraft version, running/stopped status, port

---

## Why Per-Turn, Not Cached

Minecraft state changes frequently during a conversation:
- A server might crash while the user is asking a follow-up question
- The user might install a mod through the UI while the AI terminal is open
- An installation might be deleted during a multi-step AI conversation

By rebuilding the context on every turn, the AI always sees accurate state.

---

## Example Context Block

```
=== CURRENT STATE ===

Installations:
- "Survival Fabric" (Fabric, 1.21.1)
  Mods: Sodium, Lithium, Iris, Fabric API

- "Old Forge Pack" (Forge, 1.20.1)
  Mods: JEI, Create, Botania, Applied Energistics 2

Servers:
- "Survival SMP" (Paper, 1.21.1) — RUNNING on port 25565
- "Dev Server" (Vanilla, 1.21.4) — STOPPED
```

---

## Impact on AI Behavior

Without live context, the AI would have to ask the user "what installations do you have?" for every request. With it, commands like:

- *"Install Sodium in my Fabric installation"* → AI sees exactly one Fabric installation, uses its name
- *"Start the survival server"* → AI matches "survival" to "Survival SMP"
- *"What mods are in my modpack?"* → AI lists mods from the context, no API call needed
