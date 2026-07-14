using MinecraftControlHub.Core.Models;
using MinecraftControlHub.Core.Services;

namespace MinecraftControlHub.UI.ViewModels;

/// <summary>
/// Wraps a single installed <see cref="Mod"/> for the Mods tab of the installation
/// Settings overlay. <see cref="Mod"/> itself is a plain data record with no change
/// notification, and the version-picker dropdown needs its own mutable, bindable
/// state (the lazily-loaded list of selectable versions, which one is selected,
/// whether it's currently loading/applying) — this row is that extra state.
/// </summary>
public class InstalledModRowViewModel : ViewModelBase
{
    private readonly IModService _modService;
    private readonly Installation _installation;

    private bool _versionsLoaded;
    private bool _isLoadingVersions;
    private bool _isChangingVersion;
    private List<ModVersion> _availableVersions = new();
    private ModVersion? _selectedVersion;
    private string _statusText = string.Empty;

    public InstalledModRowViewModel(Mod mod, Installation installation, IModService modService)
    {
        Mod = mod;
        _installation = installation;
        _modService = modService;
        _isEnabled = mod.IsEnabled;
    }

    public Mod Mod { get; }

    public string Name => Mod.Name;
    public string InstalledVersionLabel => string.IsNullOrEmpty(Mod.Version) ? "unknown version" : Mod.Version;
    public string LoaderLabel => Mod.Loader.ToString();
    public bool UpdateAvailable => Mod.UpdateAvailable;
    public string UpdateLabel => Mod.UpdateLabel;

    public List<ModVersion> AvailableVersions
    {
        get => _availableVersions;
        private set => SetProperty(ref _availableVersions, value);
    }

    /// <summary>The version currently shown as selected in the dropdown. Setting this
    /// to something other than the currently-installed version triggers the swap.</summary>
    public ModVersion? SelectedVersion
    {
        get => _selectedVersion;
        set
        {
            var previous = _selectedVersion;
            if (!SetProperty(ref _selectedVersion, value))
                return;

            if (value != null && previous != null && !string.Equals(value.Id, previous.Id, StringComparison.OrdinalIgnoreCase))
                _ = ApplyVersionChangeAsync(value);
        }
    }

    public bool IsLoadingVersions
    {
        get => _isLoadingVersions;
        private set => SetProperty(ref _isLoadingVersions, value);
    }

    public bool IsBusy => IsLoadingVersions || _isChangingVersion;

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (SetProperty(ref _statusText, value))
                OnPropertyChanged(nameof(HasStatusText));
        }
    }

    public bool HasStatusText => !string.IsNullOrEmpty(StatusText);

    /// <summary>Loads the mod's compatible versions from Modrinth the first time the
    /// dropdown is opened (no point fetching versions for every mod up front).</summary>
    public async Task EnsureVersionsLoadedAsync()
    {
        if (_versionsLoaded || IsLoadingVersions || string.IsNullOrEmpty(Mod.ModrinthId))
            return;

        IsLoadingVersions = true;
        OnPropertyChanged(nameof(IsBusy));
        try
        {
            // Fetch every build for this loader (unfiltered by Minecraft version) so we
            // can still locate whatever is currently installed even if it's off-branch.
            var allVersions = await _modService.GetModVersionsAsync(Mod.ModrinthId!, null, _installation.Loader);

            // Pre-select whichever entry matches what's actually installed, so the
            // dropdown opens on the current version instead of defaulting to the top.
            var current = allVersions.FirstOrDefault(v =>
                    !string.IsNullOrEmpty(Mod.Sha1Hash) && string.Equals(v.Sha1Hash, Mod.Sha1Hash, StringComparison.OrdinalIgnoreCase))
                ?? allVersions.FirstOrDefault(v =>
                    !string.IsNullOrEmpty(Mod.Version) && string.Equals(v.VersionNumber, Mod.Version, StringComparison.OrdinalIgnoreCase));

            // Only OFFER versions that EXACTLY declare support for this installation's
            // Minecraft version. Showing every version ever published for every Minecraft
            // release is exactly how someone ends up hand-picking a build meant for a
            // different Minecraft version and crashing on launch — and "close enough"
            // branch matching isn't safe either (a 1.21.1-only build is not guaranteed
            // to work on 1.21, and testing has shown it sometimes flat out doesn't).
            var compatible = allVersions
                .Where(v => v.GameVersions.Contains(_installation.MinecraftVersion, StringComparer.OrdinalIgnoreCase))
                .OrderByDescending(v => v.DatePublished)
                .ToList();

            // If what's currently installed isn't itself in the compatible list (e.g. it
            // was already switched to an off-branch build before), keep it visible so the
            // dropdown doesn't silently vanish the selection — it's just clearly labeled.
            if (current != null && !compatible.Any(v => string.Equals(v.Id, current.Id, StringComparison.OrdinalIgnoreCase)))
                compatible.Insert(0, current);

            AvailableVersions = compatible;

            _versionsLoaded = true;
            if (current != null)
                SetProperty(ref _selectedVersion, current, nameof(SelectedVersion));
        }
        catch (Exception ex)
        {
            StatusText = $"Could not load versions: {ex.Message}";
        }
        finally
        {
            IsLoadingVersions = false;
            OnPropertyChanged(nameof(IsBusy));
        }
    }

    private bool _isEnabled;
    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public async Task ToggleEnabledAsync()
    {
        var ok = await _modService.ToggleModEnabledAsync(_installation, Mod.Id);
        if (ok) IsEnabled = !IsEnabled;
    }

    /// <summary>Applies a version change directly and awaits completion.
    /// Used by <see cref="ModVersionPickerWindow"/> so the dialog only closes
    /// after the swap has actually finished (avoids fire-and-forget races).</summary>
    public Task ApplyVersionChangeDirectAsync(ModVersion targetVersion)
        => ApplyVersionChangeAsync(targetVersion);

    private async Task ApplyVersionChangeAsync(ModVersion targetVersion)
    {
        _isChangingVersion = true;
        OnPropertyChanged(nameof(IsBusy));
        StatusText = $"Switching to {targetVersion.VersionNumber}…";
        try
        {
            var ok = await _modService.ChangeModVersionAsync(_installation, Mod.Id, targetVersion);
            StatusText = ok
                ? $"Switched to {targetVersion.VersionNumber}."
                : "Could not switch version.";
        }
        catch (Exception ex)
        {
            StatusText = $"Could not switch version: {ex.Message}";
        }
        finally
        {
            _isChangingVersion = false;
            OnPropertyChanged(nameof(IsBusy));
        }
    }
}
