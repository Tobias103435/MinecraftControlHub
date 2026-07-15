using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MinecraftControlHub.Core.Models;

namespace MinecraftControlHub.Core.Services;

/// <summary>
/// Service for communicating with the Nexora Launcher API
/// </summary>
public interface INexoraApiService
{
    Task<ApiResponse<LoginResponse>> LoginAsync(string emailOrUsername, string password);
    Task<ApiResponse<LoginResponse>> Verify2FAAsync(string challenge, string code);
    Task<ApiResponse<NexoraAccount>> ValidateTokenAsync(string token);
    Task<ApiResponse<object>> LogoutAsync(string token);
    Task<ApiResponse<object>> LinkMinecraftAccountAsync(string token, string uuid, string username);
    Task<ApiResponse<object>> UnlinkMinecraftAccountAsync(string token);
    Task<ApiResponse<MeResponse>> GetMeAsync(string token);
    Task<ApiResponse<object>> SendFriendRequestAsync(string token, string username);
    Task<ApiResponse<List<FriendRequest>>> GetFriendRequestsAsync(string token);
    Task<ApiResponse<object>> AcceptFriendRequestAsync(string token, int requestId);
    Task<ApiResponse<object>> DeclineFriendRequestAsync(string token, int requestId);
    Task<ApiResponse<List<Friend>>> GetFriendsAsync(string token);
}

public class NexoraApiService : INexoraApiService
{
    private readonly HttpClient _http;

    // snake_case ↔ PascalCase + ignore unknown fields
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private const string BaseUrl = "https://nexoragames.nl/api/launcher/";

    public NexoraApiService(HttpClient http)
    {
        _http = http;
        _http.BaseAddress = new Uri(BaseUrl);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.Add("X-Launcher-Secret", "mch-launcher-2026");
        _http.DefaultRequestHeaders.Add("User-Agent", "MinecraftControlHub/1.0");
    }

    // ── Auth ──────────────────────────────────────────────────────────────────

    public async Task<ApiResponse<LoginResponse>> LoginAsync(string emailOrUsername, string password)
    {
        object payload = emailOrUsername.Contains('@')
            ? new { email = emailOrUsername, password }
            : new { username = emailOrUsername, password };

        return await PostFlatAsync<LoginResponse>("login.php", payload);
    }

    public async Task<ApiResponse<LoginResponse>> Verify2FAAsync(string challenge, string code)
        => await PostFlatAsync<LoginResponse>("verify-2fa.php", new { challenge, code });

    public async Task<ApiResponse<NexoraAccount>> ValidateTokenAsync(string token)
    {
        var resp = await PostFlatAsync<NexoraAccount>("validate.php", new { token });
        if (resp.Success && resp.Data != null)
            resp.Data.Token = token;
        return resp;
    }

    public async Task<ApiResponse<object>> LogoutAsync(string token)
        => await PostFlatAsync<object>("logout.php", new { token });

    // ── Profile & Minecraft link ──────────────────────────────────────────────

    /// <summary>
    /// GET me.php — server returns flat JSON:
    ///   { "success": true, "website_username": "...", "minecraft_username": "...", "minecraft_uuid": "..." }
    /// </summary>
    public async Task<ApiResponse<MeResponse>> GetMeAsync(string token)
        => await GetFlatAsync<MeResponse>($"me.php?token={Uri.EscapeDataString(token)}");

    public async Task<ApiResponse<object>> LinkMinecraftAccountAsync(string token, string uuid, string username)
        => await PostFlatAsync<object>("link.php", new { token, uuid, username });

    public async Task<ApiResponse<object>> UnlinkMinecraftAccountAsync(string token)
        => await PostFlatAsync<object>("unlink.php", new { token });

    // ── Friends ───────────────────────────────────────────────────────────────

    /// <summary>
    /// POST friend-request.php — server returns { "success": true }
    /// </summary>
    public async Task<ApiResponse<object>> SendFriendRequestAsync(string token, string username)
        => await PostFlatAsync<object>("friend-request.php", new { token, username });

    /// <summary>
    /// GET friend-requests.php — server returns { "success": true, "requests": [...] }
    /// </summary>
    public async Task<ApiResponse<List<FriendRequest>>> GetFriendRequestsAsync(string token)
    {
        var resp = await GetFlatAsync<FriendRequestsWrapper>($"friend-requests.php?token={Uri.EscapeDataString(token)}");
        if (resp.Success && resp.Data != null)
            return ApiResponse<List<FriendRequest>>.Ok(resp.Data.Requests ?? new List<FriendRequest>());
        return ApiResponse<List<FriendRequest>>.Fail(resp.Error);
    }

    /// <summary>
    /// GET friends.php — server returns { "success": true, "friends": [...] }
    /// </summary>
    public async Task<ApiResponse<List<Friend>>> GetFriendsAsync(string token)
    {
        var resp = await GetFlatAsync<FriendsWrapper>($"friends.php?token={Uri.EscapeDataString(token)}");
        if (resp.Success && resp.Data != null)
            return ApiResponse<List<Friend>>.Ok(resp.Data.Friends ?? new List<Friend>());
        return ApiResponse<List<Friend>>.Fail(resp.Error);
    }

    /// <summary>POST accept-friend.php — body: { token, requestId }</summary>
    public async Task<ApiResponse<object>> AcceptFriendRequestAsync(string token, int requestId)
        => await PostFlatAsync<object>("accept-friend.php", new FriendActionPayload(token, requestId));

    /// <summary>POST decline-friend.php — body: { token, requestId }</summary>
    public async Task<ApiResponse<object>> DeclineFriendRequestAsync(string token, int requestId)
        => await PostFlatAsync<object>("decline-friend.php", new FriendActionPayload(token, requestId));

    // Private DTO with explicit JSON property names to bypass the global snake_case naming policy
    private sealed record FriendActionPayload(
        [property: JsonPropertyName("token")]     string Token,
        [property: JsonPropertyName("requestId")] int    RequestId);

    // ── HTTP helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// POST and deserialize the ENTIRE response body onto T (the API returns
    /// all fields flat at the root level, not nested under "data").
    /// </summary>
    private async Task<ApiResponse<T>> PostFlatAsync<T>(string endpoint, object payload)
    {
        try
        {
            var json    = JsonSerializer.Serialize(payload, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _http.PostAsync(endpoint, content);
            var body    = await response.Content.ReadAsStringAsync();
            return ParseFlatResponse<T>(body, (int)response.StatusCode);
        }
        catch (Exception ex)
        {
            return ApiResponse<T>.Fail(ex.Message);
        }
    }

    /// <summary>
    /// GET and deserialize the ENTIRE response body onto T.
    /// </summary>
    private async Task<ApiResponse<T>> GetFlatAsync<T>(string endpoint)
    {
        try
        {
            var response = await _http.GetAsync(endpoint);
            var body     = await response.Content.ReadAsStringAsync();
            return ParseFlatResponse<T>(body, (int)response.StatusCode);
        }
        catch (Exception ex)
        {
            return ApiResponse<T>.Fail(ex.Message);
        }
    }

    /// <summary>
    /// Parses a flat API response: checks "success", reads "error" on failure,
    /// or deserializes the whole body onto T on success.
    /// Guards against non-JSON responses (e.g. HTML 404 pages) and returns a
    /// human-readable error instead of throwing a JsonException.
    /// </summary>
    private ApiResponse<T> ParseFlatResponse<T>(string body, int statusCode = 0)
    {
        // Guard: empty body
        if (string.IsNullOrWhiteSpace(body))
            return ApiResponse<T>.Fail($"Server returned an empty response (HTTP {statusCode}).");

        // Guard: non-JSON response (e.g. HTML error page from the web server)
        var trimmed = body.TrimStart();
        if (!trimmed.StartsWith("{") && !trimmed.StartsWith("["))
        {
            var preview = trimmed.Length > 120 ? trimmed[..120] + "…" : trimmed;
            return ApiResponse<T>.Fail($"Unexpected server response (HTTP {statusCode}): {preview}");
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root      = doc.RootElement;

            var success = root.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.True;

            if (!success)
            {
                var err = root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String
                    ? e.GetString() ?? "Unknown error"
                    : "Request failed";
                return ApiResponse<T>.Fail(err);
            }

            // Deserialize the full body (snake_case → PascalCase handled by _jsonOptions)
            var data = JsonSerializer.Deserialize<T>(body, _jsonOptions);
            return ApiResponse<T>.Ok(data!);
        }
        catch (JsonException ex)
        {
            return ApiResponse<T>.Fail($"Failed to parse server response: {ex.Message}");
        }
    }

    // ── Inner wrapper types for array responses ───────────────────────────────

    private class FriendsWrapper
    {
        [JsonPropertyName("friends")]
        public List<Friend>? Friends { get; set; }
    }

    private class FriendRequestsWrapper
    {
        [JsonPropertyName("requests")]
        public List<FriendRequest>? Requests { get; set; }
    }
}
