namespace MinecraftControlHub.AI.Services;

public record ChatMessage(string Role, string Content);

public interface IAIService
{
    bool IsConfigured { get; }

    Task<string> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the list of text-generation Gemini model IDs available for the
    /// given API key, fetched live from the Gemini models endpoint.
    /// </summary>
    Task<List<string>> GetGeminiModelsAsync(
        string apiKey,
        CancellationToken cancellationToken = default);
}
