using MinecraftControlHub.Core.Models;
using MinecraftControlHub.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MinecraftControlHub.UI.ViewModels;

public class ContentBrowserViewModel : ViewModelBase
{
    private readonly IContentService _contentService;
    private readonly IModService? _modService;
    private readonly Guid _installationId;
    private readonly string _mcVersion;
    private readonly LoaderType _loader;
    private readonly ContentType _contentType;
    private List<ContentItem> _installedItems;

    private List<ContentSearchResult> _searchResults = new();
    private bool _isSearching;
    private string _searchQuery = string.Empty;
    private int _currentPage = 1;
    private int _totalPages;
    private int _totalHits;
    private List<int> _pageNumbers = new();
    private string _installStatus = string.Empty;
    private string _dependencyStatus = string.Empty;
    private bool _isCheckingDeps;
    private string _lastBrowsedModrinthId = string.Empty;

    private const int PageSize = 20;

    public record SortOption(string Display, string Index)
    {
        public override string ToString() => Display;
    }

    public List<SortOption> SortOptions { get; } = new()
    {
        new SortOption("Downloads",        "downloads"),
        new SortOption("Relevance",        "relevance"),
        new SortOption("Recently updated", "updated"),
        new SortOption("Newest",           "newest")
    };

    private SortOption _selectedSort;
    public SortOption SelectedSort
    {
        get => _selectedSort;
        set
        {
            if (SetProperty(ref _selectedSort, value))
            {
                _currentPage = 1;
                _ = LoadAsync();
            }
        }
    }

    public string WindowTitle => _contentType switch
    {
        ContentType.ResourcePack => "Browse Resource Packs",
        ContentType.ShaderPack   => "Browse Shader Packs",
        ContentType.World        => "Browse Worlds & Maps",
        ContentType.Modpack      => "Browse Modpacks",
        _ => "Browse Content"
    };

    public bool ShowDependencyCheck => _contentType == ContentType.ResourcePack || _contentType == ContentType.ShaderPack;

    public string DependencyCheckLabel => _contentType switch
    {
        ContentType.ShaderPack   => "Check shader dependencies",
        ContentType.ResourcePack => "Check dependencies (optional)",
        _ => string.Empty
    };

    public string SearchQuery
    {
        get => _searchQuery;
        set => SetProperty(ref _searchQuery, value);
    }

    public List<ContentSearchResult> SearchResults
    {
        get => _searchResults;
        set
        {
            if (SetProperty(ref _searchResults, value))
            {
                OnPropertyChanged(nameof(HasResults));
                OnPropertyChanged(nameof(ShowEmptyState));
                OnPropertyChanged(nameof(HasPagination));
            }
        }
    }

    public bool HasResults    => SearchResults.Count > 0;
    public bool ShowEmptyState => !IsSearching && SearchResults.Count == 0;
    public bool HasPagination  => TotalPages > 1;

    public bool IsSearching
    {
        get => _isSearching;
        set
        {
            if (SetProperty(ref _isSearching, value))
                OnPropertyChanged(nameof(ShowEmptyState));
        }
    }

    public int TotalHits
    {
        get => _totalHits;
        set => SetProperty(ref _totalHits, value);
    }

    public int TotalPages
    {
        get => _totalPages;
        set
        {
            if (SetProperty(ref _totalPages, value))
            {
                OnPropertyChanged(nameof(CanGoPrevious));
                OnPropertyChanged(nameof(CanGoNext));
            }
        }
    }

    public List<int> PageNumbers
    {
        get => _pageNumbers;
        set => SetProperty(ref _pageNumbers, value);
    }

    public bool CanGoPrevious => _currentPage > 1;
    public bool CanGoNext     => _currentPage < TotalPages;

    public string InstallStatus
    {
        get => _installStatus;
        set
        {
            if (SetProperty(ref _installStatus, value))
                OnPropertyChanged(nameof(HasInstallStatus));
        }
    }

    public bool HasInstallStatus => !string.IsNullOrEmpty(InstallStatus);

    public string DependencyStatus
    {
        get => _dependencyStatus;
        set
        {
            if (SetProperty(ref _dependencyStatus, value))
                OnPropertyChanged(nameof(HasDependencyStatus));
        }
    }

    public bool HasDependencyStatus => !string.IsNullOrEmpty(DependencyStatus);

    public bool IsCheckingDeps
    {
        get => _isCheckingDeps;
        set => SetProperty(ref _isCheckingDeps, value);
    }

    public ContentBrowserViewModel(
        IContentService contentService,
        Guid installationId,
        string mcVersion,
        LoaderType loader,
        ContentType contentType,
        List<ContentItem> installedItems,
        IModService? modService = null)
    {
        _contentService = contentService;
        _modService     = modService;
        _installationId = installationId;
        _mcVersion      = mcVersion;
        _loader         = loader;
        _contentType    = contentType;
        _installedItems = installedItems;
        _selectedSort   = SortOptions[0];
        _ = LoadAsync();
    }

    public async Task SearchAsync()
    {
        _currentPage = 1;
        await LoadAsync();
    }

    public async Task LoadAsync()
    {
        IsSearching = true;
        try
        {
            var offset = (_currentPage - 1) * PageSize;
            var page = await _contentService.SearchContentAsync(
                _contentType, _searchQuery, _mcVersion,
                offset, PageSize, _selectedSort?.Index ?? "downloads");

            foreach (var h in page.Hits)
                h.IsInstalled = _installedItems.Any(i =>
                    !string.IsNullOrEmpty(i.ModrinthId) && i.ModrinthId == h.ModrinthId);

            SearchResults = page.Hits;
            TotalHits     = page.TotalHits;
            TotalPages    = page.TotalHits > 0 ? (int)Math.Ceiling(page.TotalHits / (double)PageSize) : 0;
            UpdatePageNumbers();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ContentBrowser load error: {ex.Message}");
            SearchResults = new();
            TotalHits = 0; TotalPages = 0;
            UpdatePageNumbers();
        }
        finally { IsSearching = false; }
    }

    public async Task InstallAsync(ContentSearchResult result)
    {
        InstallStatus = $"Installing {result.Name}…";
        _lastBrowsedModrinthId = result.ModrinthId;
        try
        {
            var r = await _contentService.InstallContentAsync(_installationId, result, _mcVersion);
            InstallStatus = r.Success ? $"✓ {result.Name} installed." : $"✗ {r.Error}";
            if (r.Success)
            {
                result.IsInstalled = true;
                // Trigger UI refresh
                SearchResults = new List<ContentSearchResult>(SearchResults);
            }
        }
        catch (Exception ex) { InstallStatus = $"Error: {ex.Message}"; }
    }

    public async Task CheckDependenciesAsync()
    {
        IsCheckingDeps = true;
        DependencyStatus = "Checking…";
        try
        {
            List<ContentDependencyInfo> deps;
            if (_contentType == ContentType.ShaderPack)
                deps = await _contentService.GetShaderDependenciesAsync(_lastBrowsedModrinthId, _mcVersion, _loader);
            else
                deps = await _contentService.GetResourcePackDependenciesAsync(_lastBrowsedModrinthId, _mcVersion);

            if (deps.Count == 0)
            {
                DependencyStatus = "No dependencies found.";
                return;
            }

            // Try to install required deps as mods (Iris, Sodium, Oculus, etc.)
            if (_modService != null)
            {
                var installed = new List<string>();
                var skipped   = new List<string>();
                foreach (var dep in deps.Where(d => d.IsRequired))
                {
                    try
                    {
                        var searchResult = new ModSearchResult
                        {
                            ModrinthId  = dep.ProjectId,
                            Name        = dep.Name,
                            Description = string.Empty
                        };
                        // Dummy installation object to pass loader/version context
                        var tempInst = new Installation
                        {
                            Id               = _installationId,
                            MinecraftVersion = _mcVersion ?? string.Empty,
                            Loader           = _loader
                        };
                        var result = await _modService.InstallModFromSearchAsync(tempInst, searchResult);
                        if (result.Success) installed.Add(dep.Name);
                        else skipped.Add(dep.Name);
                    }
                    catch { skipped.Add(dep.Name); }
                }
                var parts = new List<string>();
                if (installed.Count > 0) parts.Add($"✓ Installed: {string.Join(", ", installed)}");
                if (skipped.Count  > 0) parts.Add($"⚠ Skipped: {string.Join(", ", skipped)}");
                DependencyStatus = string.Join("  |  ", parts);
            }
            else
            {
                // No mod service — just report
                var required = deps.Where(d => d.IsRequired).Select(d => d.Name).ToList();
                var optional = deps.Where(d => !d.IsRequired).Select(d => d.Name).ToList();
                var parts = new List<string>();
                if (required.Count > 0) parts.Add("Required: " + string.Join(", ", required));
                if (optional.Count > 0) parts.Add("Optional: " + string.Join(", ", optional));
                DependencyStatus = string.Join("  |  ", parts);
            }
        }
        catch (Exception ex) { DependencyStatus = $"Error: {ex.Message}"; }
        finally { IsCheckingDeps = false; }
    }

    public async Task PreviousPageAsync()
    {
        if (_currentPage > 1) { _currentPage--; await LoadAsync(); }
    }

    public async Task NextPageAsync()
    {
        if (_currentPage < TotalPages) { _currentPage++; await LoadAsync(); }
    }

    public async Task GoToPageAsync(int page)
    {
        if (page < 1 || page > TotalPages || page == _currentPage) return;
        _currentPage = page;
        await LoadAsync();
    }

    private void UpdatePageNumbers()
    {
        var nums  = new List<int>();
        var start = Math.Max(1, _currentPage - 2);
        var end   = Math.Min(TotalPages, start + 4);
        for (var i = start; i <= end; i++) nums.Add(i);
        PageNumbers = nums;
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(HasPagination));
    }
}
