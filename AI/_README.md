# AI

Natural-language assistant layer for Minecraft Control Hub.

## Architecture

The AI layer follows a strict boundary rule: **the AI never touches files, processes, or the
system directly.** It only produces structured `AICommandBatch` objects. A separate executor
validates and runs them. This boundary is enforced in every file in this folder.

```
User message
    → AITerminalService (builds conversation + calls AIService)
    → IAIService (OpenAI / Gemini HTTP call)
    → AI response text (contains embedded JSON action plan)
    → AiTerminalViewModel (parses JSON, shows confirmation card)
    → User clicks Execute
    → AICommandExecutor (validates + dispatches to Core services)
    → Core services (InstallationService, ServerService, ModService, …)
```

## Implemented

### Services
| File | Responsibility |
|------|---------------|
| `AIService.cs` | HTTP client for OpenAI-compatible and Google Gemini APIs. Supports streaming (`StreamAsync`) and non-streaming (`CompleteAsync`). Includes `GetGeminiModelsAsync` to fetch live available models for the current API key. All error responses surface the full API error body instead of a generic HTTP exception. |
| `AITerminalService.cs` | Orchestrates the conversation loop. Builds the system prompt via `KnowledgeService`, maintains conversation history, streams AI responses token-by-token, and parses embedded JSON action plans. |
| `AICommandExecutor.cs` | Executes validated `AICommandBatch` objects. Dispatches to Core services. Supported actions: `CreateInstallation`, `DeleteInstallation`, `InstallMod`, `RemoveMod`, `CreateServer`, `StartServer`, `StopServer`, `DeleteServer`, `LaunchInstallation`, `EnableTunnel`, `DisableTunnel`. `InstallMod`/`RemoveMod` accept both installation names and server names as target. |
| `KnowledgeService.cs` | Builds the system prompt by combining static knowledge files with a **live context snapshot** of the user's current installations (with installed mods) and servers, fetched fresh on every conversation turn. |

### Models
| File | Description |
|------|-------------|
| `AICommand.cs` | `AICommand`, `AICommandBatch`, `AICommandResult`, `AICommandBatchResult` — the structured action plan format the AI embeds in its responses. |
| `TerminalMessage.cs` | Observable message model for the AI terminal UI. Raises `PropertyChanged` on the UI thread (safe to set from background threads). Types: `User`, `AI`, `ActionPlan`, `ExecutionResult`. Includes `IsDone` flag so the "Executing…" indicator hides correctly after completion. |

### Knowledge files (`/Knowledge`)
| File | Content |
|------|---------|
| `CommandKnowledge.json` | Full command reference with parameters and examples for all supported AI actions. |
| `MinecraftKnowledge.json` | Minecraft version history, loader compatibility matrix, common mistakes (including explicit Forge ≠ NeoForge rule). |
| `ModKnowledge.json` | Mod categories, popular mods per category, loader compatibility notes. |
| `SpecialCases.json` | Edge cases: CurseForge vs Modrinth, Forge vs NeoForge on Modrinth, platform-specific notes. |

## Hard rules (unchanged from original spec)
- The AI layer never imports or calls anything from `Core.Services` directly — only `AICommandExecutor` does that.
- `AICommandExecutor` is the only class allowed to mutate app state.
- The AI response is always shown for user confirmation before execution (except for read-only queries).
