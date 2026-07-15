# System Design & Architecture Patterns

## Design Principles

The application is built around four core principles:

1. **Separation of concerns** — UI knows nothing about HTTP; Core knows nothing about WPF
2. **Interface-first** — every service has an `I`-prefixed interface; implementations are swappable
3. **User confirmation before mutation** — the AI never runs actions without explicit user approval
4. **Fail-safe defaults** — missing files, corrupt JSON, and failed API calls produce sensible defaults rather than crashes

---

## Layered Architecture

```
UI (WPF)        → depends on Core interfaces + AI interfaces
Core            → depends on nothing application-specific
AI              → depends on Core interfaces (only via AICommandExecutor)
```

No circular dependencies. No service locator. No static state outside of `AppPaths`.

---

## Key Patterns

### Repository Pattern (Services as Repositories)

`InstallationService` and `ServerService` act as in-memory repositories backed by JSON files. They load on first access, fire change events, and persist on mutation:

```csharp
public async Task<Installation> CreateInstallationAsync(Installation installation)
{
    _installations.Add(installation);
    Persist(); // writes installations.json
    InstallationsChanged?.Invoke(this, EventArgs.Empty);
    return installation;
}
```

### Command Pattern (AI Actions)

The AI produces structured `AICommandBatch` objects. Each command has an `Action` string and a `Parameters` dictionary. `AICommandExecutor` routes by action name:

```csharp
case "CreateInstallation":
    await _installationService.CreateInstallationAsync(...);
    break;
case "InstallMod":
    await _modService.InstallModAsync(...);
    break;
```

This decouples the AI's natural language reasoning from the actual service calls.

### Observer Pattern (Events)

Services fire `EventHandler` events when state changes. ViewModels subscribe and update their `ObservableCollection`s. This means the UI always reflects the current state without polling.

### Strategy Pattern (Tunnel Providers)

Each tunnel provider is a `TunnelProvider` model with an `AddressSource` enum value (`StdoutRegex`, `LocalApi`, `RemoteApi`, `Manual`). `TunnelSession` uses the address source to determine how to discover the public tunnel address — same session code, different strategy.

### Factory Pattern (Server Provisioning)

`ServerProvisioningService` acts as a factory: given a server type string (`"paper"`, `"fabric"`, `"forge"`, etc.), it selects the correct download logic and returns a provisioned server directory.

---

## Data Flow

### User Action → UI Update

```
User clicks "New Installation"
  → HomePageViewModel.CreateInstallationCommand.Execute()
  → InstallationService.CreateInstallationAsync()
  → InstallationsChanged event fires
  → HomePageViewModel subscribes → calls LoadInstallationsAsync()
  → Installations ObservableCollection updated
  → ListView in HomePage re-renders
```

### AI Command → Core Execution

```
User types message
  → AITerminalService.SendMessageAsync()
  → KnowledgeService.BuildSystemPromptAsync() (includes live context)
  → IAIService.StreamAsync() → token stream
  → AiTerminalViewModel renders tokens live
  → JSON action plan parsed from response
  → Confirmation card shown
  → User clicks Execute
  → AICommandExecutor.ExecuteAsync(batch)
  → Core services mutate state
  → Services fire change events → UI updates
```
