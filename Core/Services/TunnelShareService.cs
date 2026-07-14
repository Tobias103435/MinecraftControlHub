using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MinecraftControlHub.Core.Models;

namespace MinecraftControlHub.Core.Services;

/// <summary>
/// Handles tunnel sharing and notification retrieval via the Nexora API.
/// All endpoints live under /api/launcher/ alongside the existing friend endpoints.
/// </summary>
public interface ITunnelShareService
{
    /// <summary>
    /// Shares a tunnel with one or more friends by their Nexora usernames.
    /// </summary>
    Task<ApiResponse<object>> ShareTunnelAsync(
        string token,
        IEnumerable<string> recipientUsernames,
        string ip,
        int port,
        string? serverName,
        string senderUsername);

    /// <summary>Returns all tunnel share notifications for the current user (newest first).</summary>
    Task<ApiResponse<List<TunnelNotification>>> GetNotificationsAsync(string token);

    /// <summary>Marks a notification as read.</summary>
    Task<ApiResponse<object>> MarkReadAsync(string token, int notificationId);

    /// <summary>Marks all notifications as read.</summary>
    Task<ApiResponse<object>> MarkAllReadAsync(string token);
}

public class TunnelShareService : ITunnelShareService
{
    private readonly HttpClient _http;

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive    = true,
        PropertyNamingPolicy           = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition         = JsonIgnoreCondition.WhenWritingNull,
    };

    private const string BaseUrl = "https://nexoragames.nl/api/launcher/";

    public TunnelShareService(HttpClient http)
    {
        _http = http;
        _http.BaseAddress = new Uri(BaseUrl);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.Add("X-Launcher-Secret", "mch-launcher-2026");
        _http.DefaultRequestHeaders.Add("User-Agent", "MinecraftControlHub/1.0");
    }

    // ── Share ─────────────────────────────────────────────────────────────────

    public async Task<ApiResponse<object>> ShareTunnelAsync(
        string token,
        IEnumerable<string> recipientUsernames,
        string ip,
        int port,
        string? serverName,
        string senderUsername)
    {
        var payload = new
        {
            token,
            recipients = recipientUsernames.ToArray(),
            ip,
            port,
            server_name   = serverName,
            sender_username = senderUsername,
        };
        return await PostFlatAsync<object>("share-tunnel.php", payload);
    }

    // ── Notifications ─────────────────────────────────────────────────────────

    public async Task<ApiResponse<List<TunnelNotification>>> GetNotificationsAsync(string token)
    {
        var resp = await GetFlatAsync<TunnelNotificationsWrapper>(
            $"tunnel-notifications.php?token={Uri.EscapeDataString(token)}");
        if (resp.Success && resp.Data != null)
            return ApiResponse<List<TunnelNotification>>.Ok(resp.Data.Notifications ?? new());
        return ApiResponse<List<TunnelNotification>>.Fail(resp.Error);
    }

    public async Task<ApiResponse<object>> MarkReadAsync(string token, int notificationId)
        => await PostFlatAsync<object>("tunnel-notification-read.php",
               new { token, notification_id = notificationId });

    public async Task<ApiResponse<object>> MarkAllReadAsync(string token)
        => await PostFlatAsync<object>("tunnel-notification-read-all.php", new { token });

    // ── HTTP helpers ──────────────────────────────────────────────────────────

    private async Task<ApiResponse<T>> PostFlatAsync<T>(string endpoint, object payload)
    {
        try
        {
            var json    = JsonSerializer.Serialize(payload, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _http.PostAsync(endpoint, content);
            var body    = await response.Content.ReadAsStringAsync();
            return ParseFlatResponse<T>(body);
        }
        catch (Exception ex)
        {
            return ApiResponse<T>.Fail(ex.Message);
        }
    }

    private async Task<ApiResponse<T>> GetFlatAsync<T>(string endpoint)
    {
        try
        {
            var response = await _http.GetAsync(endpoint);
            var body     = await response.Content.ReadAsStringAsync();
            return ParseFlatResponse<T>(body);
        }
        catch (Exception ex)
        {
            return ApiResponse<T>.Fail(ex.Message);
        }
    }

    private ApiResponse<T> ParseFlatResponse<T>(string body)
    {
        try
        {
            using var doc  = JsonDocument.Parse(body);
            var root       = doc.RootElement;
            var success    = root.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.True;
            if (!success)
            {
                var err = root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String
                    ? e.GetString() ?? "Unknown error"
                    : "Request failed";
                return ApiResponse<T>.Fail(err);
            }
            var data = JsonSerializer.Deserialize<T>(body, _jsonOptions);
            return ApiResponse<T>.Ok(data!);
        }
        catch (Exception ex)
        {
            return ApiResponse<T>.Fail($"Parse error: {ex.Message}");
        }
    }

    // ── Inner wrapper ─────────────────────────────────────────────────────────

    private class TunnelNotificationsWrapper
    {
        [JsonPropertyName("notifications")]
        public List<TunnelNotification>? Notifications { get; set; }
    }
}
