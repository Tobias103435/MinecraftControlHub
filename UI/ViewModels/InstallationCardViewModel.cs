using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MinecraftControlHub.Core.Models;
using MinecraftControlHub.Core.Services;

namespace MinecraftControlHub.UI.ViewModels;

/// <summary>
/// Per-mod row inside the expanded installation card. Handles the enable/disable toggle.
/// </summary>
public class ModRowViewModel : ViewModelBase
{
    private readonly IModService _modService;
    private readonly Installation _installation;
    private bool _isEnabled;
    private bool _isToggling;

    public Mod Mod { get; }
    public string Name    => Mod.Name;
    public string Version => Mod.Version ?? string.Empty;
    public char   Initial => string.IsNullOrEmpty(Mod.Name) ? '?' : char.ToUpper(Mod.Name[0]);

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public bool IsToggling
    {
        get => _isToggling;
        set => SetProperty(ref _isToggling, value);
    }

    public ModRowViewModel(Mod mod, Installation installation, IModService modService)
    {
        Mod           = mod;
        _installation = installation;
        _modService   = modService;
        _isEnabled    = mod.IsEnabled;
    }

    public async Task ToggleAsync()
    {
        if (IsToggling) return;
        IsToggling = true;
        try
        {
            var ok = await _modService.ToggleModEnabledAsync(_installation, Mod.Id);
            if (ok) IsEnabled = !IsEnabled;
        }
        finally
        {
            IsToggling = false;
        }
    }
}

/// <summary>
/// Wraps a single Installation for the home-page card.
/// Supports expand/collapse with lazy mod loading and per-mod enable/disable.
/// </summary>
public class InstallationCardViewModel : ViewModelBase
{
    private readonly IModService  _modService;
    private readonly IJavaService _javaService;

    private bool _isExpanded;
    private bool _isLoadingMods;
    private bool _modsLoaded;
    private List<ModRowViewModel> _modRows = new();

    // ── health ───────────────────────────────────────────────────────────
    private bool   _healthChecked;
    private bool   _javaOk        = true;
    private string _javaHealth    = string.Empty;
    private bool   _modsUpToDate  = true;
    private string _modsHealth    = string.Empty;
    private int    _updatesAvailable;

    public bool   JavaOk           { get => _javaOk;         private set => SetProperty(ref _javaOk,         value); }
    public string JavaHealth       { get => _javaHealth;     private set => SetProperty(ref _javaHealth,     value); }
    public bool   ModsUpToDate     { get => _modsUpToDate;   private set => SetProperty(ref _modsUpToDate,   value); }
    public string ModsHealth       { get => _modsHealth;     private set => SetProperty(ref _modsHealth,     value); }
    public bool   HasHealthWarning => !JavaOk || !ModsUpToDate;
    public bool   HealthChecked    { get => _healthChecked;  private set => SetProperty(ref _healthChecked,  value); }

    public Installation Installation { get; }

    public Guid       Id               => Installation.Id;
    public string     Name             => Installation.Name;
    public string     MinecraftVersion => Installation.MinecraftVersion;
    public LoaderType Loader           => Installation.Loader;

    // ── mod rows ─────────────────────────────────────────────────────────
    public List<ModRowViewModel> ModRows
    {
        get => _modRows;
        private set
        {
            SetProperty(ref _modRows, value);
            OnPropertyChanged(nameof(ModCountLabel));
            OnPropertyChanged(nameof(HasMods));
        }
    }

    public string ModCountLabel => _modRows.Count switch
    {
        0 when _modsLoaded => "No mods",
        0                  => "mods",
        1                  => "1 mod",
        _                  => $"{_modRows.Count} mods"
    };

    public bool HasMods        => _modRows.Count > 0;
    public bool IsLoadingMods  { get => _isLoadingMods; private set => SetProperty(ref _isLoadingMods, value); }

    // ── expand / collapse ────────────────────────────────────────────────
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (!SetProperty(ref _isExpanded, value)) return;
            if (value && !_modsLoaded)
                _ = LoadModsAsync();
        }
    }

    public InstallationCardViewModel(Installation installation, IModService modService, IJavaService javaService)
    {
        Installation = installation;
        _modService  = modService;
        _javaService = javaService;

        // Fire-and-forget background health check so the card shows indicators without blocking UI
        _ = CheckHealthAsync();
    }

    public void ToggleExpand() => IsExpanded = !IsExpanded;

    public async Task CheckHealthAsync()
    {
        try
        {
            // Java check
            var rec = await _javaService.GetRecommendationAsync(Installation.MinecraftVersion);
            JavaOk     = rec.IsSatisfied;
            JavaHealth = rec.IsSatisfied
                ? $"Java {rec.RecommendedMajor} ✓"
                : $"Java {rec.RecommendedMajor} missing ⚠";

            // Mod updates check
            var mods = await _modService.GetInstalledModsAsync(Installation.Id);
            if (mods.Count == 0)
            {
                ModsUpToDate = true;
                ModsHealth   = string.Empty;
            }
            else
            {
                var result = await _modService.CheckForUpdatesAsync(
                    new[] { Installation }, applyUpdates: false);
                _updatesAvailable = result.UpdatesAvailable.Count;
                ModsUpToDate = _updatesAvailable == 0;
                ModsHealth   = _updatesAvailable == 0
                    ? "Mods up to date ✓"
                    : $"{_updatesAvailable} mod update{(_updatesAvailable == 1 ? "" : "s")} ⚠";
            }

            OnPropertyChanged(nameof(HasHealthWarning));
        }
        catch
        {
            // Health check failures are non-critical — leave defaults
        }
        finally
        {
            HealthChecked = true;
        }
    }

    private async Task LoadModsAsync()
    {
        IsLoadingMods = true;
        try
        {
            var mods = await _modService.GetInstalledModsAsync(Installation.Id);
            ModRows = mods.Select(m => new ModRowViewModel(m, Installation, _modService)).ToList();
            _modsLoaded = true;
        }
        catch
        {
            ModRows = new List<ModRowViewModel>();
        }
        finally
        {
            IsLoadingMods = false;
        }
    }

    public void InvalidateMods()
    {
        _modsLoaded = false;
        if (_isExpanded)
            _ = LoadModsAsync();
    }
}
