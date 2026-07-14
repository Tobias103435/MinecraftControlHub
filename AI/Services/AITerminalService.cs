using System.Text.Json;
using System.Text.RegularExpressions;
using MinecraftControlHub.AI.Models;

namespace MinecraftControlHub.AI.Services;

public class AITerminalResponse
{
    public string TextResponse { get; set; } = string.Empty;
    public AICommandBatch? ActionPlan { get; set; }
    public bool RequiresConfirmation => ActionPlan?.Commands.Count > 0;
}

public class AITerminalService
{
    private readonly IAIService         _ai;
    private readonly IKnowledgeService  _knowledge;
    private readonly AICommandExecutor  _executor;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Matches a fenced JSON block: ```json ... ``` or ``` ... ``` (with a JSON object inside).
    /// </summary>
    private static readonly Regex JsonBlockRegex = new(
        @"```(?:json)?\s*(\{[\s\S]*?\})\s*```",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Fallback: matches a bare JSON object (starting with { and containing "commands")
    /// that is NOT inside a code fence.  Used when the AI omits the ```json``` wrapper.
    /// </summary>
    private static readonly Regex BareJsonBlockRegex = new(
        @"(?<!`)\{[^{}]*""commands""[^{}]*(?:\{[^{}]*\}[^{}]*)*\}(?!`)",
        RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>
    /// Detects an incomplete fenced JSON block that is still being streamed
    /// (opening ``` found but no closing ``` yet).  Everything from the
    /// opening fence onwards should be hidden from the user during streaming.
    /// </summary>
    private static readonly Regex StreamingJsonStartRegex = new(
        @"```(?:json)?\s*\{[\s\S]*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public AITerminalService(
        IAIService        ai,
        IKnowledgeService knowledge,
        AICommandExecutor executor)
    {
        _ai        = ai;
        _knowledge = knowledge;
        _executor  = executor;
    }

    public bool IsConfigured => _ai.IsConfigured;

    public async Task<AITerminalResponse> ProcessQueryAsync(
        string userMessage,
        IReadOnlyList<ChatMessage> conversationHistory,
        CancellationToken cancellationToken = default)
    {
        var systemPrompt = await _knowledge.BuildSystemPromptAsync();

        var messages = new List<ChatMessage>
        {
            new("system", systemPrompt)
        };

        messages.AddRange(conversationHistory);
        messages.Add(new ChatMessage("user", userMessage));

        var raw = await _ai.CompleteAsync(messages, cancellationToken);

        return ParseResponse(raw);
    }

    public async IAsyncEnumerable<string> StreamQueryAsync(
        string userMessage,
        IReadOnlyList<ChatMessage> conversationHistory,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        var systemPrompt = await _knowledge.BuildSystemPromptAsync();

        var messages = new List<ChatMessage>
        {
            new("system", systemPrompt)
        };

        messages.AddRange(conversationHistory);
        messages.Add(new ChatMessage("user", userMessage));

        await foreach (var chunk in _ai.StreamAsync(messages, cancellationToken))
            yield return chunk;
    }

    public Task<AICommandBatchResult> ExecuteAsync(AICommandBatch batch)
        => _executor.ExecuteAsync(batch);

    /// <summary>Parses an embedded JSON action plan from an AI response string.</summary>
    public static AICommandBatch? ParseActionPlan(string text)
    {
        // Try fenced code block first
        var match = JsonBlockRegex.Match(text);
        if (match.Success)
        {
            try
            {
                return JsonSerializer.Deserialize<AICommandBatch>(match.Groups[1].Value, JsonOpts);
            }
            catch { /* fall through to bare JSON */ }
        }

        // Fallback: try bare JSON (no code fences)
        var bareMatch = BareJsonBlockRegex.Match(text);
        if (bareMatch.Success)
        {
            try
            {
                return JsonSerializer.Deserialize<AICommandBatch>(bareMatch.Value, JsonOpts);
            }
            catch { return null; }
        }

        return null;
    }

    /// <summary>Returns the text of an AI response with the embedded JSON block stripped out.</summary>
    public static string ExtractTextWithoutJson(string text)
    {
        // Try fenced block first
        var cleaned = JsonBlockRegex.Replace(text, string.Empty).Trim();
        if (cleaned != text.Trim()) return cleaned;

        // Fallback: bare JSON block
        cleaned = BareJsonBlockRegex.Replace(text, string.Empty).Trim();
        return cleaned;
    }

    /// <summary>
    /// Strips incomplete JSON blocks from a streaming response so the user
    /// doesn't see raw JSON while the AI is still generating it.
    /// If an opening ```json is found without a closing ```, everything
    /// from that point onwards is removed.
    /// </summary>
    public static string StripStreamingJson(string text)
    {
        var match = StreamingJsonStartRegex.Match(text);
        if (match.Success)
            return text[..match.Index].TrimEnd();
        return text;
    }

    private static AITerminalResponse ParseResponse(string raw)
    {
        var response = new AITerminalResponse();

        // Try fenced code block first, then bare JSON
        var match = JsonBlockRegex.Match(raw);
        string? jsonText = null;
        if (match.Success)
        {
            jsonText = match.Groups[1].Value;
        }
        else
        {
            var bareMatch = BareJsonBlockRegex.Match(raw);
            if (bareMatch.Success)
            {
                jsonText = bareMatch.Value;
                match    = bareMatch;
            }
        }

        if (jsonText != null && match.Success)
        {
            var textBefore  = raw[..match.Index].Trim();
            var textAfter   = raw[(match.Index + match.Length)..].Trim();
            var cleanText   = string.Join("\n\n", new[] { textBefore, textAfter }
                .Where(t => !string.IsNullOrWhiteSpace(t)));

            response.TextResponse = string.IsNullOrWhiteSpace(cleanText) ? raw : cleanText;

            try
            {
                response.ActionPlan = JsonSerializer.Deserialize<AICommandBatch>(jsonText, JsonOpts);
            }
            catch
            {
                response.TextResponse = raw;
            }
        }
        else
        {
            response.TextResponse = raw;
        }

        return response;
    }

    /// <summary>
    /// Reload any configuration the terminal depends on (no-op for now).
    /// </summary>
    public Task ReloadConfigurationAsync()
    {
        // Future: refresh runtime model list or re-read settings if needed.
        return Task.CompletedTask;
    }
}
