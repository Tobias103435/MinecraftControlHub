# AI Command Processing System

## Overview

The AI layer provides a natural language interface for controlling the launcher. Users type plain English commands; the AI produces a structured action plan; the user confirms; the launcher executes.

```
User message
    → AITerminalService  (builds conversation + calls AIService)
    → IAIService         (OpenAI / Gemini HTTP call, streaming)
    → AI response text   (contains embedded JSON action plan)
    → AiTerminalViewModel (parses JSON, shows confirmation card)
    → User clicks Execute
    → AICommandExecutor  (validates + dispatches to Core services)
    → Core services      (InstallationService, ServerService, ModService, …)
```

**Hard rule:** The AI never touches files, processes, or system state directly. `AICommandExecutor` is the only class allowed to mutate app state.

---

## Components

| Component | File | Responsibility |
|---|---|---|
| `AIService` | `AI/Services/AIService.cs` | HTTP client for OpenAI-compatible and Gemini APIs. Supports streaming and non-streaming. Includes Gemini model discovery. |
| `AITerminalService` | `AI/Services/AITerminalService.cs` | Orchestrates the conversation loop. Builds system prompt, maintains history, streams responses, parses embedded JSON. |
| `AICommandExecutor` | `AI/Services/AICommandExecutor.cs` | Executes validated `AICommandBatch` objects. Dispatches to Core services. |
| `KnowledgeService` | `AI/Services/KnowledgeService.cs` | Builds the system prompt by combining static knowledge files with a live context snapshot. |

---

## Models

| Model | Description |
|---|---|
| `AICommand` | Single action: `Action` string + `Parameters` dictionary |
| `AICommandBatch` | List of `AICommand` objects extracted from one AI response |
| `AICommandResult` | Result of executing one command: success/failure + message |
| `AICommandBatchResult` | Aggregated results for all commands in a batch |
| `TerminalMessage` | Observable UI message. Types: `User`, `AI`, `ActionPlan`, `ExecutionResult`. Thread-safe `IsDone` flag. |

---

## Supported Actions

| Action | Parameters |
|---|---|
| `CreateInstallation` | `name`, `version`, `loader` |
| `DeleteInstallation` | `name` |
| `LaunchInstallation` | `name` |
| `InstallMod` | `modName`, `target` (installation or server name) |
| `RemoveMod` | `modName`, `target` |
| `CreateServer` | `name`, `version`, `type`, `ram` |
| `StartServer` | `name` |
| `StopServer` | `name` |
| `DeleteServer` | `name` |
| `EnableTunnel` | `serverName`, `provider` |
| `DisableTunnel` | `serverName` |

---

## Crash Auto-Diagnosis

When a managed server crashes, the crash report is automatically forwarded to the AI terminal:

```csharp
serverService.ServerCrashed += (_, args) =>
{
    Dispatcher.InvokeAsync(() => _ = aiVm.AskAiAboutErrorAsync(args.CrashReport));
};
```

No user input required — the AI starts diagnosing immediately.

---

## Setup

Go to **Settings → AI Terminal** and configure:

- **Provider** — `OpenAI`, `Gemini`, or `Custom`
- **API Key** — from your provider's dashboard
- **Model** — e.g. `gpt-4o`, `gemini-2.5-flash`
- **Custom endpoint** — any OpenAI-compatible base URL (only for `Custom` provider)

For Gemini, the Settings page includes a live model discovery button that fetches available models for your API key.

---

## Related Pages

- [AI Architecture and Design](AI-Architecture-and-Design)
- [AI Terminal Interface](AI-Terminal-Interface)
- [Knowledge Base System](Knowledge-Base-System)
- [Natural Language Command Processing](Natural-Language-Command-Processing)
- [Command Parsing and Validation](Command-Parsing-and-Validation)
- [Streaming Response Processing](Streaming-Response-Processing)
