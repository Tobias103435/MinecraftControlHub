using MinecraftControlHub.Core.Models;
using System.Net.Http;
using System.Text.Json;

namespace MinecraftControlHub.Core.Services;

public interface IModrinthApiClient
{
    Task<ModSearchPage> SearchModsAsync(string? query, string? minecraftVersion = null, LoaderType? loader = null, int offset = 0, int limit = 20, string index = "downloads");
    /// <summary>
    /// Searches Modrinth for server-side content (plugins, mods, datapacks) with explicit
    /// project_type and loader category filters — used by the server plugin/mod browser.
    /// </summary>
    Task<ModSearchPage> SearchServerContentAsync(string? query, string? minecraftVersion, string? projectType, string? loader, int offset = 0, int limit = 20);
    Task<Mod?> GetModAsync(string modrinthId);
    Task<ModDetail?> GetModDetailAsync(string modrinthId);
    Task<List<ModVersion>> GetModVersionsAsync(string modrinthId, string? minecraftVersion = null, LoaderType? loader = null);
    Task<ModVersion?> GetVersionByIdAsync(string versionId);
    /// <summary>
    /// Looks up the exact Modrinth version for a locally installed file by its SHA1 hash
    /// (Modrinth's own "GET /version_file/{hash}" lookup — the same mechanism Modrinth's
    /// own launcher/website use to identify "what build is this jar" with certainty,
    /// instead of guessing from filename/version-number text matching). Returns null if
    /// the hash is unknown to Modrinth (e.g. a manually side-loaded/non-Modrinth jar).
    /// </summary>
    Task<ModVersion?> GetVersionByHashAsync(string sha1Hash);
    Task<byte[]> DownloadModAsync(string downloadUrl);
}

public class ModrinthApiClient : IModrinthApiClient
{
    private readonly HttpClient _httpClient;
    private readonly IAppLogService _log;
    private const string BaseUrl = "https://api.modrinth.com/v2";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public ModrinthApiClient(HttpClient httpClient, IAppLogService log)
    {
        _httpClient = httpClient;
        _log        = log;
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "MinecraftControlHub/1.0");
    }

    public async Task<ModSearchPage> SearchModsAsync(string? query, string? minecraftVersion = null, LoaderType? loader = null, int offset = 0, int limit = 20, string index = "downloads")
    {
        try
        {
            var facets = BuildFacets(minecraftVersion, loader);
            var safeIndex = string.IsNullOrWhiteSpace(index) ? "downloads" : index;
            var url = $"{BaseUrl}/search?query={Uri.EscapeDataString(query ?? string.Empty)}" +
                      $"&index={Uri.EscapeDataString(safeIndex)}&offset={offset}&limit={limit}" +
                      $"&facets={Uri.EscapeDataString(facets)}";

            _log.Log("Modrinth.Search", $"GET {url}");
            var response = await _httpClient.GetAsync(url);
            _log.Log("Modrinth.Search", $"Response: {(int)response.StatusCode} {response.StatusCode}");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var searchResponse = JsonSerializer.Deserialize<ModrinthSearchResponse>(json, JsonOptions);

            if (searchResponse == null)
            {
                _log.Log("Modrinth.Search", "Deserialization returned null");
                return new ModSearchPage { Offset = offset, Limit = limit };
            }

            var hits = (searchResponse.Hits ?? new List<ModrinthSearchHit>()).Select(h => new ModSearchResult
            {
                ModrinthId = h.ProjectId,
                Name = h.Title,
                Description = h.Description,
                Author = h.Author,
                IconUrl = h.IconUrl,
                Downloads = h.Downloads,
                DateModified = h.DateModified,
                Categories = h.Categories ?? new List<string>()
            }).ToList();

            _log.Log("Modrinth.Search", $"Fetched {hits.Count} results (total={searchResponse.TotalHits}, offset={offset})");
            return new ModSearchPage
            {
                Hits = hits,
                Offset = searchResponse.Offset,
                Limit = searchResponse.Limit,
                TotalHits = searchResponse.TotalHits
            };
        }
        catch (Exception ex)
        {
            _log.LogError("Modrinth.Search", $"Search failed for query='{query}'", ex);
            return new ModSearchPage { Offset = offset, Limit = limit };
        }
    }

    /// <summary>
    /// Searches Modrinth for server-side content (plugins, mods, datapacks) with explicit
    /// project_type and loader category filters. Unlike <see cref="SearchModsAsync"/> which
    /// always forces project_type:mod, this method lets the caller pick any project_type
    /// ("plugin", "mod", "datapack", "resourcepack", "shader", "modpack") and a raw loader
    /// category string ("paper", "fabric", "forge", "neoforge", "quilt", "spigot", etc.).
    /// Used by the ServerPluginBrowserWindow to browse plugins/mods/datapacks for a server.
    /// </summary>
    public async Task<ModSearchPage> SearchServerContentAsync(string? query, string? minecraftVersion, string? projectType, string? loader, int offset = 0, int limit = 20)
    {
        try
        {
            var facets = BuildServerContentFacets(minecraftVersion, projectType, loader);
            var url = $"{BaseUrl}/search?query={Uri.EscapeDataString(query ?? string.Empty)}" +
                      $"&index=downloads&offset={offset}&limit={limit}" +
                      $"&facets={Uri.EscapeDataString(facets)}";

            _log.Log("Modrinth.SearchServerContent", $"GET {url}");
            var response = await _httpClient.GetAsync(url);
            _log.Log("Modrinth.SearchServerContent", $"Response: {(int)response.StatusCode} {response.StatusCode}");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var searchResponse = JsonSerializer.Deserialize<ModrinthSearchResponse>(json, JsonOptions);

            if (searchResponse == null)
            {
                _log.Log("Modrinth.SearchServerContent", "Deserialization returned null");
                return new ModSearchPage { Offset = offset, Limit = limit };
            }

            var hits = (searchResponse.Hits ?? new List<ModrinthSearchHit>()).Select(h => new ModSearchResult
            {
                ModrinthId = h.ProjectId,
                Name = h.Title,
                Description = h.Description,
                Author = h.Author,
                IconUrl = h.IconUrl,
                Downloads = h.Downloads,
                DateModified = h.DateModified,
                Categories = h.Categories ?? new List<string>()
            }).ToList();

            _log.Log("Modrinth.SearchServerContent", $"Fetched {hits.Count} results (total={searchResponse.TotalHits}, offset={offset})");
            return new ModSearchPage
            {
                Hits = hits,
                Offset = searchResponse.Offset,
                Limit = searchResponse.Limit,
                TotalHits = searchResponse.TotalHits
            };
        }
        catch (Exception ex)
        {
            _log.LogError("Modrinth.SearchServerContent", $"SearchServerContent failed for query='{query}', projectType='{projectType}', loader='{loader}'", ex);
            return new ModSearchPage { Offset = offset, Limit = limit };
        }
    }

    /// <summary>
    /// Builds the Modrinth facets JSON for server content searches. Each facet group is
    /// ANDed together; within a group items are ORed. We always include the project_type
    /// facet, optionally the loader category and Minecraft version facets.
    ///
    /// For Paper/Purpur plugin searches the loader argument will be "paper". Modrinth
    /// categorises plugins under "paper", "spigot", "bukkit" and "purpur" — all of which
    /// are compatible with a Paper server. We therefore OR those categories together in a
    /// single facet group so that plugins tagged with only "bukkit" or "spigot" still show
    /// up instead of being silently filtered out.
    /// </summary>
    private static string BuildServerContentFacets(string? minecraftVersion, string? projectType, string? loader)
    {
        var groups = new List<string>();

        // Project type facet (always required — "plugin", "mod", "datapack", etc.)
        if (!string.IsNullOrWhiteSpace(projectType))
            groups.Add($"[\"project_type:{projectType}\"]");

        // Loader/category facet — ORed within the group so that e.g. a Paper server
        // matches plugins tagged as "paper", "spigot", "bukkit" or "purpur".
        if (!string.IsNullOrWhiteSpace(loader))
        {
            var categoryGroup = loader.ToLowerInvariant() switch
            {
                // Paper is backwards-compatible with Spigot and Bukkit plugins,
                // and many Purpur plugins also work on Paper.
                "paper"  => "[\"categories:paper\",\"categories:spigot\",\"categories:bukkit\",\"categories:purpur\"]",
                // Purpur is compatible with Paper/Spigot/Bukkit as well.
                "purpur" => "[\"categories:purpur\",\"categories:paper\",\"categories:spigot\",\"categories:bukkit\"]",
                // All other loaders (fabric, forge, neoforge, quilt, …) map 1-to-1.
                _        => $"[\"categories:{loader}\"]"
            };
            groups.Add(categoryGroup);
        }

        // Minecraft version facet — only for concrete versions
        if (IsConcreteVersion(minecraftVersion))
            groups.Add($"[\"versions:{minecraftVersion}\"]");

        return "[" + string.Join(",", groups) + "]";
    }

    private static string BuildFacets(string? minecraftVersion, LoaderType? loader)
    {
        var groups = new List<string>();
        // Only apply the version facet when we have a concrete Minecraft version
        // (e.g. "1.21.2"). Launcher-imported profiles can carry placeholders like
        // "latest-release" / "latest-snapshot", which Modrinth doesn't recognise and
        // which would otherwise return zero results ("no mods shown").
        if (IsConcreteVersion(minecraftVersion))
            groups.Add($"[\"versions:{minecraftVersion}\"]");

        var loaderFacet = MapLoaderToFacet(loader);
        if (loaderFacet != null)
            groups.Add($"[\"categories:{loaderFacet}\"]");

        groups.Add("[\"project_type:mod\"]");
        return "[" + string.Join(",", groups) + "]";
    }

    /// <summary>
    /// True when the string looks like a real Minecraft version number (e.g. "1.21.2"),
    /// as opposed to launcher placeholders such as "latest-release"/"latest-snapshot".
    /// </summary>
    private static bool IsConcreteVersion(string? version)
    {
        return !string.IsNullOrWhiteSpace(version)
               && System.Text.RegularExpressions.Regex.IsMatch(version, @"^\d+\.\d+(?:\.\d+)?$");
    }

    private static string? MapLoaderToFacet(LoaderType? loader)
    {
        return loader switch
        {
            LoaderType.Forge => "forge",
            LoaderType.Fabric => "fabric",
            LoaderType.NeoForge => "neoforge",
            LoaderType.Quilt => "quilt",
            _ => null
        };
    }

    public async Task<Mod?> GetModAsync(string modrinthId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{BaseUrl}/project/{modrinthId}");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var modrinthProject = JsonSerializer.Deserialize<ModrinthProject>(json, JsonOptions);

            if (modrinthProject == null)
                return null;

            return new Mod
            {
                ModrinthId = modrinthProject.Id,
                Name = modrinthProject.Title,
                Description = modrinthProject.Description,
                Author = modrinthProject.Author,
                IconUrl = modrinthProject.IconUrl
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<ModDetail?> GetModDetailAsync(string modrinthId)
    {
        try
        {
            var url = $"{BaseUrl}/project/{modrinthId}";
            _log.Log("Modrinth.Detail", $"GET {url}");
            var response = await _httpClient.GetAsync(url);
            _log.Log("Modrinth.Detail", $"Response: {(int)response.StatusCode} {response.StatusCode}");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var project = JsonSerializer.Deserialize<ModrinthProject>(json, JsonOptions);

            if (project == null)
            {
                _log.Log("Modrinth.Detail", $"Deserialization returned null for project={modrinthId}");
                return null;
            }

            return new ModDetail
            {
                ModrinthId = project.Id,
                Name = project.Title,
                Description = project.Description,
                Body = project.Body,
                Author = project.Author,
                IconUrl = project.IconUrl,
                Downloads = project.Downloads,
                Followers = project.Followers,
                Categories = project.Categories ?? new List<string>(),
                Gallery = (project.Gallery ?? new List<ModrinthGalleryImage>())
                    .OrderByDescending(g => g.Featured)
                    .ThenBy(g => g.Ordering)
                    .Select(g => new ModGalleryImage
                    {
                        Url = g.Url,
                        Title = g.Title,
                        Description = g.Description
                    })
                    .Where(g => !string.IsNullOrEmpty(g.Url))
                    .ToList()
            };
        }
        catch (Exception ex)
        {
            _log.LogError("Modrinth.Detail", $"GetModDetailAsync failed for project={modrinthId}", ex);
            return null;
        }
    }

    public async Task<List<ModVersion>> GetModVersionsAsync(string modrinthId, string? minecraftVersion = null, LoaderType? loader = null)
    {
        try
        {
            // Ask Modrinth to filter server-side too (faster + fewer irrelevant
            // results), but ALSO re-check client-side below: a version can support
            // several Minecraft versions at once (its game_versions is a list), so
            // relying on either side alone previously let mismatched builds through.
            var url = $"{BaseUrl}/project/{modrinthId}/version";
            var query = new List<string>();

            var loaderFacet = MapLoaderToFacet(loader);
            if (loaderFacet != null)
            {
                var loadersJson = "[\"" + loaderFacet + "\"]";
                query.Add("loaders=" + Uri.EscapeDataString(loadersJson));
            }

            if (IsConcreteVersion(minecraftVersion))
            {
                var versionsJson = "[\"" + minecraftVersion + "\"]";
                query.Add("game_versions=" + Uri.EscapeDataString(versionsJson));
            }

            if (query.Count > 0)
                url += "?" + string.Join("&", query);

            _log.Log("Modrinth.Versions", $"GET {url}");
            var response = await _httpClient.GetAsync(url);
            _log.Log("Modrinth.Versions", $"Response: {(int)response.StatusCode} {response.StatusCode}");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var modrinthVersions = JsonSerializer.Deserialize<List<ModrinthVersion>>(json, JsonOptions);

            if (modrinthVersions == null)
            {
                _log.Log("Modrinth.Versions", $"Deserialization returned null for project={modrinthId}");
                return new List<ModVersion>();
            }

            var versions = modrinthVersions.Select(v => MapVersion(v, modrinthId)).ToList();

            // Strict client-side filter: only return versions that explicitly support
            // the requested Minecraft version. If none match → return empty list so
            // the UI can show "not available for this version" instead of installing
            // an incompatible build.
            if (IsConcreteVersion(minecraftVersion))
            {
                versions = versions
                    .Where(v => v.GameVersions.Contains(minecraftVersion!, StringComparer.OrdinalIgnoreCase))
                    .ToList();
            }

            if (loader.HasValue)
            {
                versions = versions.Where(v => v.Loader == loader.Value).ToList();
            }

            return versions;
        }
        catch (Exception)
        {
            return new List<ModVersion>();
        }
    }

    public async Task<ModVersion?> GetVersionByIdAsync(string versionId)
    {
        try
        {
            var url = $"{BaseUrl}/version/{versionId}";
            _log.Log("Modrinth.VersionById", $"GET {url}");
            var response = await _httpClient.GetAsync(url);
            _log.Log("Modrinth.VersionById", $"Response: {(int)response.StatusCode} {response.StatusCode}");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var version = JsonSerializer.Deserialize<ModrinthVersion>(json, JsonOptions);

            return version == null ? null : MapVersion(version, version.ProjectId ?? string.Empty);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Modrinth version error: {ex.Message}");
            return null;
        }
    }

    public async Task<ModVersion?> GetVersionByHashAsync(string sha1Hash)
    {
        try
        {
            // This is the exact lookup Modrinth's own launcher/website use to identify
            // an arbitrary local jar with certainty: hash it, ask Modrinth "what build is
            // this", done — no guessing from filename text or a loader-filtered list that
            // might not even contain the file. A 404 here just means the hash isn't a
            // known Modrinth file (not an error worth logging loudly).
            var response = await _httpClient.GetAsync($"{BaseUrl}/version_file/{sha1Hash}?algorithm=sha1");
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync();
            var version = JsonSerializer.Deserialize<ModrinthVersion>(json, JsonOptions);

            return version == null ? null : MapVersion(version, version.ProjectId ?? string.Empty);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Modrinth version-by-hash error: {ex.Message}");
            return null;
        }
    }

    private static ModVersion MapVersion(ModrinthVersion v, string modrinthId)
    {
        // Modrinth marks the download file to use as the "primary" one; fall back to the first.
        var file = v.Files?.FirstOrDefault(f => f.Primary) ?? v.Files?.FirstOrDefault();
        var gameVersions = v.GameVersions ?? new List<string>();
        return new ModVersion
        {
            Id = v.Id,
            ModrinthId = string.IsNullOrEmpty(v.ProjectId) ? modrinthId : v.ProjectId!,
            VersionNumber = v.VersionNumber,
            Name = v.Name,
            // Newest-listed supported version, purely for display; compatibility
            // checks elsewhere must use the full GameVersions list below.
            MinecraftVersion = gameVersions.LastOrDefault(),
            GameVersions = gameVersions,
            Loader = ParseLoaderType(v.Loaders),
            DownloadUrl = file?.Url,
            FileName = file?.Filename,
            FileSize = file?.Size ?? 0,
            DatePublished = v.DatePublished,
            Sha1Hash = file?.Hashes?.GetValueOrDefault("sha1"),
            Sha512Hash = file?.Hashes?.GetValueOrDefault("sha512"),
            VersionType = v.VersionType,
            Dependencies = (v.Dependencies ?? new List<ModrinthDependency>())
                .Select(d => new ModDependency
                {
                    ProjectId = d.ProjectId,
                    VersionId = d.VersionId,
                    Type = ParseDependencyType(d.DependencyType)
                })
                .ToList()
        };
    }

    private static DependencyType ParseDependencyType(string? type)
    {
        return type?.ToLowerInvariant() switch
        {
            "optional" => DependencyType.Optional,
            "incompatible" => DependencyType.Incompatible,
            "embedded" => DependencyType.Embedded,
            _ => DependencyType.Required
        };
    }

    private static LoaderType ParseLoaderType(List<string>? loaders)
    {
        if (loaders == null || loaders.Count == 0)
            return LoaderType.Vanilla;

        // FOUND BUG (this is very likely the real root cause behind "check thinks
        // everything's compatible but it isn't" / "grabs the wrong mods for 1.21" on
        // NeoForge): this used to check "forge" BEFORE "neoforge". NeoForge is a fork of
        // Forge, and it's extremely common on Modrinth for a single file to be tagged
        // with BOTH ["forge", "neoforge"] (older NeoForge builds carried the legacy
        // "forge" tag too, or a project just declares both to maximize visibility). With
        // "forge" checked first, every one of those dual-tagged NeoForge files got
        // permanently mis-classified as LoaderType.Forge instead of NeoForge. That
        // silently corrupts everything downstream for a NeoForge installation: the
        // client-side loader re-filter in GetModVersionsAsync throws the file out of the
        // "neoforge" results entirely, and if it slips through anyway (e.g. via
        // GetVersionByIdAsync/GetVersionByHashAsync, which don't re-filter), the
        // installed Mod record ends up stamped Loader=Forge — so every later
        // "is this compatible with installation.Loader" check (dependency compatibility,
        // update resolution, etc.) compares NeoForge != Forge and either wrongly flags a
        // genuinely fine mod as incompatible, or — worse — wrongly waves through/loses
        // track of a real problem because the comparison landed on the wrong enum value.
        // Fix: check the more specific fork tags (neoforge/quilt) before the legacy base
        // tags they were forked from (forge/fabric), so a dual-tagged file resolves to
        // the loader that's actually relevant.
        var loader = loaders.FirstOrDefault(l => l.Equals("neoforge", StringComparison.OrdinalIgnoreCase));
        if (loader != null) return LoaderType.NeoForge;

        loader = loaders.FirstOrDefault(l => l.Equals("quilt", StringComparison.OrdinalIgnoreCase));
        if (loader != null) return LoaderType.Quilt;

        loader = loaders.FirstOrDefault(l => l.Equals("forge", StringComparison.OrdinalIgnoreCase));
        if (loader != null) return LoaderType.Forge;

        loader = loaders.FirstOrDefault(l => l.Equals("fabric", StringComparison.OrdinalIgnoreCase));
        if (loader != null) return LoaderType.Fabric;

        return LoaderType.Vanilla;
    }

    public async Task<byte[]> DownloadModAsync(string downloadUrl)
    {
        var response = await _httpClient.GetAsync(downloadUrl);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync();
    }

    // DTOs for Modrinth API responses
    private class ModrinthSearchResponse
    {
        public List<ModrinthSearchHit>? Hits { get; set; }
        public int Offset { get; set; }
        public int Limit { get; set; }
        public int TotalHits { get; set; }
    }

    private class ModrinthSearchHit
    {
        public string ProjectId { get; set; } = string.Empty;
        public string? Slug { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Author { get; set; }
        public string? IconUrl { get; set; }
        public int Downloads { get; set; }
        public DateTime? DateModified { get; set; }
        public List<string>? Categories { get; set; }
        public List<string>? Versions { get; set; }
    }

    private class ModrinthProject
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Body { get; set; }
        public string? Author { get; set; }
        public string? IconUrl { get; set; }
        public int Downloads { get; set; }
        public int Followers { get; set; }
        public List<string>? Categories { get; set; }
        public List<ModrinthGalleryImage>? Gallery { get; set; }
    }

    private class ModrinthGalleryImage
    {
        public string Url { get; set; } = string.Empty;
        public bool Featured { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public int Ordering { get; set; }
    }

    private class ModrinthVersion
    {
        public string Id { get; set; } = string.Empty;
        public string? ProjectId { get; set; }
        public string VersionNumber { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public List<string>? GameVersions { get; set; }
        public List<string>? Loaders { get; set; }
        public List<ModrinthFile>? Files { get; set; }
        public List<ModrinthDependency>? Dependencies { get; set; }
        public DateTime DatePublished { get; set; }
        public string? VersionType { get; set; }
    }

    private class ModrinthDependency
    {
        public string? ProjectId { get; set; }
        public string? VersionId { get; set; }
        public string? DependencyType { get; set; }
    }

    private class ModrinthFile
    {
        public Dictionary<string, string>? Hashes { get; set; }
        public string Url { get; set; } = string.Empty;
        public string? Filename { get; set; }
        public bool Primary { get; set; }
        public long Size { get; set; }
    }
}
