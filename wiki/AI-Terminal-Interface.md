# AI Terminal Interface

## Overview

The AI terminal is a chat-like UI panel where users type natural language commands. It renders streaming token output in real time, shows confirmation cards for proposed action plans, and displays execution results.

---

## TerminalMessage Model

```csharp
public class TerminalMessage : INotifyPropertyChanged
{
    public TerminalMessageType Type { get; }   // User | AI | ActionPlan | ExecutionResult
    public string Content { get; private set; }
    public bool IsDone { get; private set; }

    // Thread-safe — raises PropertyChanged on UI thread
    public void Append(string token) { ... }
    public void SetDone() { ... }
}

public enum TerminalMessageType
{
    User,
    AI,
    ActionPlan,
    ExecutionResult
}
```

All property changes are dispatched to the UI thread internally, so background streaming tasks can safely call `Append()` and `SetDone()` without explicit `Dispatcher.InvokeAsync` calls at the call site.

---

## Streaming Flow

```csharp
// In AITerminalService
var message = new TerminalMessage(TerminalMessageType.AI);
Messages.Add(message);

await foreach (var token in _ai.StreamAsync(history, cancellationToken))
{
    message.Append(token);
}

message.SetDone(); // hides the "typing" indicator
```

The `IsDone` flag drives a `DataTrigger` in XAML that hides the animated ellipsis shown while the AI is still responding.

---

## Action Plan Confirmation Card

After streaming completes, `AITerminalService` strips the embedded JSON from the displayed text and parses it into an `AICommandBatch`. An `ActionPlan` message is added showing each proposed action:

```
AI: I'll create a Fabric installation for 1.21.1 and install Sodium.

  ┌─ Proposed Actions ────────────────────────────────┐
  │  1. CreateInstallation  name="Fabric 1.21.1"       │
  │     version="1.21.1"   loader="Fabric"             │
  │                                                    │
  │  2. InstallMod  modName="Sodium"                   │
  │     target="Fabric 1.21.1"                         │
  │                                                    │
  │  [Execute]  [Cancel]                               │
  └────────────────────────────────────────────────────┘
```

Clicking **Execute** calls `AICommandExecutor.ExecuteAsync(batch)`. Results appear as `ExecutionResult` messages below.

---

## AiTerminalViewModel

`AiTerminalViewModel` is the ViewModel for the AI page. Key responsibilities:

- Holds `ObservableCollection<TerminalMessage> Messages`
- Exposes `SendCommand` (ICommand) for the input field
- Exposes `ExecuteCommand` and `CancelCommand` for confirmation cards
- Wires `IsLoading` for the send button disable state
- Exposes `AskAiAboutErrorAsync(string errorContext)` — called by `App.xaml.cs` on server crash

---

## Configuration Check

If `AIService.IsConfigured` is false (no API key in settings), the terminal shows an inline warning instead of sending the message:

```
⚠ AI is not configured. Go to Settings → AI Terminal to add an API key.
```
