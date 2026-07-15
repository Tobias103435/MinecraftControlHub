# Conversation History and Context Management

## Overview

`AITerminalService` maintains a sliding conversation history that is passed to the AI on every turn. This allows multi-turn conversations where the AI remembers what was said and done earlier in the session.

---

## History Format

Each entry in the history is a `ChatMessage`:

```csharp
public record ChatMessage(string Role, string Content);
// Role: "system" | "user" | "assistant"
```

The full messages list passed to `IAIService.StreamAsync`:

```csharp
var messages = new List<ChatMessage>
{
    new("system", await _knowledge.BuildSystemPromptAsync()),
    ..._history,
    new("user", userMessage)
};
```

---

## History Window

To avoid exceeding model context limits, the history is trimmed to the most recent N turns. The system prompt is always included and never trimmed.

```csharp
private readonly List<ChatMessage> _history = new();
private const int MaxHistoryPairs = 10; // 10 user + 10 assistant = 20 messages

private IReadOnlyList<ChatMessage> GetTrimmedHistory()
{
    if (_history.Count <= MaxHistoryPairs * 2) return _history;
    return _history.TakeLast(MaxHistoryPairs * 2).ToList();
}
```

---

## History Update

After each completed turn, both the user message and the AI response are appended to history:

```csharp
_history.Add(new ChatMessage("user", userMessage));
// ... stream response ...
_history.Add(new ChatMessage("assistant", fullResponseText));
```

Note: the full response text (before JSON stripping) is stored in history, so the AI can refer back to previously proposed action plans in subsequent turns.

---

## Session Reset

The conversation history is per-session — it starts empty when the app opens and is not persisted to disk. Navigating away from the AI page and back does not reset the history (the ViewModel is cached). Explicitly clearing the terminal resets it.

---

## System Prompt Freshness

The system prompt is rebuilt on every turn via `KnowledgeService.BuildSystemPromptAsync()`. This means the live context (current installations, servers, their states) is always fresh — if a server was started or stopped during the conversation, the AI will know about it on the next turn.
