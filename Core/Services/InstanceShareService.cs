using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MinecraftControlHub.Core.Models;

namespace MinecraftControlHub.Core.Services;

/// <summary>
/// Shares Minecraft installations between Nexora users.
///
/// Two flows:
///   1. Share with friends  — POSTs a share-instance.php payload to the backend.
///      Recipients get a notification they can "Install" or "Decline".
///   2. Share code          — POSTs a create-instance-code.php and gets back a
///      short code the sender can paste anywhere.
///      Anyone with the code can call RedeemCodeAsync to get the mrpack bytes.
/// </summary>
public interface IInstanceShareService
{
    /// <summary>Share an installation with one or more Nexora friends.</summary>
    Task<ApiResponse<object>> ShareWithFriendsAsync(
        string            token,
        string            senderUsername,
        Installation      installation,
        IEnumerable<string> recipientUsernames,
        IProgress<string>? progress = null);

    /// <summary>Generate a short share code for an installation.</summary>
    Task<ApiResponse<string>> GenerateShareCodeAsync(
        string         token,
        Installation   installation,
        IProgress<string>? progress = null);

    /// <summary>Redeem a share code and return the mrpack bytes.</summary>
    Task<ApiResponse<byte[]>> RedeemCodeAsync(string code);

    /// <summary>Returns all incoming instance-share notifications.</summary>
    Task<ApiResponse<List<InstanceShareNotification>>> GetNotificationsAsync(string token);

    /// <summary>Marks a notification as read.</summary>
    Task<ApiResponse<object>> MarkReadAsync(string token, int notificationId);

    /// <summary>Marks all notifications as read.</summary>
    Task<ApiResponse<object>> MarkAllReadAsync(string token);
}

public class InstanceShareService : IInstanceShareService
{
    private readonly HttpClient                  _http;
    private readonly IModpackExportImportService _modpacks;

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy        = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
    };

    private const string BaseUrl = "https://nexoragames.nl/api/launcher/";

    public InstanceShareService(HttpClient http, IModpackExportImportService modpacks)
    {
        _http     = http;
        _modpacks = modpacks;
        _http.BaseAddress = new Uri(BaseUrl);
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.Add("X-Launcher-Secret", "mch-launcher-2026");
        _http.DefaultRequestHeaders.Add("User-Agent", "MinecraftControlHub/1.0");
    }

    // ── Share with friends ────────────────────────────────────────────────────

    public async Task<ApiResponse<object>> ShareWithFriendsAsync(
        string             token,
        string             senderUsername,
        Installation       installation,
        IEnumerable<string> recipientUsernames,
        IProgress<string>? progress = null)
    {
        progress?.Report("Packing modpack for sharing…");
        var mrpackBytes = await ExportToMemoryAsync(installation, progress);

        progress?.Report("Uploading…");
        var payload = new
        {
            token,
            sender_username   = senderUsername,
            recipients        = recipientUsernames.ToArray(),
            instance_name     = installation.Name,
            minecraft_version = installation.MinecraftVersion,
            loader            = installation.Loader.ToString(),
            mrpack_base64     = Convert.ToBase64String(mrpackBytes)
        };

        return await PostFlatAsync<object>("share-instance.php", payload);
    }

    // ── Share code ────────────────────────────────────────────────────────────

    public async Task<ApiResponse<string>> GenerateShareCodeAsync(
        string         token,
        Installation   installation,
        IProgress<string>? progress = null)
    {
        progress?.Report("Packing modpack…");
        var mrpackBytes = await ExportToMemoryAsync(installation, progress);

        progress?.Report("Uploading to Nexora…");
        var payload = new
        {
            token,
            instance_name     = installation.Name,
            minecraft_version = installation.MinecraftVersion,
            loader            = installation.Loader.ToString(),
            mrpack_base64     = Convert.ToBase64String(mrpackBytes)
        };

        var resp = await PostFlatAsync<ShareCodeResponse>("create-instance-code.php", payload);
        if (!resp.Success)
            return ApiResponse<string>.Fail(resp.Error);

        return ApiResponse<string>.Ok(resp.Data?.Code ?? string.Empty);
    }

    // ── Redeem code ───────────────────────────────────────────────────────────

    public async Task<ApiResponse<byte[]>> RedeemCodeAsync(string code)
    {
        try
        {
            var resp = await GetFlatAsync<RedeemCodeResponse>(
                $"redeem-instance-code.php?code={Uri.EscapeDataString(code)}");

            if (!resp.Success || string.IsNullOrWhiteSpace(resp.Data?.MrpackBase64))
                return ApiResponse<byte[]>.Fail(resp.Error ?? "Invalid or expired code.");

            return ApiResponse<byte[]>.Ok(Convert.FromBase64String(resp.Data.MrpackBase64));
        }
        catch (Exception ex)
        {
            return ApiResponse<byte[]>.Fail(ex.Message);
        }
    }

    // ── Notifications ─────────────────────────────────────────────────────────

    public async Task<ApiResponse<List<InstanceShareNotification>>> GetNotificationsAsync(string token)
    {
        var resp = await GetFlatAsync<InstanceNotificationsWrapper>(
            $"instance-notifications.php?token={Uri.EscapeDataString(token)}");

        if (resp.Success && resp.Data != null)
            return ApiResponse<List<InstanceShareNotification>>.Ok(
                resp.Data.Notifications ?? new());

        return ApiResponse<List<InstanceShareNotification>>.Fail(resp.Error);
    }

    public Task<ApiResponse<object>> MarkReadAsync(string token, int notificationId)
        => PostFlatAsync<object>("instance-notification-read.php",
               new { token, notification_id = notificationId });

    public Task<ApiResponse<object>> MarkAllReadAsync(string token)
        => PostFlatAsync<object>("instance-notification-read-all.php", new { token });

    // ── Internal helpers ──────────────────────────────────────────────────────

    /// <summary>Exports an installation to a .mrpack in memory (temp file, then read bytes).</summary>
    private async Task<byte[]> ExportToMemoryAsync(
        Installation installation, IProgress<string>? progress)
    {
        var tempPath = Path.Combine(Path.GetTempPath(),
            $"mch-share-{installation.Id:N}.mrpack");
        try
        {
            var result = await _modpacks.ExportAsync(installation, tempPath, progress);
            if (!result.Success)
                throw new Exception(result.Error ?? "Modpack export failed.");
            
            if (!File.Exists(tempPath))
                throw new Exception("Exported modpack file not found.");
                
            return await File.ReadAllBytesAsync(tempPath);
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
        }
    }

    private async Task<ApiResponse<T>> PostFlatAsync<T>(string endpoint, object payload)
    {
        try
        {
            var json     = JsonSerializer.Serialize(payload, _jsonOptions);
            var content  = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _http.PostAsync(endpoint, content);
            var body     = await response.Content.ReadAsStringAsync();
            return ParseFlatResponse<T>(body);
        }
        catch (Exception ex) { return ApiResponse<T>.Fail(ex.Message); }
    }

    private async Task<ApiResponse<T>> GetFlatAsync<T>(string endpoint)
    {
        try
        {
            var response = await _http.GetAsync(endpoint);
            var body     = await response.Content.ReadAsStringAsync();
            return ParseFlatResponse<T>(body);
        }
        catch (Exception ex) { return ApiResponse<T>.Fail(ex.Message); }
    }

    private ApiResponse<T> ParseFlatResponse<T>(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root      = doc.RootElement;
            var success   = root.TryGetProperty("success", out var s)
                         && s.ValueKind == JsonValueKind.True;

            if (!success)
            {
                var err = root.TryGetProperty("error", out var e)
                       && e.ValueKind == JsonValueKind.String
                    ? e.GetString() ?? "Unknown error"
                    : "Request failed";
                return ApiResponse<T>.Fail(err);
            }

            var data = JsonSerializer.Deserialize<T>(body, _jsonOptions);
            return ApiResponse<T>.Ok(data!);
        }
        catch (Exception ex) { return ApiResponse<T>.Fail($"Parse error: {ex.Message}"); }
    }

    // ── DTO inner types ───────────────────────────────────────────────────────

    private class ShareCodeResponse
    {
        [JsonPropertyName("code")]
        public string? Code { get; set; }
    }

    private class RedeemCodeResponse
    {
        [JsonPropertyName("mrpack_base64")]
        public string? MrpackBase64 { get; set; }
    }

    private class InstanceNotificationsWrapper
    {
        [JsonPropertyName("notifications")]
        public List<InstanceShareNotification>? Notifications { get; set; }
    }
}
