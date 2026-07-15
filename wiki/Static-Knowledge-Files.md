# Static Knowledge Files

## Overview

Static knowledge files are JSON files bundled in `AI/Knowledge/`. They are loaded by `KnowledgeService` and included in every system prompt to give the AI accurate, project-specific knowledge it could not infer from its training data alone.

---

## Files

### CommandKnowledge.json

The primary reference for what the AI can do. Contains:

- All supported action names (`CreateInstallation`, `InstallMod`, etc.)
- Required and optional parameters for each action
- Parameter validation rules (valid loaders, naming constraints)
- Example commands showing correct parameter usage
- What to do when a parameter is ambiguous (e.g. ask the user to clarify)

This file is the single source of truth for what `AICommandExecutor` supports. If an action is not in this file, the AI will not propose it.

### MinecraftKnowledge.json

Minecraft-specific facts the AI needs to give correct advice:

- Minecraft version history and release dates
- Loader compatibility matrix (which Fabric/Forge/NeoForge/Quilt versions support which MC versions)
- Java version requirements per MC version
- Common mistakes — explicitly includes: "Forge and NeoForge are NOT the same. Do not confuse them."
- Known breaking changes between major Minecraft versions

### ModKnowledge.json

Mod ecosystem knowledge:

- Popular mod categories (performance, optimization, utility, magic, tech, etc.)
- Well-known mods per category with brief descriptions
- Loader compatibility notes (e.g. "Sodium is Fabric/Quilt only — use Embeddium for Forge")
- Common mod bundles (e.g. Sodium + Lithium + Iris is the standard performance trio for Fabric)

### SpecialCases.json

Edge cases and platform differences that trip up AI models:

- Some mods are only on CurseForge, not Modrinth
- Forge mods on Modrinth may be listed under "NeoForge" for newer versions
- The official Minecraft launcher profile import path
- Offline mode limitations (no skin, no chat signing)

### TunnelKnowledge.json

Tunnel provider reference:

- Provider names, descriptions, and setup requirements
- Authentication requirements (ngrok needs authtoken, playit.gg free tier does not)
- TCP vs UDP support (playit.gg supports UDP, others are TCP-only on free tier)
- Recommended provider: playit.gg for game servers, ngrok for general use
- How each provider's address discovery works (stdout, local API, manual paste)
