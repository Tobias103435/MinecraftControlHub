using MinecraftControlHub.Core.Models;
using MinecraftControlHub.Core.Services;

namespace MinecraftControlHub.UI.ViewModels;

public class ModsPageViewModel : ViewModelBase
{
    private readonly IModService _modService;
    private readonly IInstallationService _installationService;
    private readonly IContentService _contentService;
    private readonly IModrinthApiClient _modrinthClient;
    private List<Installation> _installations;
    private List<ModSearchResult> _searchResults;
    private List<Mod> _installedMods;
    private bool _isSearching;
    private string _searchQuery = string.Empty;
    private Installation? _selectedInstallation;
    private int _currentPage = 1;
    private int _totalPages;
    private int _totalHits;
    private List<int> _pageNumbers = new();
    private ModSearchResult? _selectedMod;
    private ModDetail? _selectedModDetail;
    private bool _isDetailVisible;
    private bool _isDetailLoading;
    private SortOption _selectedSort;

    private const int PageSize = 20;

    public record SortOption(string Display, string Index)
    {
        public override string ToString() => Display;
    }

    /// <summary>Dropdown item for the source selector (Modrinth / CurseForge / Both).</summary>
    public record SourceOption(string Display, ModSource? Value)
    {
        public override string ToString() => Display;
    }

    public List<SourceOption> SourceOptions { get; } = new()
    {
        new SourceOption("Both", null),
        new SourceOption("Modrinth", ModSource.Modrinth),
        new SourceOption("CurseForge", ModSource.CurseForge)
    };

    private SourceOption? _selectedSourceOption;
    public SourceOption? SelectedSourceOption
    {
        get => _selectedSourceOption;
        set
        {
            if (SetProperty(ref _selectedSourceOption, value) && SelectedInstallation != null)
            {
                CurrentPage = 1;
                _ = LoadModsAsync();
            }
        }
    }

    public List<SortOption> SortOptions { get; } = new()
    {
        new SortOption("Relevance", "relevance"),
        new SortOption("Downloads", "downloads"),
        new SortOption("Popularity (Follows)", "follows"),
        new SortOption("Recently updated", "updated"),
        new SortOption("Newest", "newest")
    };

    public SortOption SelectedSort
    {
        get => _selectedSort;
        set
        {
            if (SetProperty(ref _selectedSort, value) && SelectedInstallation != null)
            {
                CurrentPage = 1;
                _ = LoadModsAsync();
            }
        }
    }

    public List<Installation> Installations
    {
        get => _installations;
        set => SetProperty(ref _installations, value);
    }

    public Installation? SelectedInstallation
    {
        get => _selectedInstallation;
        set
        {
            if (SetProperty(ref _selectedInstallation, value))
            {
                OnPropertyChanged(nameof(HasInstallationSelected));
                // Auto-fill version and loader from selected installation
                if (value != null)
                {
                    SelectedMinecraftVersion = value.MinecraftVersion;
                    SelectedLoader = value.Loader;
                    CurrentPage = 1;
                    _ = LoadInstalledModsAsync();
                    _ = LoadModsAsync();
                    // Reset modpack search state when switching installations
                    _modpackResults = new List<ContentSearchResult>();
                    _modpackCards = new List<ModpackCardViewModel>();
                    _showModpackPrompt = true;
                    _modpackTotalHits = 0;
                    OnPropertyChanged(nameof(ModpackResults));
                    OnPropertyChanged(nameof(ModpackCards));
                    OnPropertyChanged(nameof(ShowModpackPrompt));
                    OnPropertyChanged(nameof(HasModpackResults));
                }
            }
        }
    }

    public List<ModSearchResult> SearchResults
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

    public bool HasResults => SearchResults.Count > 0;
    public bool ShowEmptyState => SelectedInstallation != null && !IsSearching && SearchResults.Count == 0;
    public bool HasPagination => TotalPages > 1;

    public List<Mod> InstalledMods
    {
        get => _installedMods;
        set => SetProperty(ref _installedMods, value);
    }

    public bool IsSearching
    {
        get => _isSearching;
        set
        {
            if (SetProperty(ref _isSearching, value))
            {
                OnPropertyChanged(nameof(ShowEmptyState));
            }
        }
    }

    public int CurrentPage
    {
        get => _currentPage;
        set => SetProperty(ref _currentPage, value);
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

    public int TotalHits
    {
        get => _totalHits;
        set => SetProperty(ref _totalHits, value);
    }

    public List<int> PageNumbers
    {
        get => _pageNumbers;
        set => SetProperty(ref _pageNumbers, value);
    }

    public bool CanGoPrevious => CurrentPage > 1;
    public bool CanGoNext => CurrentPage < TotalPages;

    public ModSearchResult? SelectedMod
    {
        get => _selectedMod;
        set
        {
            if (SetProperty(ref _selectedMod, value))
            {
                OnPropertyChanged(nameof(SelectedModCategories));
            }
        }
    }

    public ModDetail? SelectedModDetail
    {
        get => _selectedModDetail;
        set
        {
            if (SetProperty(ref _selectedModDetail, value))
            {
                _galleryIndex = 0;
                OnPropertyChanged(nameof(HasScreenshots));
                NotifyGalleryChanged();
            }
        }
    }

    public bool HasScreenshots => SelectedModDetail?.Gallery.Count > 0;

    private int _galleryIndex;

    public ModGalleryImage? CurrentGalleryImage =>
        SelectedModDetail != null
        && SelectedModDetail.Gallery.Count > 0
        && _galleryIndex >= 0
        && _galleryIndex < SelectedModDetail.Gallery.Count
            ? SelectedModDetail.Gallery[_galleryIndex]
            : null;

    public string GalleryCounter =>
        HasScreenshots ? $"{_galleryIndex + 1} / {SelectedModDetail!.Gallery.Count}" : string.Empty;

    public bool CanGoNextImage =>
        SelectedModDetail != null && _galleryIndex < SelectedModDetail.Gallery.Count - 1;

    public bool CanGoPreviousImage => _galleryIndex > 0;

    public bool HasMultipleScreenshots => (SelectedModDetail?.Gallery.Count ?? 0) > 1;

    public void NextImage()
    {
        if (!CanGoNextImage)
            return;
        _galleryIndex++;
        NotifyGalleryChanged();
    }

    public void PreviousImage()
    {
        if (!CanGoPreviousImage)
            return;
        _galleryIndex--;
        NotifyGalleryChanged();
    }

    private void NotifyGalleryChanged()
    {
        OnPropertyChanged(nameof(CurrentGalleryImage));
        OnPropertyChanged(nameof(GalleryCounter));
        OnPropertyChanged(nameof(CanGoNextImage));
        OnPropertyChanged(nameof(CanGoPreviousImage));
        OnPropertyChanged(nameof(HasMultipleScreenshots));
    }

    public bool IsDetailLoading
    {
        get => _isDetailLoading;
        set => SetProperty(ref _isDetailLoading, value);
    }

    public bool IsDetailVisible
    {
        get => _isDetailVisible;
        set => SetProperty(ref _isDetailVisible, value);
    }

    public string SelectedModCategories =>
        SelectedMod?.Categories != null ? string.Join(", ", SelectedMod.Categories) : string.Empty;

    public void ShowDetail(ModSearchResult mod)
    {
        SelectedMod = mod;
        SelectedModDetail = null;
        IsDetailVisible = true;
        if (mod.Source == ModSource.CurseForge && mod.CurseForgeId.HasValue)
            _ = LoadCfDetailAsync(mod.CurseForgeId.Value);
        else
            _ = LoadDetailAsync(mod.ModrinthId);
    }

    private async Task LoadCfDetailAsync(int curseForgeId)
    {
        IsDetailLoading = true;
        try
        {
            SelectedModDetail = await _modService.GetCfModDetailAsync(curseForgeId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"CF Detail load error: {ex.Message}");
            SelectedModDetail = null;
        }
        finally
        {
            IsDetailLoading = false;
        }
    }

    private async Task LoadDetailAsync(string modrinthId)
    {
        IsDetailLoading = true;
        try
        {
            SelectedModDetail = await _modService.GetModDetailAsync(modrinthId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Detail load error: {ex.Message}");
            SelectedModDetail = null;
        }
        finally
        {
            IsDetailLoading = false;
        }
    }

    public void CloseDetail()
    {
        IsDetailVisible = false;
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set => SetProperty(ref _searchQuery, value);
    }

    public string SelectedMinecraftVersion { get; private set; } = string.Empty;
    public LoaderType SelectedLoader { get; private set; }

    public bool HasInstallationSelected => SelectedInstallation != null;

    public ModsPageViewModel(IModService modService, IInstallationService installationService, IContentService contentService, IModrinthApiClient modrinthClient)
    {
        _modService = modService;
        _installationService = installationService;
        _contentService = contentService;
        _modrinthClient = modrinthClient;
        _searchResults = new List<ModSearchResult>();
        _installedMods = new List<Mod>();
        _installations = new List<Installation>();
        _selectedSort = SortOptions[1];
        _selectedSourceOption = SourceOptions[0]; // Both
        _ = LoadInstallationsAsync();
    }

    private async Task LoadInstallationsAsync()
    {
        Installations = await _installationService.GetAllInstallationsAsync();
    }

    public async Task SearchModsAsync()
    {
        CurrentPage = 1;
        await LoadModsAsync();
    }

    public async Task LoadModsAsync()
    {
        if (SelectedInstallation == null)
            return;

        IsSearching = true;
        try
        {
            var offset = (CurrentPage - 1) * PageSize;
            var page = await _modService.SearchUnifiedAsync(
                SearchQuery,
                SelectedInstallation.MinecraftVersion,
                SelectedInstallation.Loader,
                SelectedSourceOption?.Value,
                offset,
                PageSize,
                SelectedSort?.Index ?? "downloads");

            SearchResults = page.Hits;
            MarkInstalledStatus();
            TotalHits = page.TotalHits;
            TotalPages = page.TotalHits > 0 ? (int)Math.Ceiling(page.TotalHits / (double)PageSize) : 0;
            UpdatePageNumbers();
            OnPropertyChanged(nameof(CanGoPrevious));
            OnPropertyChanged(nameof(CanGoNext));
            OnPropertyChanged(nameof(HasPagination));
            System.Diagnostics.Debug.WriteLine($"Search returned {page.Hits.Count} results (total {page.TotalHits})");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Search error: {ex.Message}");
            SearchResults = new List<ModSearchResult>();
            TotalHits = 0;
            TotalPages = 0;
            UpdatePageNumbers();
        }
        finally
        {
            IsSearching = false;
        }
    }

    public async Task GoToPageAsync(int page)
    {
        if (page < 1 || page > TotalPages || page == CurrentPage)
            return;

        CurrentPage = page;
        await LoadModsAsync();
    }

    public async Task NextPageAsync()
    {
        if (!CanGoNext)
            return;

        CurrentPage++;
        await LoadModsAsync();
    }

    public async Task PreviousPageAsync()
    {
        if (!CanGoPrevious)
            return;

        CurrentPage--;
        await LoadModsAsync();
    }

    private void UpdatePageNumbers()
    {
        if (TotalPages <= 0)
        {
            PageNumbers = new List<int>();
            return;
        }

        // Show a window of up to 7 page numbers around the current page.
        const int window = 7;
        var start = Math.Max(1, CurrentPage - window / 2);
        var end = Math.Min(TotalPages, start + window - 1);
        start = Math.Max(1, end - window + 1);

        var numbers = new List<int>();
        for (var i = start; i <= end; i++)
            numbers.Add(i);
        PageNumbers = numbers;
    }

    public async Task LoadInstalledModsAsync()
    {
        if (SelectedInstallation == null)
            return;

        InstalledMods = await _modService.GetInstalledModsAsync(SelectedInstallation.Id);
        MarkInstalledStatus();

        // Check each installed mod for a newer compatible version in the background,
        // then republish so the "Update" buttons appear where relevant.
        _ = RefreshUpdateStatusAsync();

        // Clear any dependency-check results from a previously selected installation —
        // they no longer apply to this one.
        DependencyIssues = new List<DependencyIssue>();
        DependencyStatus = string.Empty;
    }

    private async Task RefreshUpdateStatusAsync()
    {
        if (SelectedInstallation == null)
            return;

        try
        {
            await _modService.RefreshUpdateStatusAsync(SelectedInstallation);
            // Republish a fresh list so the ItemsControl rebinds the update state.
            InstalledMods = new List<Mod>(InstalledMods);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Update-status refresh failed: {ex.Message}");
        }
    }

    private bool _isCheckingModUpdates;
    public bool IsCheckingModUpdates
    {
        get => _isCheckingModUpdates;
        set => SetProperty(ref _isCheckingModUpdates, value);
    }

    private string _updateStatus = string.Empty;
    public string UpdateStatus
    {
        get => _updateStatus;
        set
        {
            if (SetProperty(ref _updateStatus, value))
                OnPropertyChanged(nameof(HasUpdateStatus));
        }
    }
    public bool HasUpdateStatus => !string.IsNullOrWhiteSpace(UpdateStatus);

    /// <summary>Explicit, user-triggered "Check for updates" for the Mods page's
    /// Installed tab (mirrors HomePage's installation-detail equivalent) — on top of
    /// the silent background refresh that already runs whenever the tab loads.</summary>
    public async Task CheckForUpdatesAsync()
    {
        if (SelectedInstallation == null || IsCheckingModUpdates)
            return;

        IsCheckingModUpdates = true;
        UpdateStatus = string.Empty;
        try
        {
            var result = await _modService.CheckForUpdatesAsync(new[] { SelectedInstallation }, applyUpdates: false);
            UpdateStatus = result.Summary;
            InstalledMods = new List<Mod>(InstalledMods);
        }
        catch (Exception ex)
        {
            UpdateStatus = $"Update check failed: {ex.Message}";
        }
        finally
        {
            IsCheckingModUpdates = false;
        }
    }

    private bool _isCheckingDependencies;
    public bool IsCheckingDependencies
    {
        get => _isCheckingDependencies;
        set => SetProperty(ref _isCheckingDependencies, value);
    }

    private string _dependencyStatus = string.Empty;
    public string DependencyStatus
    {
        get => _dependencyStatus;
        set
        {
            if (SetProperty(ref _dependencyStatus, value))
                OnPropertyChanged(nameof(HasDependencyStatus));
        }
    }
    public bool HasDependencyStatus => !string.IsNullOrWhiteSpace(DependencyStatus);

    private List<DependencyIssue> _dependencyIssues = new();
    public List<DependencyIssue> DependencyIssues
    {
        get => _dependencyIssues;
        set
        {
            if (SetProperty(ref _dependencyIssues, value))
                OnPropertyChanged(nameof(HasDependencyIssues));
        }
    }
    public bool HasDependencyIssues => DependencyIssues.Count > 0;

    /// <summary>Scans every installed mod on this installation for missing/incompatible
    /// required dependencies (e.g. Iris installed without Sodium) — same engine as
    /// HomePage's installation-detail "Check for dependencies", surfaced here too since
    /// this is where people actually look at their installed mods.</summary>
    public async Task CheckDependenciesAsync()
    {
        if (SelectedInstallation == null || IsCheckingDependencies)
            return;

        IsCheckingDependencies = true;
        DependencyStatus = string.Empty;
        DependencyIssues = new List<DependencyIssue>();
        try
        {
            var result = await _modService.CheckDependencyCompatibilityAsync(SelectedInstallation);
            DependencyIssues = result.Issues;
            DependencyStatus = result.Summary;
        }
        catch (Exception ex)
        {
            DependencyStatus = $"Dependency check failed: {ex.Message}";
        }
        finally
        {
            IsCheckingDependencies = false;
        }
    }

    /// <summary>Applies the fix for a single dependency issue (installs the missing
    /// dependency, or swaps an incompatible one to a compatible build), then re-runs the
    /// check so the list reflects reality.</summary>
    public async Task FixDependencyIssueAsync(DependencyIssue issue)
    {
        if (SelectedInstallation == null)
            return;

        try
        {
            var fixedOk = await _modService.FixDependencyIssueAsync(SelectedInstallation, issue);
            // Refresh the installed-mods list first (LoadInstalledModsAsync clears the
            // dependency-check results since they'd otherwise apply to a stale mod list),
            // then re-run the dependency check last so its results are what's left showing.
            await LoadInstalledModsAsync();
            await CheckDependenciesAsync();
            if (!fixedOk)
                DependencyStatus = $"Could not fix {issue.DependencyName} automatically — no compatible build (release or beta) exists for Minecraft {SelectedInstallation.MinecraftVersion} on {SelectedInstallation.Loader}. " + DependencyStatus;
        }
        catch (Exception ex)
        {
            DependencyStatus = $"Fix failed: {ex.Message}";
        }
    }

    public async Task UpdateModAsync(Mod mod)
    {
        if (SelectedInstallation == null || IsInstalling)
            return;

        IsInstalling = true;
        InstallComplete = false;
        IsInstallInProgress = true;
        InstallOverlayTitle = $"Updating {mod.Name}";
        InstallOverlayMessage = mod.LatestVersion != null
            ? $"Downloading version {mod.LatestVersion}…"
            : "Downloading the latest version…";
        IsInstallOverlayVisible = true;

        try
        {
            var updated = await _modService.UpdateModAsync(SelectedInstallation, mod.Id);
            InstallOverlayMessage = updated
                ? $"{mod.Name} was updated successfully."
                : $"{mod.Name} is already up to date.";
            await LoadInstalledModsAsync();
        }
        catch (Exception ex)
        {
            InstallOverlayMessage = $"Update failed: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"Update error: {ex}");
        }
        finally
        {
            IsInstalling = false;
            IsInstallInProgress = false;
            InstallComplete = true;
        }
    }

    /// <summary>
    /// Flags each search result that is already installed in the selected installation and
    /// re-publishes the list so the "Install"/"Installed" button state refreshes.
    /// Checks both Modrinth IDs and CurseForge IDs.
    /// </summary>
    private void MarkInstalledStatus()
    {
        if (SearchResults.Count == 0)
            return;

        var installedModrinthIds = new HashSet<string>(
            (InstalledMods ?? new List<Mod>())
                .Where(m => !string.IsNullOrEmpty(m.ModrinthId))
                .Select(m => m.ModrinthId!),
            StringComparer.OrdinalIgnoreCase);

        var installedCfIds = new HashSet<int>(
            (InstalledMods ?? new List<Mod>())
                .Where(m => m.CurseForgeId.HasValue)
                .Select(m => m.CurseForgeId!.Value));

        foreach (var result in SearchResults)
        {
            if (result.Source == ModSource.CurseForge && result.CurseForgeId.HasValue)
                result.IsInstalled = installedCfIds.Contains(result.CurseForgeId.Value);
            else
                result.IsInstalled = installedModrinthIds.Contains(result.ModrinthId);
        }

        // Reassign a fresh list so the ItemsControl rebuilds with the new flags.
        SearchResults = new List<ModSearchResult>(SearchResults);
    }

    private bool _isInstalling;
    private bool _isInstallOverlayVisible;
    private bool _isInstallInProgress;
    private bool _installComplete;
    private string _installOverlayTitle = string.Empty;
    private string _installOverlayMessage = string.Empty;

    public bool IsInstalling
    {
        get => _isInstalling;
        set => SetProperty(ref _isInstalling, value);
    }

    /// <summary>Controls the centered install progress popup.</summary>
    public bool IsInstallOverlayVisible
    {
        get => _isInstallOverlayVisible;
        set => SetProperty(ref _isInstallOverlayVisible, value);
    }

    /// <summary>True while the download/install is running (drives the indeterminate progress bar).</summary>
    public bool IsInstallInProgress
    {
        get => _isInstallInProgress;
        set => SetProperty(ref _isInstallInProgress, value);
    }

    /// <summary>True once the install has finished, so the Finish button is shown.</summary>
    public bool InstallComplete
    {
        get => _installComplete;
        set => SetProperty(ref _installComplete, value);
    }

    public string InstallOverlayTitle
    {
        get => _installOverlayTitle;
        set => SetProperty(ref _installOverlayTitle, value);
    }

    public string InstallOverlayMessage
    {
        get => _installOverlayMessage;
        set => SetProperty(ref _installOverlayMessage, value);
    }

    public async Task InstallModAsync(ModSearchResult searchResult)
    {
        if (SelectedInstallation == null || IsInstalling || searchResult.IsInstalled)
            return;

        IsInstalling = true;
        InstallComplete = false;
        IsInstallInProgress = true;
        InstallOverlayTitle = $"Installing {searchResult.Name}";
        InstallOverlayMessage = "Downloading files and resolving dependencies…";
        IsInstallOverlayVisible = true;

        try
        {
            var result = await _modService.InstallModFromSearchAsync(SelectedInstallation, searchResult);
            InstallOverlayMessage = result.Success
                ? result.Summary
                : (result.Error ?? "Installation failed.");

            if (result.Success)
                await LoadInstalledModsAsync();
        }
        catch (Exception ex)
        {
            InstallOverlayMessage = $"Installation failed: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"Install error: {ex}");
        }
        finally
        {
            IsInstalling = false;
            IsInstallInProgress = false;
            InstallComplete = true;
        }
    }

    /// <summary>Dismisses the install popup once the user presses Finish.</summary>
    public void FinishInstall()
    {
        IsInstallOverlayVisible = false;
        InstallComplete = false;
        InstallOverlayMessage = string.Empty;
    }

    /// <summary>Pre-selects an installation by ID (used when opening the mods browser from Settings).</summary>
    public async Task PreSelectInstallationAsync(Guid installationId)
    {
        var all = await _installationService.GetAllInstallationsAsync();
        Installations = all;
        SelectedInstallation = all.FirstOrDefault(i => i.Id == installationId) ?? all.FirstOrDefault();
    }

    public async Task UninstallModAsync(Mod mod)
    {
        if (SelectedInstallation == null)
            return;

        await _modService.UninstallModAsync(SelectedInstallation.Id, mod.Id);
        await LoadInstalledModsAsync();
    }
    // ─── Modpack inline search ────────────────────────────────────────────────

    private List<ContentSearchResult> _modpackResults = new();
    private bool _isModpackSearching;
    private string _modpackSearchQuery = string.Empty;
    private int _modpackTotalHits;
    private bool _showModpackPrompt = true;
    private SortOption? _selectedModpackSort;
    private List<ModpackCardViewModel> _modpackCards = new();

    // ── Modpack paging ───────────────────────────────────────────────────────
    private int _modpackCurrentPage = 1;
    private int _modpackTotalPages;
    private const int ModpackPageSize = 20;

    public int ModpackCurrentPage
    {
        get => _modpackCurrentPage;
        set
        {
            if (SetProperty(ref _modpackCurrentPage, value))
            {
                OnPropertyChanged(nameof(ModpackCanGoPrevious));
                OnPropertyChanged(nameof(ModpackCanGoNext));
                OnPropertyChanged(nameof(ModpackPageNumbers));
            }
        }
    }

    public int ModpackTotalPages
    {
        get => _modpackTotalPages;
        set
        {
            if (SetProperty(ref _modpackTotalPages, value))
            {
                OnPropertyChanged(nameof(ModpackHasPagination));
                OnPropertyChanged(nameof(ModpackCanGoPrevious));
                OnPropertyChanged(nameof(ModpackCanGoNext));
                OnPropertyChanged(nameof(ModpackPageNumbers));
            }
        }
    }

    public bool ModpackHasPagination => _modpackTotalPages > 1;
    public bool ModpackCanGoPrevious => _modpackCurrentPage > 1;
    public bool ModpackCanGoNext     => _modpackCurrentPage < _modpackTotalPages;

    public List<int> ModpackPageNumbers
    {
        get
        {
            if (_modpackTotalPages <= 0) return new List<int>();
            const int window = 5;
            var start = Math.Max(1, _modpackCurrentPage - window / 2);
            var end   = Math.Min(_modpackTotalPages, start + window - 1);
            if (end - start < window - 1) start = Math.Max(1, end - window + 1);
            var numbers = new List<int>();
            for (int i = start; i <= end; i++) numbers.Add(i);
            return numbers;
        }
    }

    public async Task ModpackNextPageAsync()
    {
        if (!ModpackCanGoNext) return;
        ModpackCurrentPage++;
        await SearchModpacksAsync(resetPage: false);
    }

    public async Task ModpackPreviousPageAsync()
    {
        if (!ModpackCanGoPrevious) return;
        ModpackCurrentPage--;
        await SearchModpacksAsync(resetPage: false);
    }

    public async Task ModpackGoToPageAsync(int page)
    {
        if (page < 1 || page > _modpackTotalPages || page == _modpackCurrentPage) return;
        ModpackCurrentPage = page;
        await SearchModpacksAsync(resetPage: false);
    }

    public List<SortOption> ModpackSortOptions { get; } = new()
    {
        new SortOption("Downloads",        "downloads"),
        new SortOption("Relevance",        "relevance"),
        new SortOption("Recently updated", "updated"),
        new SortOption("Newest",           "newest")
    };

    public SortOption? SelectedModpackSort
    {
        get => _selectedModpackSort;
        set
        {
            if (SetProperty(ref _selectedModpackSort, value))
                _ = SearchModpacksAsync();
        }
    }

    public List<ContentSearchResult> ModpackResults
    {
        get => _modpackResults;
        set => SetProperty(ref _modpackResults, value);
    }

    /// <summary>Wrapped view-model cards for the modpacks tab — one per search result,
    /// supports expand/collapse with per-mod enable/disable rows.</summary>
    public List<ModpackCardViewModel> ModpackCards
    {
        get => _modpackCards;
        private set => SetProperty(ref _modpackCards, value);
    }

    public bool IsModpackSearching
    {
        get => _isModpackSearching;
        set => SetProperty(ref _isModpackSearching, value);
    }

    public string ModpackSearchQuery
    {
        get => _modpackSearchQuery;
        set => SetProperty(ref _modpackSearchQuery, value);
    }

    public int ModpackTotalHits
    {
        get => _modpackTotalHits;
        set
        {
            if (SetProperty(ref _modpackTotalHits, value))
                OnPropertyChanged(nameof(HasModpackResults));
        }
    }

    public bool HasModpackResults => _modpackTotalHits > 0;

    public bool ShowModpackEmptyState => !_isModpackSearching && !_showModpackPrompt && (_modpackResults == null || _modpackResults.Count == 0);

    public bool ShowModpackPrompt => _showModpackPrompt;

    public async Task SearchModpacksAsync(bool resetPage = true)
    {
        if (SelectedInstallation == null) return;

        if (resetPage) ModpackCurrentPage = 1;

        _showModpackPrompt = false;
        OnPropertyChanged(nameof(ShowModpackPrompt));

        IsModpackSearching = true;
        ModpackResults = new List<ContentSearchResult>();
        ModpackTotalHits = 0;

        try
        {
            var offset = (ModpackCurrentPage - 1) * ModpackPageSize;
            var page = await _contentService.SearchContentAsync(
                ContentType.Modpack,
                _modpackSearchQuery,
                SelectedInstallation.MinecraftVersion,
                offset, ModpackPageSize,
                _selectedModpackSort?.Index ?? "downloads");

            ModpackResults = page.Hits ?? new List<ContentSearchResult>();
            ModpackTotalHits = page.TotalHits;
            ModpackTotalPages = page.TotalHits > 0
                ? (int)Math.Ceiling(page.TotalHits / (double)ModpackPageSize)
                : 0;

            if (SelectedInstallation != null)
                ModpackCards = ModpackResults
                    .Select(r => new ModpackCardViewModel(r, SelectedInstallation, _modService, _modrinthClient))
                    .ToList();
        }
        catch
        {
            ModpackResults = new List<ContentSearchResult>();
            ModpackCards = new List<ModpackCardViewModel>();
            ModpackTotalPages = 0;
        }
        finally
        {
            IsModpackSearching = false;
            OnPropertyChanged(nameof(ShowModpackEmptyState));
        }
    }

    public async Task InstallModpackAsync(ContentSearchResult modpack)
    {
        if (SelectedInstallation == null || IsInstalling) return;

        IsInstalling = true;
        InstallComplete = false;
        IsInstallInProgress = true;
        InstallOverlayTitle = $"Installing {modpack.Name}";
        InstallOverlayMessage = "Downloading modpack files…";
        IsInstallOverlayVisible = true;

        try
        {
            // Convert ContentSearchResult → ModSearchResult for install service
            var modResult = new ModSearchResult
            {
                ModrinthId   = modpack.ModrinthId,
                Name         = modpack.Name,
                Description  = modpack.Description,
                Author       = modpack.Author,
                IconUrl      = modpack.IconUrl,
                Downloads    = modpack.Downloads,
                DateModified = modpack.DateModified,
                Categories   = modpack.Categories
            };
            var result = await _modService.InstallModFromSearchAsync(SelectedInstallation, modResult);
            InstallOverlayMessage = result.Success ? result.Summary : (result.Error ?? "Installation failed.");
            if (result.Success) await LoadInstalledModsAsync();
        }
        catch (Exception ex)
        {
            InstallOverlayMessage = $"Installation failed: {ex.Message}";
        }
        finally
        {
            IsInstalling = false;
            IsInstallInProgress = false;
            InstallComplete = true;
        }
    }

}
