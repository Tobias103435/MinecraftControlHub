# Natural Language Command Processing

## Overview

`AITerminalService` is the orchestrator of the conversation loop. It takes a user message, builds the full conversation context, streams the AI response, and extracts the action plan.

---

## Conversation Flow

```
1. User submits message
2. AITerminalService.SendMessageAsync(userMessage)
3. KnowledgeService.BuildSystemPromptAsync()  → fresh system prompt
4. Conversation history assembled:
   [ system, ...history, user_message ]
5. IAIService.StreamAsync(messages)
6. Tokens streamed → TerminalMessage.Append(token) live
7. Stream ends → message.SetDone()
8. StripStreamingJson() removes JSON block from displayed text
9. ParseActionPlan() extracts AICommandBatch
10. ActionPlan TerminalMessage added to Messages
11. User clicks Execute → AICommandExecutor.ExecuteAsync(batch)
```

---

## Prompt Construction

The messages list passed to `IAIService`:

```csharp
var messages = new List<ChatMessage>
{
    new("system", systemPrompt),          // built by KnowledgeService
    ..._conversationHistory,               // previous turns
    new("user", userMessage)              // current input
};
```

The `system` role is handled differently by each provider:
- **OpenAI**: sent as a `{ "role": "system", ... }` message
- **Gemini**: extracted and sent as `systemInstruction` (Gemini does not support `system` role in `contents`)

---

## StripStreamingJson

The AI is instructed to embed a JSON block in its response. Before displaying the final message, this block is stripped so the user only sees the prose explanation:

```csharp
private static string StripStreamingJson(string text)
{
    // Removes ```actions ... ``` blocks from the displayed AI message
    return Regex.Replace(text, @"```actions\s*\{.*?\}\s*```", "", RegexOptions.Singleline);
}
```

---

## ParseActionPlan

```csharp
private AICommandBatch? ParseActionPlan(string responseText)
{
    var match = Regex.Match(responseText, @"```actions\s*(\{.*?\})\s*```", RegexOptions.Singleline);
    if (!match.Success) return null;

    return JsonSerializer.Deserialize<AICommandBatch>(match.Groups[1].Value, _options);
}
```

If no JSON block is found (e.g. for a question that doesn't require actions), no confirmation card is shown and the AI message is displayed as-is.

---

## Read-Only Queries

Not all user messages require actions. If the AI responds without an embedded JSON block, the response is shown directly without a confirmation card. Examples:

- "What mods are installed in my Fabric pack?" → AI lists mods from live context, no action required
- "Why is my server lagging?" → AI explains causes, suggests solutions, no action unless user asks to apply them
