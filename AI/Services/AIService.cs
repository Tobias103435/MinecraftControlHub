using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MinecraftControlHub.Core.Services;

namespace MinecraftControlHub.AI.Services;

public class AIService : IAIService
{
    private readonly HttpClient _http;
    private readonly ISettingsService _settings;

    private const string GeminiBaseUrl =
        "https://generativelanguage.googleapis.com/v1beta/models";

    public AIService(HttpClient http, ISettingsService settings)
    {
        _http     = http;
        _settings = settings;
    }

    /// <summary>
    /// Throws an <see cref="HttpRequestException"/> with the actual API error body
    /// included in the message, instead of swallowing it via EnsureSuccessStatusCode.
    /// </summary>
    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken = default)
    {
        if (response.IsSuccessStatusCode) return;

        string body;
        try { body = await response.Content.ReadAsStringAsync(cancellationToken); }
        catch { body = "(could not read response body)"; }

        throw new HttpRequestException(
            $"API request failed ({(int)response.StatusCode} {response.ReasonPhrase}): {body}",
            inner: null,
            statusCode: response.StatusCode);
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_settings.Settings.AiApiKey);

    private bool IsGemini =>
        string.Equals(_settings.Settings.AiProvider, "Gemini",
            StringComparison.OrdinalIgnoreCase);

    // ── Non-streaming complete ───────────────────────────────────────────────

    public Task<string> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default)
        => IsGemini
            ? GeminiCompleteAsync(messages, cancellationToken)
            : OpenAICompleteAsync(messages, cancellationToken);

    // ── Streaming ────────────────────────────────────────────────────────────

    public IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default)
        => IsGemini
            ? GeminiStreamAsync(messages, cancellationToken)
            : OpenAIStreamAsync(messages, cancellationToken);

    // =========================================================================
    // OpenAI-compatible
    // =========================================================================

    private async Task<string> OpenAICompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        var request  = BuildOpenAIRequest(messages, stream: false);
        var response = await _http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var doc  = JsonNode.Parse(json);

        return doc?["choices"]?[0]?["message"]?["content"]?.GetValue<string>()
               ?? string.Empty;
    }

    private async IAsyncEnumerable<string> OpenAIStreamAsync(
        IReadOnlyList<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var request = BuildOpenAIRequest(messages, stream: true);

        using var response = await _http.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new System.IO.StreamReader(stream);

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrEmpty(line)) continue;
            if (!line.StartsWith("data: ")) continue;

            var data = line["data: ".Length..];
            if (data == "[DONE]") yield break;

            JsonNode? node;
            try { node = JsonNode.Parse(data); }
            catch { continue; }

            var delta = node?["choices"]?[0]?["delta"]?["content"]?.GetValue<string>();
            if (delta != null)
                yield return delta;
        }
    }

    private HttpRequestMessage BuildOpenAIRequest(
        IReadOnlyList<ChatMessage> messages, bool stream)
    {
        var settings = _settings.Settings;
        var endpoint = (settings.AiApiEndpoint ?? "https://api.openai.com/v1").TrimEnd('/');

        var body = new
        {
            model    = settings.AiModel ?? "gpt-4o-mini",
            stream,
            messages = messages.Select(m => new { role = m.Role, content = m.Content }).ToArray()
        };

        var request = new HttpRequestMessage(HttpMethod.Post, $"{endpoint}/chat/completions")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json")
        };

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", settings.AiApiKey);

        return request;
    }

    // =========================================================================
    // Google Gemini
    // =========================================================================

    private async Task<string> GeminiCompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        var (url, body) = BuildGeminiRequest(messages, stream: false);

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        var response = await _http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var doc  = JsonNode.Parse(json);

        return doc?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.GetValue<string>()
               ?? string.Empty;
    }

    private async IAsyncEnumerable<string> GeminiStreamAsync(
        IReadOnlyList<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var (url, body) = BuildGeminiRequest(messages, stream: true);

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        using var response = await _http.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new System.IO.StreamReader(stream);

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrEmpty(line)) continue;
            if (!line.StartsWith("data: ")) continue;

            var data = line["data: ".Length..].Trim();
            if (string.IsNullOrWhiteSpace(data)) continue;

            JsonNode? node;
            try { node = JsonNode.Parse(data); }
            catch { continue; }

            var text = node?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.GetValue<string>();
            if (text != null)
                yield return text;
        }
    }

    // =========================================================================
    // Model discovery
    // =========================================================================

    public async Task<List<string>> GetGeminiModelsAsync(
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        var url = $"{GeminiBaseUrl}?key={apiKey}&pageSize=100";
        var response = await _http.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var doc  = JsonNode.Parse(json);

        var models = new List<string>();
        var arr = doc?["models"]?.AsArray();
        if (arr == null) return models;

        foreach (var item in arr)
        {
            // name is e.g. "models/gemini-2.5-flash" — strip the prefix
            var name = item?["name"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(name)) continue;

            var modelId = name.StartsWith("models/") ? name["models/".Length..] : name;

            // Only include text-generation models (exclude embedding, tts, image, video, etc.)
            var supportedMethods = item?["supportedGenerationMethods"]?.AsArray();
            if (supportedMethods == null) continue;
            if (!supportedMethods.Any(m => m?.GetValue<string>() == "generateContent")) continue;

            models.Add(modelId);
        }

        // Stable/short names first, then previews/experimental
        models.Sort((a, b) =>
        {
            bool aPreview = a.Contains("preview") || a.Contains("exp");
            bool bPreview = b.Contains("preview") || b.Contains("exp");
            if (aPreview != bPreview) return aPreview ? 1 : -1;
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        });

        return models;
    }

    private (string url, string body) BuildGeminiRequest(
        IReadOnlyList<ChatMessage> messages, bool stream)
    {
        var settings = _settings.Settings;
        var model    = settings.AiModel ?? "gemini-2.5-flash";
        var apiKey   = settings.AiApiKey;

        var action = stream
            ? $"streamGenerateContent?alt=sse&key={apiKey}"
            : $"generateContent?key={apiKey}";

        var url = $"{GeminiBaseUrl}/{model}:{action}";

        var systemMsg = messages.FirstOrDefault(m => m.Role == "system");
        var chatMsgs  = messages.Where(m => m.Role != "system").ToList();

        var contents = chatMsgs.Select(m => new
        {
            role  = m.Role == "assistant" ? "model" : "user",
            parts = new[] { new { text = m.Content } }
        }).ToArray();

        object bodyObj;
        if (systemMsg != null)
        {
            bodyObj = new
            {
                systemInstruction = new
                {
                    parts = new[] { new { text = systemMsg.Content } }
                },
                contents
            };
        }
        else
        {
            bodyObj = new { contents };
        }

        return (url, JsonSerializer.Serialize(bodyObj));
    }
}
