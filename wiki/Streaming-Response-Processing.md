# Streaming Response Processing

## Overview

Both OpenAI and Gemini support server-sent events (SSE) streaming. `AIService` implements `IAsyncEnumerable<string>` streaming for both providers, yielding individual text tokens as they arrive.

---

## OpenAI Streaming

```csharp
private async IAsyncEnumerable<string> OpenAIStreamAsync(
    IReadOnlyList<ChatMessage> messages,
    [EnumeratorCancellation] CancellationToken cancellationToken)
{
    // Send request with stream: true
    using var response = await _http.SendAsync(
        BuildOpenAIRequest(messages, stream: true),
        HttpCompletionOption.ResponseHeadersRead,
        cancellationToken);

    await EnsureSuccessAsync(response, cancellationToken);

    await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
    using var reader = new StreamReader(stream);

    while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
    {
        var line = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrEmpty(line) || !line.StartsWith("data: ")) continue;

        var data = line["data: ".Length..];
        if (data == "[DONE]") yield break;

        var delta = JsonNode.Parse(data)?["choices"]?[0]?["delta"]?["content"]?.GetValue<string>();
        if (delta != null) yield return delta;
    }
}
```

SSE lines look like:
```
data: {"choices":[{"delta":{"content":"Hello"}}]}
data: {"choices":[{"delta":{"content":" world"}}]}
data: [DONE]
```

---

## Gemini Streaming

Gemini uses the same SSE format but with a different JSON structure:

```csharp
var text = node?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.GetValue<string>();
```

The stream URL uses `?alt=sse` to enable SSE mode:
```
POST https://generativelanguage.googleapis.com/v1beta/models/{model}:streamGenerateContent?alt=sse&key={apiKey}
```

---

## TerminalMessage.Append

Tokens are appended to the active `TerminalMessage` as they arrive:

```csharp
await foreach (var token in _ai.StreamAsync(messages, cancellationToken))
{
    activeMessage.Append(token);   // thread-safe, dispatches to UI thread
}
activeMessage.SetDone();           // hides "typing" indicator
```

`Append()` is designed to be called from background threads — it dispatches `PropertyChanged` to the UI thread internally.

---

## IsDone Flag

The `IsDone` flag on `TerminalMessage` controls the animated "typing" indicator in the UI:

```csharp
public void SetDone()
{
    App.Current.Dispatcher.InvokeAsync(() =>
    {
        IsDone = true;
        OnPropertyChanged(nameof(IsDone));
    });
}
```

A `DataTrigger` in the `TerminalMessage` ItemTemplate hides the ellipsis animation when `IsDone` is `true`.

---

## StripStreamingJson

After the stream ends, the embedded JSON action plan is removed from the displayed text:

```csharp
private static string StripStreamingJson(string text)
    => Regex.Replace(text, @"```actions\s*\{.*?\}\s*```", string.Empty, RegexOptions.Singleline).Trim();
```

The user sees clean prose. The JSON is preserved separately for the confirmation card.
