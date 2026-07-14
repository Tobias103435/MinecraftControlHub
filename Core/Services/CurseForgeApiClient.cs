using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using MinecraftControlHub.Core.Models;

namespace MinecraftControlHub.Core.Services;

public interface ICurseForgeApiClient
{
    Task<ModSearchPage> SearchModsAsync(string? query, string? minecraftVersion = null, LoaderType? loader = null, int offset = 0, int limit = 20);
    Task<ModDetail?> GetModDetailAsync(int curseForgeId);
    Task<List<ModVersion>> GetModFilesAsync(int curseForgeId, string? minecraftVersion = null, LoaderType? loader = null);
    Task<byte[]> DownloadModAsync(string downloadUrl);

    /// <summary>
    /// Looks up CurseForge file metadata by a file fingerprint (MurmurHash2).
    /// Used for update checks on locally-installed CurseForge mods.
    /// </summary>
    Task<ModVersion?> GetFileByFingerprintAsync(uint fingerprint);
}

public class CurseForgeApiClient : ICurseForgeApiClient
{
    private readonly HttpClient _httpClient;
    private readonly IAppLogService _log;
    private readonly INexoraAccountService _nexoraAccount;

    /// <summary>
    /// All CurseForge calls go through the Nexora backend proxy so the API key
    /// stays server-side only (CurseForge ToS: key is non-transferable).
    /// </summary>
    private const string ProxyBaseUrl = "https://nexoragames.nl/api/launcher/curseforge-proxy.php";
    private const int MinecraftGameId = 432;
    private const int ModClassId = 6;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public CurseForgeApiClient(HttpClient httpClient, IAppLogService log, INexoraAccountService nexoraAccount)
    {
        _httpClient = httpClient;
        _log = log;
        _nexoraAccount = nexoraAccount;
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "MinecraftControlHub/1.0");
    }

    // ── Proxy helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a GET URL for the Nexora CurseForge proxy. The CurseForge path
    /// (e.g. "mods/search?gameId=432&amp;...") is passed as the <c>cf_path</c>
    /// query parameter, along with the user's auth token.
    /// </summary>
    private string BuildProxyGetUrl(string cfPath)
    {
        var token = _nexoraAccount.Current?.Token ?? string.Empty;
        return $"{ProxyBaseUrl}?cf_path={Uri.EscapeDataString(cfPath)}&token={Uri.EscapeDataString(token)}";
    }

    /// <summary>
    /// Sends a POST through the proxy — the CurseForge path and JSON body are
    /// wrapped together with the auth token.
    /// </summary>
    private async Task<HttpResponseMessage> ProxyPostAsync(string cfPath, string jsonBody)
    {
        var token = _nexoraAccount.Current?.Token ?? string.Empty;
        var wrapper = JsonSerializer.Serialize(new
        {
            token,
            cf_path = cfPath,
            cf_body = jsonBody
        });
        var content = new StringContent(wrapper, System.Text.Encoding.UTF8, "application/json");
        return await _httpClient.PostAsync(ProxyBaseUrl, content);
    }

    // ── Search ──────────────────────────────────────────────────────────────────

    public async Task<ModSearchPage> SearchModsAsync(
        string? query, string? minecraftVersion = null, LoaderType? loader = null,
        int offset = 0, int limit = 20)
    {
        try
        {
            var cfPath = $"mods/search?gameId={MinecraftGameId}&classId={ModClassId}" +
                         $"&searchFilter={Uri.EscapeDataString(query ?? string.Empty)}" +
                         $"&index={offset}&pageSize={limit}&sortField=2&sortOrder=desc";

            if (!string.IsNullOrWhiteSpace(minecraftVersion))
                cfPath += $"&gameVersion={Uri.EscapeDataString(minecraftVersion)}";

            var loaderType = MapLoaderType(loader);
            if (loaderType > 0)
                cfPath += $"&modLoaderType={loaderType}";

            var url = BuildProxyGetUrl(cfPath);
            _log.Log("CurseForge.Search", $"GET {cfPath}");
            var response = await _httpClient.GetAsync(url);
            _log.Log("CurseForge.Search", $"Response: {(int)response.StatusCode} {response.StatusCode}");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var cfResponse = JsonSerializer.Deserialize<CfSearchResponse>(json, JsonOptions);

            if (cfResponse?.Data == null)
            {
                _log.Log("CurseForge.Search", "Deserialization returned null");
                return new ModSearchPage { Offset = offset, Limit = limit };
            }

            var hits = cfResponse.Data.Select(MapToSearchResult).ToList();

            _log.Log("CurseForge.Search", $"Fetched {hits.Count} results (total={cfResponse.Pagination.TotalCount}, offset={offset})");
            return new ModSearchPage
            {
                Hits = hits,
                Offset = offset,
                Limit = limit,
                TotalHits = cfResponse.Pagination.TotalCount
            };
        }
        catch (Exception ex)
        {
            _log.LogError("CurseForge.Search", $"Search failed for query='{query}'", ex);
            return new ModSearchPage { Offset = offset, Limit = limit };
        }
    }

    // ── Detail ──────────────────────────────────────────────────────────────────

    public async Task<ModDetail?> GetModDetailAsync(int curseForgeId)
    {
        try
        {
            var url = BuildProxyGetUrl($"mods/{curseForgeId}");
            _log.Log("CurseForge.Detail", $"GET mods/{curseForgeId}");
            var response = await _httpClient.GetAsync(url);
            _log.Log("CurseForge.Detail", $"Response: {(int)response.StatusCode} {response.StatusCode}");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var wrapper = JsonSerializer.Deserialize<CfModWrapper>(json, JsonOptions);
            var mod = wrapper?.Data;

            if (mod == null)
            {
                _log.Log("CurseForge.Detail", $"Deserialization returned null for mod={curseForgeId}");
                return null;
            }

            // Fetch description separately (it's a different endpoint)
            string? body = null;
            try
            {
                var descUrl = BuildProxyGetUrl($"mods/{curseForgeId}/description");
                var descResp = await _httpClient.GetAsync(descUrl);
                if (descResp.IsSuccessStatusCode)
                {
                    var descJson = await descResp.Content.ReadAsStringAsync();
                    var descWrapper = JsonSerializer.Deserialize<CfDescriptionWrapper>(descJson, JsonOptions);
                    body = descWrapper?.Data;
                }
            }
            catch { /* description is optional */ }

            return new ModDetail
            {
                ModrinthId = string.Empty,
                CurseForgeId = mod.Id,
                Source = ModSource.CurseForge,
                Name = mod.Name,
                Description = mod.Summary,
                Body = body,
                Author = mod.Authors?.FirstOrDefault()?.Name,
                IconUrl = mod.Logo?.Url,
                Downloads = mod.DownloadCount,
                Followers = mod.ThumbsUp,
                Categories = mod.Categories?.Select(c => c.Name).Where(n => n != null).Cast<string>().ToList() ?? new List<string>(),
                Gallery = mod.Screenshots?
                    .Select(s => new ModGalleryImage { Url = s.Url ?? string.Empty, Title = s.Title })
                    .Where(g => !string.IsNullOrEmpty(g.Url))
                    .ToList() ?? new List<ModGalleryImage>()
            };
        }
        catch (Exception ex)
        {
            _log.LogError("CurseForge.Detail", $"GetModDetailAsync failed for mod={curseForgeId}", ex);
            return null;
        }
    }

    // ── Files / Versions ─────────────────────────────────────────────────────────

    public async Task<List<ModVersion>> GetModFilesAsync(
        int curseForgeId, string? minecraftVersion = null, LoaderType? loader = null)
    {
        try
        {
            var cfPath = $"mods/{curseForgeId}/files?pageSize=100";

            if (!string.IsNullOrWhiteSpace(minecraftVersion))
                cfPath += $"&gameVersion={Uri.EscapeDataString(minecraftVersion)}";

            var loaderType = MapLoaderType(loader);
            if (loaderType > 0)
                cfPath += $"&modLoaderType={loaderType}";

            var url = BuildProxyGetUrl(cfPath);
            _log.Log("CurseForge.Files", $"GET {cfPath}");
            var response = await _httpClient.GetAsync(url);
            _log.Log("CurseForge.Files", $"Response: {(int)response.StatusCode} {response.StatusCode}");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var wrapper = JsonSerializer.Deserialize<CfFilesWrapper>(json, JsonOptions);

            if (wrapper?.Data == null)
            {
                _log.Log("CurseForge.Files", $"Deserialization returned null for mod={curseForgeId}");
                return new List<ModVersion>();
            }

            var versions = wrapper.Data
                .Where(f => f.IsAvailable)
                .Select(f => MapToFile(f, curseForgeId))
                .ToList();

            // Client-side filter for exact Minecraft version
            if (!string.IsNullOrWhiteSpace(minecraftVersion))
            {
                versions = versions
                    .Where(v => v.GameVersions.Contains(minecraftVersion, StringComparer.OrdinalIgnoreCase))
                    .ToList();
            }

            return versions;
        }
        catch (Exception)
        {
            return new List<ModVersion>();
        }
    }

    // ── Download ─────────────────────────────────────────────────────────────
    // Downloads go directly to the CurseForge CDN (no API key needed for CDN URLs),
    // bypassing the proxy. This keeps large file transfers efficient.

    public async Task<byte[]> DownloadModAsync(string downloadUrl)
    {
        // CurseForge sometimes returns a redirect URL for downloads.
        // HttpClient follows redirects by default, so this just works.
        var response = await _httpClient.GetAsync(downloadUrl);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync();
    }

    // ── Fingerprint lookup ──────────────────────────────────────────────────────

    public async Task<ModVersion?> GetFileByFingerprintAsync(uint fingerprint)
    {
        try
        {
            var body = JsonSerializer.Serialize(new { fingerprints = new[] { fingerprint } });
            var response = await ProxyPostAsync("fingerprints", body);
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync();
            var wrapper = JsonSerializer.Deserialize<CfFingerprintResponse>(json, JsonOptions);

            var match = wrapper?.Data?.ExactMatches?.FirstOrDefault();
            if (match?.File == null)
                return null;

            return MapToFile(match.File, match.Id);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"CurseForge fingerprint error: {ex.Message}");
            return null;
        }
    }

    // ── Mapping helpers ──────────────────────────────────────────────────────────

    private static ModSearchResult MapToSearchResult(CfMod mod) => new()
    {
        ModrinthId = string.Empty,
        CurseForgeId = mod.Id,
        Source = ModSource.CurseForge,
        Name = mod.Name,
        Description = mod.Summary,
        Author = mod.Authors?.FirstOrDefault()?.Name,
        IconUrl = mod.Logo?.Url,
        Downloads = mod.DownloadCount,
        DateModified = mod.DateModified,
        Categories = mod.Categories?.Select(c => c.Name).Where(n => n != null).Cast<string>().ToList() ?? new List<string>()
    };

    private static ModVersion MapToFile(CfFile file, int modId)
    {
        var gameVersions = file.GameVersions ?? new List<string>();
        var loader = ParseLoaderFromGameVersions(gameVersions);

        return new ModVersion
        {
            Id = file.Id.ToString(),
            ModrinthId = string.Empty,
            CurseForgeFileId = file.Id,
            Source = ModSource.CurseForge,
            VersionNumber = file.FileName ?? file.Id.ToString(),
            Name = file.DisplayName ?? file.FileName ?? string.Empty,
            MinecraftVersion = gameVersions.LastOrDefault(v => LooksLikeMcVersion(v)),
            GameVersions = gameVersions.Where(v => LooksLikeMcVersion(v)).ToList(),
            Loader = loader,
            DownloadUrl = file.DownloadUrl,
            FileName = file.FileName,
            FileSize = file.FileLength,
            DatePublished = file.FileDate,
            Sha1Hash = file.Hashes?.FirstOrDefault(h => h.Algo == 1)?.Value,
            Sha512Hash = file.Hashes?.FirstOrDefault(h => h.Algo == 2)?.Value,
            VersionType = file.ReleaseType switch
            {
                1 => "release",
                2 => "beta",
                3 => "alpha",
                _ => "release"
            },
            Dependencies = file.Dependencies?
                .Select(d => new ModDependency
                {
                    ProjectId = d.ModId.ToString(),
                    Type = d.RelationType switch
                    {
                        2 => DependencyType.Optional,
                        3 => DependencyType.Required,
                        4 => DependencyType.Incompatible,
                        _ => DependencyType.Optional
                    }
                })
                .ToList() ?? new List<ModDependency>()
        };
    }

    private static LoaderType ParseLoaderFromGameVersions(List<string> gameVersions)
    {
        // CurseForge includes loader names in gameVersions alongside MC versions
        if (gameVersions.Any(v => v.Equals("NeoForge", StringComparison.OrdinalIgnoreCase)))
            return LoaderType.NeoForge;
        if (gameVersions.Any(v => v.Equals("Quilt", StringComparison.OrdinalIgnoreCase)))
            return LoaderType.Quilt;
        if (gameVersions.Any(v => v.Equals("Forge", StringComparison.OrdinalIgnoreCase)))
            return LoaderType.Forge;
        if (gameVersions.Any(v => v.Equals("Fabric", StringComparison.OrdinalIgnoreCase)))
            return LoaderType.Fabric;
        return LoaderType.Vanilla;
    }

    private static bool LooksLikeMcVersion(string v)
        => System.Text.RegularExpressions.Regex.IsMatch(v, @"^\d+\.\d+(?:\.\d+)?$");

    private static int MapLoaderType(LoaderType? loader) => loader switch
    {
        LoaderType.Forge => 1,
        LoaderType.Fabric => 4,
        LoaderType.Quilt => 5,
        LoaderType.NeoForge => 6,
        _ => 0
    };

    // ── CurseForge API DTOs ─────────────────────────────────────────────────────

    private class CfSearchResponse
    {
        public List<CfMod>? Data { get; set; }
        public CfPagination Pagination { get; set; } = new();
    }

    private class CfPagination
    {
        public int Index { get; set; }
        public int PageSize { get; set; }
        public int ResultCount { get; set; }
        public int TotalCount { get; set; }
    }

    private class CfModWrapper
    {
        public CfMod? Data { get; set; }
    }

    private class CfFilesWrapper
    {
        public List<CfFile>? Data { get; set; }
    }

    private class CfDescriptionWrapper
    {
        public string? Data { get; set; }
    }

    private class CfFingerprintResponse
    {
        public CfFingerprintData? Data { get; set; }
    }

    private class CfFingerprintData
    {
        public List<CfFingerprintMatch>? ExactMatches { get; set; }
    }

    private class CfFingerprintMatch
    {
        public int Id { get; set; }
        public CfFile? File { get; set; }
    }

    private class CfMod
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Slug { get; set; }
        public string? Summary { get; set; }
        public int DownloadCount { get; set; }
        public int ThumbsUp { get; set; }
        public DateTime? DateModified { get; set; }
        public CfLogo? Logo { get; set; }
        public List<CfAuthor>? Authors { get; set; }
        public List<CfCategory>? Categories { get; set; }
        public List<CfScreenshot>? Screenshots { get; set; }
    }

    private class CfLogo
    {
        public string? Url { get; set; }
    }

    private class CfAuthor
    {
        public string? Name { get; set; }
    }

    private class CfCategory
    {
        public string? Name { get; set; }
    }

    private class CfScreenshot
    {
        public string? Url { get; set; }
        public string? Title { get; set; }
    }

    private class CfFile
    {
        public int Id { get; set; }
        public string? DisplayName { get; set; }
        public string? FileName { get; set; }
        public string? DownloadUrl { get; set; }
        public long FileLength { get; set; }
        public DateTime FileDate { get; set; }
        public int ReleaseType { get; set; }
        public bool IsAvailable { get; set; } = true;
        public List<string>? GameVersions { get; set; }
        public List<CfFileHash>? Hashes { get; set; }
        public List<CfFileDependency>? Dependencies { get; set; }
        public uint FileFingerprint { get; set; }
    }

    private class CfFileHash
    {
        public int Algo { get; set; }
        public string? Value { get; set; }
    }

    private class CfFileDependency
    {
        public int ModId { get; set; }
        public int RelationType { get; set; }
    }
}
