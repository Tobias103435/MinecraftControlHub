using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using MinecraftControlHub.Core.Models;
using MinecraftControlHub.Core.Services;

namespace MinecraftControlHub.UI.ViewModels;

public class ServerSettingsViewModel : ViewModelBase
{
    private readonly IServerService _serverService;
    private readonly IModService _modService;
    private readonly IAppLogService _log;

    public Server Server { get; }

    public ServerSettingsViewModel(Server server, IServerService serverService, IModService modService, IAppLogService log)
    {
        Server = server;
        _serverService = serverService;
        _modService = modService;
        _log = log;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Navigation
    // ─────────────────────────────────────────────────────────────────────────

    private string _detailSection = "General";
    public string DetailSection
    {
        get => _detailSection;
        set
        {
            if (SetProperty(ref _detailSection, value))
            {
                OnPropertyChanged(nameof(IsGeneralSection));
                OnPropertyChanged(nameof(IsModsSection));
                OnPropertyChanged(nameof(IsAdvancedSection));
            }
        }
    }

    public bool IsGeneralSection  => DetailSection == "General";
    public bool IsModsSection     => DetailSection == "Mods";
    public bool IsAdvancedSection => DetailSection == "Advanced";

    // ─────────────────────────────────────────────────────────────────────────
    //  Mods list (mirrors HomePageViewModel pattern)
    // ─────────────────────────────────────────────────────────────────────────

    private List<InstalledModRowViewModel> _modRows = new();
    public List<InstalledModRowViewModel> ModRows
    {
        get => _modRows;
        private set
        {
            SetProperty(ref _modRows, value);
            OnPropertyChanged(nameof(HasMods));
        }
    }
    public bool HasMods => ModRows.Count > 0;

    private bool _isCheckingModUpdates;
    public bool IsCheckingModUpdates
    {
        get => _isCheckingModUpdates;
        private set => SetProperty(ref _isCheckingModUpdates, value);
    }

    private bool _isCheckingDependencies;
    public bool IsCheckingDependencies
    {
        get => _isCheckingDependencies;
        private set => SetProperty(ref _isCheckingDependencies, value);
    }

    private string _updateStatus = string.Empty;
    public string UpdateStatus
    {
        get => _updateStatus;
        private set
        {
            SetProperty(ref _updateStatus, value);
            OnPropertyChanged(nameof(HasUpdateStatus));
        }
    }
    public bool HasUpdateStatus => !string.IsNullOrEmpty(UpdateStatus);

    private string _dependencyStatus = string.Empty;
    public string DependencyStatus
    {
        get => _dependencyStatus;
        private set
        {
            SetProperty(ref _dependencyStatus, value);
            OnPropertyChanged(nameof(HasDependencyStatus));
        }
    }
    public bool HasDependencyStatus => !string.IsNullOrEmpty(DependencyStatus);

    private List<DependencyIssue> _dependencyIssues = new();
    public List<DependencyIssue> DependencyIssues
    {
        get => _dependencyIssues;
        private set
        {
            SetProperty(ref _dependencyIssues, value);
            OnPropertyChanged(nameof(HasDependencyIssues));
        }
    }
    public bool HasDependencyIssues => _dependencyIssues.Count > 0;

    // ─────────────────────────────────────────────────────────────────────────
    //  Load mods
    // ─────────────────────────────────────────────────────────────────────────

    public async Task LoadModsAsync()
    {
        var installation = BuildInstallation();
        if (installation == null)
        {
            ModRows = new List<InstalledModRowViewModel>();
            return;
        }

        try
        {
            var mods = await _modService.GetInstalledModsAsync(installation.Id);
            ModRows = mods
                .Select(m => new InstalledModRowViewModel(m, installation, _modService))
                .ToList();
        }
        catch
        {
            ModRows = new List<InstalledModRowViewModel>();
        }

        UpdateStatus    = string.Empty;
        DependencyStatus = string.Empty;
        DependencyIssues = new List<DependencyIssue>();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Check for updates
    // ─────────────────────────────────────────────────────────────────────────

    public async Task CheckModUpdatesAsync()
    {
        var installation = BuildInstallation();
        if (installation == null || IsCheckingModUpdates) return;

        IsCheckingModUpdates = true;
        UpdateStatus = "Checking for updates…";
        try
        {
            await _modService.RefreshUpdateStatusAsync(installation);
            await LoadModsAsync();

            var withUpdate = ModRows.Count(r => r.UpdateAvailable);
            UpdateStatus = withUpdate == 0
                ? $"All {ModRows.Count} mod(s) are up to date."
                : $"{withUpdate} of {ModRows.Count} mod(s) have an update available.";
        }
        catch (Exception ex)
        {
            UpdateStatus = $"Could not check for updates: {ex.Message}";
        }
        finally
        {
            IsCheckingModUpdates = false;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Check dependencies
    // ─────────────────────────────────────────────────────────────────────────

    public async Task CheckDependenciesAsync()
    {
        var installation = BuildInstallation();
        if (installation == null || IsCheckingDependencies) return;

        IsCheckingDependencies = true;
        DependencyStatus = "Checking dependencies…";
        try
        {
            var result = await _modService.CheckDependencyCompatibilityAsync(installation);
            DependencyStatus = result.Summary;
            DependencyIssues = result.Issues;
        }
        catch (Exception ex)
        {
            DependencyStatus = $"Dependency check failed: {ex.Message}";
            DependencyIssues = new List<DependencyIssue>();
        }
        finally
        {
            IsCheckingDependencies = false;
        }
    }

    public async Task FixDependencyIssueAsync(DependencyIssue issue)
    {
        var installation = BuildInstallation();
        if (installation == null) return;
        await _modService.FixDependencyIssueAsync(installation, issue);
        await CheckDependenciesAsync();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Mod actions
    // ─────────────────────────────────────────────────────────────────────────

    public async Task UninstallModAsync(Mod mod)
    {
        var installation = BuildInstallation();
        if (installation == null) return;
        await _modService.UninstallModAsync(installation.Id, mod.Id);
        await LoadModsAsync();
    }

    public async Task UpdateModRowAsync(InstalledModRowViewModel row)
    {
        var installation = BuildInstallation();
        if (installation == null) return;
        try
        {
            var updated = await _modService.UpdateModAsync(installation, row.Mod.Id);
            UpdateStatus = updated ? $"Updated {row.Name}." : $"{row.Name} is already up to date.";
            await LoadModsAsync();
        }
        catch (Exception ex)
        {
            UpdateStatus = $"Could not update {row.Name}: {ex.Message}";
        }
    }

    public void OpenModsFolder()
    {
        var folder = GetServerPluginFolder();
        if (string.IsNullOrWhiteSpace(folder)) return;
        Directory.CreateDirectory(folder);
        try { Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true }); }
        catch { try { Process.Start("explorer.exe", folder); } catch { } }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Save
    // ─────────────────────────────────────────────────────────────────────────

    public async Task SaveAsync()
    {
        try { await _serverService.UpdateServerAsync(Server); }
        catch (Exception ex) { _log.Log("ServerSettings.Save", $"Save failed: {ex.Message}"); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────────────────

    public Installation? BuildInstallation()
    {
        if (string.IsNullOrWhiteSpace(Server.ServerDirectory) ||
            string.IsNullOrWhiteSpace(Server.MinecraftVersion))
            return null;

        var loader = Server.Type switch
        {
            ServerType.Fabric   => LoaderType.Fabric,
            ServerType.Quilt    => LoaderType.Quilt,
            ServerType.Forge    => LoaderType.Forge,
            ServerType.NeoForge => LoaderType.NeoForge,
            _                   => LoaderType.Vanilla
        };

        return new Installation
        {
            Id               = Server.Id,
            Name             = Server.Name,
            MinecraftVersion = Server.MinecraftVersion,
            Loader           = loader,
            GameDirectory    = Server.ServerDirectory
        };
    }

    public string GetServerPluginFolder()
    {
        if (string.IsNullOrWhiteSpace(Server.ServerDirectory)) return string.Empty;
        return Server.Type switch
        {
            ServerType.Paper or ServerType.Purpur => Path.Combine(Server.ServerDirectory, "plugins"),
            _ => Path.Combine(Server.ServerDirectory, "mods")
        };
    }
}
