# Knowledge Base System

## Overview

`KnowledgeService` builds the system prompt that is sent to the AI on every conversation turn. It combines static JSON knowledge files with a live context snapshot of the user's current state, so the AI always has accurate, up-to-date information.

---

## IKnowledgeService

```csharp
public interface IKnowledgeService
{
    Task<string> BuildSystemPromptAsync();
}
```

Called by `AITerminalService` at the start of every user message. A fresh prompt is built each time — the live context is never cached between turns.

---

## Static Knowledge Files

Located in `AI/Knowledge/`:

| File | Content |
|---|---|
| `CommandKnowledge.json` | Full command reference: all supported AI actions with required parameters, optional parameters, and usage examples |
| `MinecraftKnowledge.json` | Minecraft version history, loader compatibility matrix (which loaders support which versions), and common mistakes (e.g. explicit rule that Forge ≠ NeoForge) |
| `ModKnowledge.json` | Mod categories (performance, utility, magic, etc.), popular mods per category, loader compatibility notes |
| `SpecialCases.json` | Edge cases: CurseForge vs. Modrinth availability differences, Forge vs. NeoForge on Modrinth, platform-specific notes |
| `TunnelKnowledge.json` | Tunnel provider descriptions, setup requirements, UDP/TCP support, recommended providers |

---

## Live Context

`BuildLiveContextAsync()` reads the current state of the app and formats it as a text snapshot:

```
Current installations:
- "Fabric 1.21.1" (Fabric, 1.21.1)
  Installed mods: Sodium, Lithium, Iris

- "Forge Pack" (Forge, 1.20.1)
  Installed mods: JEI, Create, Botania

Current servers:
- "Survival Server" (Paper, 1.21.1) — RUNNING on port 25565
- "Test Server" (Fabric, 1.20.1) — STOPPED
```

This snapshot is appended to the system prompt so the AI knows exactly what exists — no guessing, no hallucination about current state.

---

## System Prompt Assembly

```csharp
public async Task<string> BuildSystemPromptAsync()
{
    var sb = new StringBuilder();

    // 1. Role and boundary rules
    sb.AppendLine("You are the Nexora Launcher AI assistant...");
    sb.AppendLine("You NEVER execute actions without user confirmation.");
    sb.AppendLine("You ALWAYS embed a JSON action plan in your response.");

    // 2. Static knowledge
    sb.AppendLine(await LoadStaticKnowledgeAsync());

    // 3. Live context
    sb.AppendLine(await BuildLiveContextAsync());

    return sb.ToString();
}
```

---

## Embedded JSON Format

The AI is instructed to embed a JSON action plan at the end of every response that proposes actions:

```json
```actions
{
  "commands": [
    { "action": "CreateInstallation", "parameters": { "name": "Fabric 1.21.1", "version": "1.21.1", "loader": "Fabric" } },
    { "action": "InstallMod", "parameters": { "modName": "Sodium", "target": "Fabric 1.21.1" } }
  ]
}
```
```

`AITerminalService` strips this block from the displayed AI message (so the user sees clean prose) and parses it into an `AICommandBatch`.
