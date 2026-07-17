using MinecraftControlHub.Core.Models;
using MinecraftControlHub.Core.Services;

namespace MinecraftControlHub.UI.ViewModels;

/// <summary>
/// Wraps a <see cref="ContentSearchResult"/> (modpack) in the Mods-page Modpacks tab.
/// Supports expand/collapse with per-mod enable/disable rows — mirrors the pattern
/// used by <see cref="InstallationCardViewModel"/> on the home page.
///
/// When the modpack is installed, expanding shows installed mods with a toggle.
/// When it is NOT installed, expanding shows the mod-files from the Modrinth
/// version info (read-only — so you can preview what's inside before installing).
/// </summary>
public class ModpackCardViewModel : ViewModelBase
{
    private readonly IModService _modService;
    private readonly IModrinthApiClient _modrinthClient;
    private readonly Installation _installation;

    private bool _isExpanded;
    private bool _isLoadingMods;
    private bool _modsLoaded;
    private List<ModRowViewModel> _modRows = new();
    private List<string> _previewModNames = new();

    // ── Data from the Modrinth search result ─────────────────────────────────
    public ContentSearchResult Modpack { get; }

    public string   Name        => Modpack.Name;
    public string?  Description => Modpack.Description;
    public string?  IconUrl     => Modpack.IconUrl;
    public string?  Author      => Modpack.Author;
    public int      Downloads   => Modpack.Downloads;
    public bool     IsInstalled => Modpack.IsInstalled;
    public string   InstallButtonLabel => IsInstalled ? "Installed" : "Install";

    // ── Expand / collapse ────────────────────────────────────────────────────
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (!SetProperty(ref _isExpanded, value)) return;
            OnPropertyChanged(nameof(ExpandChevron));
            if (value && !_modsLoaded)
                _ = LoadModsAsync();
        }
    }

    /// <summary>Chevron glyph reflecting the expand/collapse state.</summary>
    public string ExpandChevron => _isExpanded ? "▼" : "▶";

    public bool IsLoadingMods
    {
        get => _isLoadingMods;
        private set => SetProperty(ref _isLoadingMods, value);
    }

    // ── Installed mod rows (toggleable) ──────────────────────────────────────
    public List<ModRowViewModel> ModRows
    {
        get => _modRows;
        private set
        {
            SetProperty(ref _modRows, value);
            OnPropertyChanged(nameof(ModCountLabel));
            OnPropertyChanged(nameof(HasMods));
            OnPropertyChanged(nameof(HasInstalledRows));
            OnPropertyChanged(nameof(HasPreviewNames));
        }
    }

    // ── Preview mod names (read-only, for non-installed modpacks) ─────────────
    public List<string> PreviewModNames
    {
        get => _previewModNames;
        private set
        {
            SetProperty(ref _previewModNames, value);
            OnPropertyChanged(nameof(ModCountLabel));
            OnPropertyChanged(nameof(HasPreviewNames));
        }
    }

    public bool HasMods          => _modRows.Count > 0 || _previewModNames.Count > 0;
    public bool HasInstalledRows => _modRows.Count > 0;
    public bool HasPreviewNames  => _previewModNames.Count > 0 && _modRows.Count == 0;

    public string ModCountLabel
    {
        get
        {
            var count = IsInstalled ? _modRows.Count : _previewModNames.Count;
            return count switch
            {
                0 when _modsLoaded => "No mods found",
                0                  => string.Empty,
                1                  => "1 mod",
                _                  => $"{count} mods"
            };
        }
    }

    // ── Constructor ──────────────────────────────────────────────────────────
    public ModpackCardViewModel(ContentSearchResult modpack, Installation installation,
        IModService modService, IModrinthApiClient modrinthClient)
    {
        Modpack          = modpack;
        _installation    = installation;
        _modService      = modService;
        _modrinthClient  = modrinthClient;
    }

    public void ToggleExpand() => IsExpanded = !IsExpanded;

    // ── Private helpers ──────────────────────────────────────────────────────
    private async Task LoadModsAsync()
    {
        IsLoadingMods = true;
        try
        {
            if (IsInstalled)
            {
                // Show installed mods with toggle
                var mods = await _modService.GetInstalledModsAsync(_installation.Id);
                ModRows = mods.Select(m => new ModRowViewModel(m, _installation, _modService)).ToList();
            }
            else
            {
                // Show a preview: fetch the latest version of this modpack and list its files
                var versions = await _modrinthClient.GetModVersionsAsync(
                    Modpack.ModrinthId,
                    _installation.MinecraftVersion);

                var latest = versions.FirstOrDefault();
                if (latest != null)
                {
                    // Each file in the version is a mod in the pack — extract the name from the path/filename
                    // The ModVersion.Dependencies list contains the required projects
                    // but the cleanest preview is simply the dependency project names
                    // if available, otherwise the filename without extension.
                    var names = latest.Dependencies
                        .Where(d => d.Type == DependencyType.Required && !string.IsNullOrEmpty(d.ProjectId))
                        .Select(d => d.ProjectId!)
                        .ToList();

                    // If dependencies are empty (common for modpacks — they embed files rather than
                    // listing deps), fall back to showing the version number + file name
                    if (names.Count == 0 && !string.IsNullOrEmpty(latest.FileName))
                        names = new List<string> { latest.FileName };

                    PreviewModNames = names;
                }
                else
                {
                    PreviewModNames = new List<string>();
                }
            }
            _modsLoaded = true;
        }
        catch
        {
            ModRows = new List<ModRowViewModel>();
            PreviewModNames = new List<string>();
        }
        finally
        {
            IsLoadingMods = false;
        }
    }
}
