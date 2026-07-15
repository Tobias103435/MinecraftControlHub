# AI Architecture and Design

## Architectural Boundary

The AI layer enforces a strict boundary: **the AI only produces data structures, never executes side effects.** This boundary is enforced in every file in the `AI/` folder.

```
AI Layer                          Core Layer
─────────────────────────         ─────────────────────────
AIService          (HTTP)         InstallationService
AITerminalService  (orchestration) ServerService
KnowledgeService   (context)      ModService
                                  TunnelService
AICommandExecutor ──────────────► (only class that crosses
                                   the boundary)
```

`AICommandExecutor` is the only class in the AI layer that imports anything from `Core.Services`.

---

## AIService

`AIService` is a dual-protocol HTTP client that supports both OpenAI-compatible APIs and Google Gemini.

### Provider Selection

```csharp
public bool IsGemini =>
    string.Equals(_settings.Settings.AiProvider, "Gemini",
        StringComparison.OrdinalIgnoreCase);

public Task<string> CompleteAsync(IReadOnlyList<ChatMessage> messages, ...)
    => IsGemini ? GeminiCompleteAsync(...) : OpenAICompleteAsync(...);

public IAsyncEnumerable<string> StreamAsync(IReadOnlyList<ChatMessage> messages, ...)
    => IsGemini ? GeminiStreamAsync(...) : OpenAIStreamAsync(...);
```

### OpenAI Request Format

```json
{
    "model": "gpt-4o",
    "stream": true,
    "messages": [
        { "role": "system", "content": "..." },
        { "role": "user",   "content": "..." }
    ]
}
```

### Gemini Request Format

Gemini uses a different format — the system message is extracted and sent as `systemInstruction`, while the rest become `contents` with role `user`/`model` (not `assistant`):

```json
{
    "systemInstruction": { "parts": [{ "text": "..." }] },
    "contents": [
        { "role": "user",  "parts": [{ "text": "..." }] },
        { "role": "model", "parts": [{ "text": "..." }] }
    ]
}
```

### Error Handling

All responses go through `EnsureSuccessAsync`, which reads the response body before throwing:

```csharp
throw new HttpRequestException(
    $"API request failed ({(int)response.StatusCode} {response.ReasonPhrase}): {body}",
    statusCode: response.StatusCode);
```

This ensures the full API error message (e.g. "invalid api key", "model not found") surfaces in the terminal rather than a generic HTTP exception.

### Gemini Model Discovery

`GetGeminiModelsAsync` fetches all available Gemini models for a given API key, filters to only `generateContent`-capable models, and sorts stable models before preview/experimental ones:

```csharp
public async Task<List<string>> GetGeminiModelsAsync(string apiKey, ...)
```

---

## AITerminalService

`AITerminalService` owns the conversation loop:

1. User message arrives → appended to conversation history
2. `KnowledgeService.BuildSystemPromptAsync()` builds a fresh system prompt (includes live context)
3. `IAIService.StreamAsync()` streams the response token by token
4. Each token is appended to the active `TerminalMessage`
5. After streaming completes, `StripStreamingJson()` removes the embedded JSON from the displayed text
6. The JSON action plan is parsed into an `AICommandBatch`
7. An `ActionPlan` message is added to the terminal for the user to review
8. On user confirmation → `AICommandExecutor.ExecuteAsync(batch)`

---

## KnowledgeService

Builds the system prompt on every conversation turn:

```csharp
public async Task<string> BuildSystemPromptAsync()
{
    var staticKnowledge = LoadStaticKnowledge();   // JSON files
    var liveContext     = await BuildLiveContextAsync(); // current installations + servers
    return Combine(staticKnowledge, liveContext);
}
```

The live context means the AI always knows what installations and servers currently exist — it never hallucinates about state it can't see.

---

## AICommandExecutor

Routes `AICommand` objects to Core services by the `Action` string:

```csharp
public async Task<AICommandResult> ExecuteAsync(AICommand command)
{
    return command.Action switch
    {
        "CreateInstallation" => await CreateInstallationAsync(command.Parameters),
        "DeleteInstallation" => await DeleteInstallationAsync(command.Parameters),
        "InstallMod"         => await InstallModAsync(command.Parameters),
        "CreateServer"       => await CreateServerAsync(command.Parameters),
        "StartServer"        => await StartServerAsync(command.Parameters),
        // ...
        _ => AICommandResult.Failure($"Unknown action: {command.Action}")
    };
}
```
