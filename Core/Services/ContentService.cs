using System.IO;
using System.Net.Http;
using System.Text.Json;
using MinecraftControlHub.Core.Models;

namespace MinecraftControlHub.Core.Services;

public interface IContentService
{
    Task<ContentSearchPage> SearchContentAsync(ContentType type, string? query, string? minecraftVersion,
        int offset = 0, int limit = 20, string index = "downloads");
    Task<List<ContentItem>> GetInstalledContentAsync(Guid installationId, ContentType type);
    Task<ContentInstallResult> InstallContentAsync(Guid installationId, ContentSearchResult result, string? minecraftVersion);
    Task DeleteContentAsync(Guid installationId, ContentItem item);
    Task<List<ContentDependencyInfo>> GetShaderDependenciesAsync(string modrinthId, string? minecraftVersion, LoaderType loader);
    Task<List<ContentDependencyInfo>> GetResourcePackDependenciesAsync(string modrinthId, string? minecraftVersion);
    ContentItem? AddFromDrop(Guid installationId, ContentType type, string sourcePath);
    Task<bool> ToggleContentEnabledAsync(Guid installationId, ContentItem item);
    Task<List<ContentVersionInfo>> GetVersionsForContentAsync(string modrinthId, string? minecraftVersion);
    Task<ContentInstallResult> ChangeContentVersionAsync(Guid installationId, ContentItem item, ContentVersionInfo version);
}

/// <summary>A version available on Modrinth for a resourcepack or shaderpack.</summary>
public class ContentVersionInfo
{
    public string Id { get; set; } = string.Empty;
    public string VersionNumber { get; set; } = string.Empty;
    public string? GameVersions { get; set; }
    public string? DownloadUrl { get; set; }
    public string? FileName { get; set; }
}

public class ContentService : IContentService
{
    private readonly HttpClient _httpClient;
    private readonly IAppLogService _log;
    private const string BaseUrl = "https://api.modrinth.com/v2";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    // Known shader-loader mod IDs on Modrinth
    private static readonly Dictionary<string, string> ShaderLoaders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["YL57xq9U"] = "Iris Shaders",
        ["1IjD5062"] = "Oculus (Forge/NeoForge)",
        ["rIC2XJV4"] = "Sodium (required by Iris)",
    };

    public ContentService(HttpClient httpClient, IAppLogService log)
    {
        _httpClient = httpClient;
        _log        = log;
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "MinecraftControlHub/1.0");
    }

    // ── Search ───────────────────────────────────────────────────────────────

    public async Task<ContentSearchPage> SearchContentAsync(ContentType type, string? query, string? minecraftVersion,
        int offset = 0, int limit = 20, string index = "downloads")
    {
        try
        {
            var projectType = type switch
            {
                ContentType.ResourcePack => "resourcepack",
                ContentType.ShaderPack   => "shader",
                ContentType.Modpack      => "modpack",
                _ => "resourcepack"
            };

            var facets = BuildFacets(projectType, minecraftVersion, type == ContentType.World);
            var url = $"{BaseUrl}/search?query={Uri.EscapeDataString(query ?? string.Empty)}"
                     + $"&index={Uri.EscapeDataString(index)}&offset={offset}&limit={limit}"
                     + $"&facets={Uri.EscapeDataString(facets)}";

            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            var raw = JsonSerializer.Deserialize<ContentSearchResponse>(json, JsonOptions);
            if (raw == null) return new ContentSearchPage { Offset = offset, Limit = limit };

            var hits = (raw.Hits ?? new()).Select(h => new ContentSearchResult
            {
                ModrinthId   = h.ProjectId ?? string.Empty,
                Name         = h.Title ?? string.Empty,
                Description  = h.Description,
                Author       = h.Author,
                IconUrl      = h.IconUrl,
                Downloads    = h.Downloads,
                DateModified = h.DateModified,
                Categories   = h.Categories ?? new(),
                ContentType  = type
            }).ToList();

            return new ContentSearchPage { Hits = hits, Offset = raw.Offset, Limit = raw.Limit, TotalHits = raw.TotalHits };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ContentService search error: {ex.Message}");
            return new ContentSearchPage { Offset = offset, Limit = limit };
        }
    }

    private static string BuildFacets(string projectType, string? minecraftVersion, bool isWorld)
    {
        var groups = new List<string> { $"[\"project_type:{projectType}\"]" };

        if (isWorld)
            groups.Add("[\"categories:adventure\",\"categories:world\"]");

        if (IsConcreteVersion(minecraftVersion))
            groups.Add($"[\"versions:{minecraftVersion}\"]");

        return "[" + string.Join(",", groups) + "]";
    }

    private static bool IsConcreteVersion(string? v) =>
        !string.IsNullOrWhiteSpace(v) &&
        System.Text.RegularExpressions.Regex.IsMatch(v, @"^\d+\.\d+(?:\.\d+)?$");

    // ── Installed list ───────────────────────────────────────────────────────

    public Task<List<ContentItem>> GetInstalledContentAsync(Guid installationId, ContentType type)
    {
        var dir = GetContentDir(installationId, type);
        _log.Log("Content.GetInstalled", $"type={type}, dir={dir}");
        if (!Directory.Exists(dir))
        {
            _log.Log("Content.GetInstalled", $"Dir does not exist for type={type}");
            return Task.FromResult(new List<ContentItem>());
        }

        List<ContentItem> items;
        if (type == ContentType.World)
        {
            items = Directory.GetDirectories(dir)
                .Select(d => new ContentItem
                {
                    Type          = type,
                    Name          = Path.GetFileName(d),
                    FileName      = Path.GetFileName(d),
                    FilePath      = d,
                    FileSizeBytes = DirSize(d)
                }).ToList();
        }
        else
        {
            var enabledFiles  = Directory.GetFiles(dir, "*.zip").Concat(Directory.GetFiles(dir, "*.jar"));
            var disabledFiles = Directory.GetFiles(dir, "*.zip.disabled").Concat(Directory.GetFiles(dir, "*.jar.disabled"));
            items = enabledFiles.Select(f => new ContentItem
            {
                Type          = type,
                Name          = Path.GetFileNameWithoutExtension(f),
                FileName      = Path.GetFileName(f),
                FilePath      = f,
                FileSizeBytes = new FileInfo(f).Length,
                IsEnabled     = true
            }).Concat(disabledFiles.Select(f => new ContentItem
            {
                Type          = type,
                Name          = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(f)), // strip .disabled too
                FileName      = Path.GetFileName(f),
                FilePath      = f,
                FileSizeBytes = new FileInfo(f).Length,
                IsEnabled     = false
            })).ToList();
        }

        _log.Log("Content.GetInstalled", $"Returning {items.Count} items for type={type}");
        return Task.FromResult(items);
    }

    private static long DirSize(string path)
    {
        try { return new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length); }
        catch { return 0; }
    }

    // ── Install from Modrinth ────────────────────────────────────────────────

    public async Task<ContentInstallResult> InstallContentAsync(Guid installationId, ContentSearchResult result, string? minecraftVersion)
    {
        try
        {
            _log.Log("Content.Install", $"Installing '{result.Name}' (modrinthId={result.ModrinthId}) for MC {minecraftVersion}");
            var vResp = await _httpClient.GetAsync($"{BaseUrl}/project/{result.ModrinthId}/version");
            _log.Log("Content.Install", $"Versions response: {(int)vResp.StatusCode} {vResp.StatusCode}");
            vResp.EnsureSuccessStatusCode();
            var vJson = await vResp.Content.ReadAsStringAsync();
            var versions = JsonSerializer.Deserialize<List<ContentVersionRaw>>(vJson, JsonOptions);
            if (versions == null || versions.Count == 0)
                return new ContentInstallResult { Error = "No versions found on Modrinth." };

            // Pick newest version compatible with MC version if specified
            ContentVersionRaw? chosen = null;
            if (IsConcreteVersion(minecraftVersion))
                chosen = versions.FirstOrDefault(v => v.GameVersions != null && v.GameVersions.Contains(minecraftVersion!));
            chosen ??= versions[0];

            var file = chosen.Files?.FirstOrDefault(f => f.Primary) ?? chosen.Files?.FirstOrDefault();
            if (file?.Url == null)
                return new ContentInstallResult { Error = "No download URL found." };

            var bytes = await _httpClient.GetByteArrayAsync(file.Url);

            var dir = GetContentDir(installationId, result.ContentType);
            Directory.CreateDirectory(dir);
            var fileName = file.Filename ?? $"{result.ModrinthId}.zip";
            var destPath = Path.Combine(dir, fileName);

            if (result.ContentType == ContentType.World)
            {
                var worldDir = Path.Combine(dir, Path.GetFileNameWithoutExtension(fileName));
                Directory.CreateDirectory(worldDir);
                using var ms = new MemoryStream(bytes);
                System.IO.Compression.ZipFile.ExtractToDirectory(ms, worldDir, overwriteFiles: true);
            }
            else
            {
                await File.WriteAllBytesAsync(destPath, bytes);
            }

            _log.Log("Content.Install", $"Installed '{result.Name}' -> {destPath}");
            return new ContentInstallResult { Success = true, Summary = $"Installed {result.Name} successfully." };
        }
        catch (Exception ex)
        {
            _log.LogError("Content.Install", $"Failed to install '{result.Name}' (modrinthId={result.ModrinthId})", ex);
            return new ContentInstallResult { Error = ex.Message };
        }
    }

    // ── Delete ───────────────────────────────────────────────────────────────

    public Task DeleteContentAsync(Guid installationId, ContentItem item)
    {
        try
        {
            if (item.Type == ContentType.World && item.FilePath != null && Directory.Exists(item.FilePath))
                Directory.Delete(item.FilePath, recursive: true);
            else if (item.FilePath != null && File.Exists(item.FilePath))
                File.Delete(item.FilePath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"DeleteContent error: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    // ── Dependency checks ────────────────────────────────────────────────────

    public async Task<List<ContentDependencyInfo>> GetShaderDependenciesAsync(string modrinthId, string? minecraftVersion, LoaderType loader)
    {
        var result = new List<ContentDependencyInfo>();

        // Add known loader-specific shader requirements
        var relevant = loader switch
        {
            LoaderType.Forge or LoaderType.NeoForge => new[] { "1IjD5062" },          // Oculus
            LoaderType.Fabric or LoaderType.Quilt   => new[] { "YL57xq9U", "rIC2XJV4" }, // Iris + Sodium
            _                                        => new[] { "YL57xq9U", "1IjD5062" }
        };

        foreach (var id in relevant)
        {
            if (ShaderLoaders.TryGetValue(id, out var name))
                result.Add(new ContentDependencyInfo { ProjectId = id, Name = name, IsRequired = true });
        }

        // Also check actual Modrinth project dependencies (if modrinthId given)
        if (!string.IsNullOrWhiteSpace(modrinthId))
        {
            try
            {
                var resp = await _httpClient.GetAsync($"{BaseUrl}/project/{modrinthId}/dependencies");
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("projects", out var projects))
                    {
                        foreach (var proj in projects.EnumerateArray())
                        {
                            var pid    = proj.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty;
                            var ptitle = proj.TryGetProperty("title", out var tp)  ? tp.GetString() : pid;
                            if (!result.Any(r => r.ProjectId == pid))
                                result.Add(new ContentDependencyInfo { ProjectId = pid, Name = ptitle ?? pid, IsRequired = true });
                        }
                    }
                }
            }
            catch { /* best effort */ }
        }

        return result;
    }

    public async Task<List<ContentDependencyInfo>> GetResourcePackDependenciesAsync(string modrinthId, string? minecraftVersion)
    {
        var result = new List<ContentDependencyInfo>();
        if (string.IsNullOrWhiteSpace(modrinthId)) return result;
        try
        {
            var resp = await _httpClient.GetAsync($"{BaseUrl}/project/{modrinthId}/dependencies");
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("projects", out var projects))
                {
                    foreach (var proj in projects.EnumerateArray())
                    {
                        var pid    = proj.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty;
                        var ptitle = proj.TryGetProperty("title", out var tp)  ? tp.GetString() : pid;
                        result.Add(new ContentDependencyInfo { ProjectId = pid, Name = ptitle ?? pid, IsRequired = false });
                    }
                }
            }
        }
        catch { /* best effort */ }
        return result;
    }

    // ── Drag & drop ──────────────────────────────────────────────────────────

    public ContentItem? AddFromDrop(Guid installationId, ContentType type, string sourcePath)
    {
        try
        {
            var dir = GetContentDir(installationId, type);
            Directory.CreateDirectory(dir);
            bool isDir = Directory.Exists(sourcePath);
            string destPath;

            if (isDir)
            {
                var folderName = Path.GetFileName(sourcePath);
                destPath = Path.Combine(dir, folderName);
                CopyDirectory(sourcePath, destPath);
            }
            else
            {
                var fileName = Path.GetFileName(sourcePath);
                destPath = Path.Combine(dir, fileName);
                File.Copy(sourcePath, destPath, overwrite: true);
            }

            return new ContentItem
            {
                Type          = type,
                Name          = Path.GetFileNameWithoutExtension(isDir ? sourcePath : destPath),
                FileName      = Path.GetFileName(destPath),
                FilePath      = destPath,
                FileSizeBytes = isDir ? DirSize(destPath) : new FileInfo(destPath).Length
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AddFromDrop error: {ex.Message}");
            return null;
        }
    }

    // ── Toggle enabled/disabled ──────────────────────────────────────────────

    public Task<bool> ToggleContentEnabledAsync(Guid installationId, ContentItem item)
    {
        if (item.FilePath == null) return Task.FromResult(false);
        try
        {
            if (item.IsEnabled)
            {
                var disabledPath = item.FilePath + ".disabled";
                if (File.Exists(item.FilePath))
                    File.Move(item.FilePath, disabledPath);
                item.FilePath = disabledPath;
                item.FileName = Path.GetFileName(disabledPath);
                item.IsEnabled = false;
            }
            else
            {
                // strip .disabled suffix
                var enabledPath = item.FilePath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                    ? item.FilePath[..^".disabled".Length]
                    : item.FilePath;
                if (File.Exists(item.FilePath))
                    File.Move(item.FilePath, enabledPath);
                item.FilePath = enabledPath;
                item.FileName = Path.GetFileName(enabledPath);
                item.IsEnabled = true;
            }
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ToggleContentEnabled error: {ex.Message}");
            return Task.FromResult(false);
        }
    }

    // ── Version listing & switching ───────────────────────────────────────────

    public async Task<List<ContentVersionInfo>> GetVersionsForContentAsync(string modrinthId, string? minecraftVersion)
    {
        try
        {
            var url = $"{BaseUrl}/project/{modrinthId}/version";
            if (IsConcreteVersion(minecraftVersion))
                url += $"?game_versions={Uri.EscapeDataString($"[\"{minecraftVersion}\"]")}";
            _log.Log("Content.Versions", $"GET {url} (modrinthId={modrinthId}, mcVersion={minecraftVersion})");
            var resp = await _httpClient.GetAsync(url);
            _log.Log("Content.Versions", $"Response: {(int)resp.StatusCode} {resp.StatusCode}");
            if (!resp.IsSuccessStatusCode)
            {
                _log.Log("Content.Versions", $"Non-success for modrinthId={modrinthId}: {(int)resp.StatusCode}");
                return new();
            }
            var json = await resp.Content.ReadAsStringAsync();
            var versions = JsonSerializer.Deserialize<List<ContentVersionRaw>>(json, JsonOptions);
            if (versions == null)
            {
                _log.Log("Content.Versions", $"Deserialization null for modrinthId={modrinthId}");
                return new();
            }

            return versions.Select(v =>
            {
                var file = v.Files?.FirstOrDefault(f => f.Primary) ?? v.Files?.FirstOrDefault();
                return new ContentVersionInfo
                {
                    Id            = v.Id ?? string.Empty,
                    VersionNumber = v.VersionNumber ?? v.Id ?? string.Empty,
                    GameVersions  = v.GameVersions != null ? string.Join(", ", v.GameVersions.Take(3)) : string.Empty,
                    DownloadUrl   = file?.Url,
                    FileName      = file?.Filename
                };
            }).ToList();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GetVersionsForContent error: {ex.Message}");
            return new();
        }
    }

    public async Task<ContentInstallResult> ChangeContentVersionAsync(Guid installationId, ContentItem item, ContentVersionInfo version)
    {
        if (version.DownloadUrl == null)
            return new ContentInstallResult { Error = "No download URL for this version." };
        try
        {
            var dir = GetContentDir(installationId, item.Type);
            _log.Log("Content.ChangeVersion", $"Downloading '{item.Name}' v{version.VersionNumber} from {version.DownloadUrl}");
            var bytes = await _httpClient.GetByteArrayAsync(version.DownloadUrl);
            _log.Log("Content.ChangeVersion", $"Downloaded {bytes.Length:N0} bytes for '{item.Name}'");
            var newFileName = version.FileName ?? Path.GetFileName(version.DownloadUrl);
            var newPath = Path.Combine(dir, newFileName);

            // Delete old file (enabled or disabled)
            if (item.FilePath != null && File.Exists(item.FilePath))
                File.Delete(item.FilePath);
            var disabledOld = item.FilePath + ".disabled";
            if (File.Exists(disabledOld)) File.Delete(disabledOld);

            await File.WriteAllBytesAsync(newPath, bytes);
            item.FilePath      = newPath;
            item.FileName      = newFileName;
            item.Name          = Path.GetFileNameWithoutExtension(newFileName);
            item.FileSizeBytes = bytes.Length;
            item.ModrinthVersionId = version.Id;
            item.IsEnabled     = true;
            return new ContentInstallResult { Success = true, Summary = $"Switched to version {version.VersionNumber}." };
        }
        catch (Exception ex)
        {
            return new ContentInstallResult { Error = ex.Message };
        }
    }

        private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var f in Directory.GetFiles(source))
            File.Copy(f, Path.Combine(dest, Path.GetFileName(f)), overwrite: true);
        foreach (var d in Directory.GetDirectories(source))
            CopyDirectory(d, Path.Combine(dest, Path.GetFileName(d)));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string GetContentDir(Guid installationId, ContentType type)
    {
        var sub = type switch
        {
            ContentType.ResourcePack => "resourcepacks",
            ContentType.ShaderPack   => "shaderpacks",
            ContentType.World        => "saves",
            ContentType.Modpack      => "modpacks",
            _ => "resourcepacks"
        };
        return Path.Combine(AppPaths.InstanceDir(installationId), sub);
    }
}

// ── Internal JSON DTOs ────────────────────────────────────────────────────────

internal class ContentSearchResponse
{
    public List<ContentSearchHit>? Hits { get; set; }
    public int Offset { get; set; }
    public int Limit { get; set; }
    public int TotalHits { get; set; }
}

internal class ContentSearchHit
{
    public string? ProjectId { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Author { get; set; }
    public string? IconUrl { get; set; }
    public int Downloads { get; set; }
    public DateTime? DateModified { get; set; }
    public List<string>? Categories { get; set; }
}

internal class ContentVersionRaw
{
    public string? Id { get; set; }
    public string? VersionNumber { get; set; }
    public List<string>? GameVersions { get; set; }
    public List<ContentVersionFile>? Files { get; set; }
}

internal class ContentVersionFile
{
    public string? Url { get; set; }
    public string? Filename { get; set; }
    public bool Primary { get; set; }
}
