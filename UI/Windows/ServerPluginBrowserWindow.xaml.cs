using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using MinecraftControlHub.Core.Models;
using MinecraftControlHub.Core.Services;

namespace MinecraftControlHub.UI.Windows;

/// <summary>
/// Browse and install plugins, mods, and datapacks from Modrinth for a specific server.
///
/// Loader / directory mapping:
///   Paper / Purpur  → Modrinth loader "paper",   install dir: /plugins/
///   Fabric          → Modrinth loader "fabric",  install dir: /mods/
///   Forge           → Modrinth loader "forge",   install dir: /mods/
///   NeoForge        → Modrinth loader "neoforge",install dir: /mods/
///   Quilt           → Modrinth loader "quilt",   install dir: /mods/
///   Vanilla         → no loader filter,           install dir: /plugins/
/// Datapacks always go to /world/datapacks/.
/// </summary>
public partial class ServerPluginBrowserWindow : Window
{
    // ── Services ──────────────────────────────────────────────────────────────
    private readonly Server _server;
    private readonly IModrinthApiClient? _modrinthClient;
    private readonly IAppLogService? _log;

    // ── Search state ──────────────────────────────────────────────────────────
    private string _activeTab = "plugin";      // "plugin" | "mod" | "datapack"
    private string _activeSort = "downloads";
    private int _currentPage = 1;
    private int _totalHits;
    private const int PageSize = 20;
    private CancellationTokenSource? _searchCts;

    // Already-installed filenames (lower-cased) for quick duplicate detection
    private readonly HashSet<string> _installedFileNames =
        new(StringComparer.OrdinalIgnoreCase);

    // Sort options (display → Modrinth index name)
    private readonly List<(string Display, string Index)> _sortOptions = new()
    {
        ("Downloads",        "downloads"),
        ("Relevance",        "relevance"),
        ("Recently updated", "updated"),
        ("Newest",           "newest"),
        ("Popularity",       "follows"),
    };

    // ── Constructor ───────────────────────────────────────────────────────────
    public ServerPluginBrowserWindow(Server server)
    {
        InitializeComponent();

        _server = server;
        var sp = (Application.Current as App)?.ServiceProvider;
        _modrinthClient = sp?.GetService<IModrinthApiClient>();
        _log = sp?.GetService<IAppLogService>();

        // Window header
        WindowTitleTextBlock.Text    = $"Browse Plugins & Mods — {server.Name}";
        ServerSubtitleTextBlock.Text = $"MC {server.MinecraftVersion}  ·  {server.Type}  ·  Powered by Modrinth";
        VersionBadge.Text            = $"MC {server.MinecraftVersion}";

        // Populate sort ComboBox
        SortComboBox.ItemsSource   = _sortOptions.Select(s => s.Display).ToList();
        SortComboBox.SelectedIndex = 0;

        RefreshInstalledSet();
        ConfigureTabsForServerType();

        // Kick off initial search
        _ = SearchAsync();
    }

    /// <summary>
    /// Shows/hides and pre-selects the correct tab based on the server type so the user
    /// only ever sees content that can actually run on this server:
    ///   Paper / Purpur          → Plugins (default) + Datapacks  (no Mods tab)
    ///   Fabric / Forge / NeoForge / Quilt → Mods (default) + Datapacks  (no Plugins tab)
    ///   Vanilla                 → Datapacks only
    ///   Other / unknown         → show all tabs
    /// </summary>
    private void ConfigureTabsForServerType()
    {
        switch (_server.Type)
        {
            case ServerType.Paper:
            case ServerType.Purpur:
                // Plugins + Datapacks only
                TabPlugins.Visibility   = Visibility.Visible;
                TabMods.Visibility      = Visibility.Collapsed;
                TabDatapacks.Visibility = Visibility.Visible;
                _activeTab = "plugin";
                TabPlugins.Style = (Style)FindResource("ActiveTabButtonStyle");
                TabMods.Style    = (Style)FindResource("TabButtonStyle");
                break;

            case ServerType.Fabric:
            case ServerType.Forge:
            case ServerType.NeoForge:
            case ServerType.Quilt:
                // Mods + Datapacks only
                TabPlugins.Visibility   = Visibility.Collapsed;
                TabMods.Visibility      = Visibility.Visible;
                TabDatapacks.Visibility = Visibility.Visible;
                _activeTab = "mod";
                TabPlugins.Style = (Style)FindResource("TabButtonStyle");
                TabMods.Style    = (Style)FindResource("ActiveTabButtonStyle");
                TabDatapacks.Style = (Style)FindResource("TabButtonStyle");
                break;

            case ServerType.Vanilla:
                // Datapacks only
                TabPlugins.Visibility   = Visibility.Collapsed;
                TabMods.Visibility      = Visibility.Collapsed;
                TabDatapacks.Visibility = Visibility.Visible;
                _activeTab = "datapack";
                TabDatapacks.Style = (Style)FindResource("ActiveTabButtonStyle");
                break;

            default:
                // Show all tabs
                TabPlugins.Visibility   = Visibility.Visible;
                TabMods.Visibility      = Visibility.Visible;
                TabDatapacks.Visibility = Visibility.Visible;
                break;
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  TAB SWITCHING
    // ══════════════════════════════════════════════════════════════════════════

    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;

        _activeTab    = btn.Tag as string ?? "plugin";
        _currentPage  = 1;

        // Reset all tab styles, then activate the clicked one
        TabPlugins.Style   = (Style)FindResource("TabButtonStyle");
        TabMods.Style      = (Style)FindResource("TabButtonStyle");
        TabDatapacks.Style = (Style)FindResource("TabButtonStyle");

        switch (_activeTab)
        {
            case "plugin":   TabPlugins.Style   = (Style)FindResource("ActiveTabButtonStyle"); break;
            case "mod":      TabMods.Style       = (Style)FindResource("ActiveTabButtonStyle"); break;
            case "datapack": TabDatapacks.Style  = (Style)FindResource("ActiveTabButtonStyle"); break;
        }

        _ = SearchAsync();
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  SEARCH
    // ══════════════════════════════════════════════════════════════════════════

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        _currentPage = 1;
        _ = SearchAsync();
    }

    private void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        _currentPage = 1;
        _ = SearchAsync();
    }

    private void SortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var idx = SortComboBox.SelectedIndex;
        if (idx >= 0 && idx < _sortOptions.Count)
        {
            _activeSort  = _sortOptions[idx].Index;
            _currentPage = 1;
            _ = SearchAsync();
        }
    }

    private async Task SearchAsync()
    {
        if (_modrinthClient == null)
        {
            ShowEmpty("Modrinth service is not available.");
            return;
        }

        // Cancel any in-flight request
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        ShowLoading(true);

        try
        {
            var query  = SearchBox.Text?.Trim() ?? string.Empty;
            var offset = (_currentPage - 1) * PageSize;
            var loader = GetLoaderForCurrentTab();

            var page = await _modrinthClient.SearchServerContentAsync(
                query,
                _server.MinecraftVersion,
                _activeTab,
                loader,
                offset,
                PageSize
            );

            if (token.IsCancellationRequested) return;

            _totalHits = page.TotalHits;
            ShowLoading(false);
            UpdateResults(page.Hits ?? new List<ModSearchResult>());
            UpdatePagination();
        }
        catch (Exception ex) when (!token.IsCancellationRequested)
        {
            _log?.LogError("ServerPluginBrowser.Search", $"Search failed: {ex.Message}", ex);
            ShowLoading(false);
            ShowEmpty($"Search failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Returns the Modrinth category loader string appropriate for the active tab + server type.
    /// Returns null when no loader filter is needed (e.g. datapacks, or Vanilla + plugins).
    /// </summary>
    private string? GetLoaderForCurrentTab()
    {
        return _activeTab switch
        {
            "plugin" => _server.Type switch
            {
                ServerType.Paper   => "paper",
                ServerType.Purpur  => "purpur",
                // Vanilla / loader servers: no specific loader filter for plugin tab
                _                  => null
            },
            "mod" => _server.Type switch
            {
                ServerType.Fabric  => "fabric",
                ServerType.Forge   => "forge",
                ServerType.NeoForge => "neoforge",
                ServerType.Quilt   => "quilt",
                // Paper can also run certain mods via Fabric bridges — default to fabric
                ServerType.Paper   => "fabric",
                ServerType.Purpur  => "fabric",
                _                  => null
            },
            "datapack" => null,
            _          => null
        };
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  RESULTS
    // ══════════════════════════════════════════════════════════════════════════

    private void UpdateResults(List<ModSearchResult> results)
    {
        if (results.Count == 0)
        {
            ShowEmpty("No results found. Try a different search term or tab.");
            return;
        }

        EmptyState.Visibility          = Visibility.Collapsed;
        ResultsScrollViewer.Visibility = Visibility.Visible;

        // Bind fresh list to the ItemsControl
        ResultsItemsControl.ItemsSource = null;
        ResultsItemsControl.ItemsSource = results;

        StatusTextBlock.Text = $"Showing {results.Count} of {_totalHits:N0} results";
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  INSTALL
    // ══════════════════════════════════════════════════════════════════════════

    private async void InstallContent_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.Tag is not ModSearchResult result) return;

        if (string.IsNullOrWhiteSpace(_server.ServerDirectory))
        {
            MessageBox.Show(
                "Server directory is not set. Please configure the server first.",
                "Cannot install", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        btn.IsEnabled = false;
        btn.Content   = "Installing…";

        try
        {
            await InstallResultAsync(result);
            btn.Content = "✓ Installed";
            RefreshInstalledSet();

            // Try to auto-install dependencies
            try
            {
                await InstallDependenciesAsync(result);
            }
            catch (Exception depEx)
            {
                _log?.LogError("ServerPluginBrowser.InstallDependencies", $"Failed to install dependencies for {result.Name}: {depEx.Message}", depEx);
            }

            // Trigger Instance Sync if enabled
            try
            {
                var ownerWindow = Owner as ServerPreviewWindow;
                if (ownerWindow != null)
                    await ownerWindow.TriggerAutoSyncIfEnabledAsync();
            }
            catch { /* sync failure should never interrupt install */ }
        }
        catch (Exception ex)
        {
            _log?.LogError("ServerPluginBrowser.Install",
                $"Install failed for {result.Name}: {ex.Message}", ex);
            btn.Content   = "Failed";
            btn.IsEnabled = true;
            MessageBox.Show(
                $"Could not install {result.Name}:\n\n{ex.Message}",
                "Install failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task InstallResultAsync(ModSearchResult result)
    {
        if (_modrinthClient == null)
            throw new InvalidOperationException("Modrinth client not available.");

        // Determine install directory and make sure it exists
        var targetDir = GetInstallDirectory();
        Directory.CreateDirectory(targetDir);

        // Map server type → LoaderType for the Modrinth version query
        // Note: LoaderType enum only covers client-side loaders; for Paper servers
        //       we pass null (no loader filter) because Paper plugins show up without
        //       a loader facet in the version list.
        LoaderType? loaderFilter = _server.Type switch
        {
            ServerType.Fabric   => LoaderType.Fabric,
            ServerType.Forge    => LoaderType.Forge,
            ServerType.NeoForge => LoaderType.NeoForge,
            ServerType.Quilt    => LoaderType.Quilt,
            _                   => null       // Paper, Purpur, Vanilla — no loader filter
        };

        var versions = await _modrinthClient.GetModVersionsAsync(
            result.ModrinthId,
            _server.MinecraftVersion,
            loaderFilter
        );

        if (versions.Count == 0)
            throw new InvalidOperationException(
                $"No compatible version found for MC {_server.MinecraftVersion}. " +
                "Try installing a different version manually.");

        // Pick the newest compatible, preferring stable over beta/alpha
        var version = versions
            .OrderByDescending(v => v.DatePublished)
            .FirstOrDefault(v => v.VersionType == "release" || v.VersionType == null)
            ?? versions.First();

        if (string.IsNullOrWhiteSpace(version.DownloadUrl))
            throw new InvalidOperationException("No download URL found for this version.");

        var bytes    = await _modrinthClient.DownloadModAsync(version.DownloadUrl);
        var fileName = version.FileName ?? $"{result.ModrinthId}.jar";
        var destPath = Path.Combine(targetDir, fileName);

        await File.WriteAllBytesAsync(destPath, bytes);

        _log?.Log("ServerPluginBrowser.Install",
            $"Installed '{result.Name}' v{version.VersionNumber} ({fileName}) → {destPath}");
    }

    /// <summary>Returns the server subfolder where the content should be installed.</summary>
    private string GetInstallDirectory()
    {
        var serverDir = _server.ServerDirectory!;

        // Datapacks → world/datapacks
        if (_activeTab == "datapack")
            return Path.Combine(serverDir, "world", "datapacks");

        // Plugin servers (Paper / Purpur / Vanilla) → /plugins/
        if (_server.Type is ServerType.Paper or ServerType.Purpur or ServerType.Vanilla)
            return Path.Combine(serverDir, "plugins");

        // Loader servers (Fabric / Forge / NeoForge / Quilt) → /mods/
        return Path.Combine(serverDir, "mods");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  PAGINATION
    // ══════════════════════════════════════════════════════════════════════════

    private void PrevPage_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage <= 1) return;
        _currentPage--;
        _ = SearchAsync();
        ResultsScrollViewer.ScrollToTop();
    }

    private void NextPage_Click(object sender, RoutedEventArgs e)
    {
        var totalPages = (int)Math.Ceiling((double)_totalHits / PageSize);
        if (_currentPage >= totalPages) return;
        _currentPage++;
        _ = SearchAsync();
        ResultsScrollViewer.ScrollToTop();
    }

    private void UpdatePagination()
    {
        var totalPages = Math.Max(1, (int)Math.Ceiling((double)_totalHits / PageSize));
        PageLabel.Text             = $"Page {_currentPage} of {totalPages}";
        PrevPageButton.IsEnabled   = _currentPage > 1;
        NextPageButton.IsEnabled   = _currentPage < totalPages;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  HELPERS
    // ══════════════════════════════════════════════════════════════════════════

    private void ShowLoading(bool loading)
    {
        LoadingOverlay.Visibility      = loading ? Visibility.Visible  : Visibility.Collapsed;
        ResultsScrollViewer.Visibility = loading ? Visibility.Collapsed : Visibility.Visible;
        EmptyState.Visibility          = Visibility.Collapsed;
    }

    private void ShowEmpty(string message)
    {
        EmptyStateText.Text            = message;
        EmptyState.Visibility          = Visibility.Visible;
        ResultsScrollViewer.Visibility = Visibility.Collapsed;
        LoadingOverlay.Visibility      = Visibility.Collapsed;
        StatusTextBlock.Text           = string.Empty;
        PageLabel.Text                 = string.Empty;
        PrevPageButton.IsEnabled       = false;
        NextPageButton.IsEnabled       = false;
    }

    private void RefreshInstalledSet()
    {
        _installedFileNames.Clear();
        if (string.IsNullOrWhiteSpace(_server.ServerDirectory)) return;

        foreach (var subDir in new[] { "plugins", "mods" })
        {
            var full = Path.Combine(_server.ServerDirectory, subDir);
            if (!Directory.Exists(full)) continue;
            foreach (var f in Directory.GetFiles(full, "*.jar"))
                _installedFileNames.Add(Path.GetFileName(f));
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  DEPENDENCY MANAGEMENT
    // ══════════════════════════════════════════════════════════════════════════

    private async Task InstallDependenciesAsync(ModSearchResult result)
    {
        if (_modrinthClient == null) return;
        var processedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { result.ModrinthId };
        await InstallDependenciesRecursiveAsync(result.ModrinthId, processedIds);
    }

    private async Task InstallDependenciesRecursiveAsync(string modrinthId, HashSet<string> processedIds)
    {
        if (_modrinthClient == null) return;

        _log?.Log("ServerPluginBrowser.Dependencies", $"Checking dependencies for project '{modrinthId}'...");
        var detail = await _modrinthClient.GetModDetailAsync(modrinthId);
        if (detail == null)
        {
            _log?.Log("ServerPluginBrowser.Dependencies", $"Could not retrieve details for project '{modrinthId}'. Skipping.");
            return;
        }

        LoaderType? loaderFilter = _server.Type switch
        {
            ServerType.Fabric   => LoaderType.Fabric,
            ServerType.Forge    => LoaderType.Forge,
            ServerType.NeoForge => LoaderType.NeoForge,
            ServerType.Quilt    => LoaderType.Quilt,
            _                   => null
        };

        var versions = await _modrinthClient.GetModVersionsAsync(modrinthId, _server.MinecraftVersion, loaderFilter);
        if (versions == null || versions.Count == 0) return;

        var selectedVersion = versions
            .OrderByDescending(v => v.DatePublished)
            .FirstOrDefault(v => v.VersionType == "release" || v.VersionType == null)
            ?? versions.First();

        foreach (var dep in selectedVersion.Dependencies)
        {
            if (dep.Type != DependencyType.Required || string.IsNullOrWhiteSpace(dep.ProjectId))
                continue;

            if (processedIds.Contains(dep.ProjectId))
                continue;

            processedIds.Add(dep.ProjectId);

            // Fetch details for the dependency to know its name and details
            var depDetail = await _modrinthClient.GetModDetailAsync(dep.ProjectId);
            if (depDetail == null) continue;

            // Check if already installed
            bool alreadyInstalled = false;
            var depVersions = await _modrinthClient.GetModVersionsAsync(dep.ProjectId, _server.MinecraftVersion, loaderFilter);
            if (depVersions != null && depVersions.Count > 0)
            {
                var depSelectedVersion = depVersions
                    .OrderByDescending(v => v.DatePublished)
                    .FirstOrDefault(v => v.VersionType == "release" || v.VersionType == null)
                    ?? depVersions.First();

                var expectedFileName = depSelectedVersion.FileName ?? $"{dep.ProjectId}.jar";
                if (_installedFileNames.Contains(expectedFileName))
                {
                    alreadyInstalled = true;
                }
            }

            if (alreadyInstalled)
            {
                _log?.Log("ServerPluginBrowser.Dependencies", $"Dependency '{depDetail.Name}' is already installed. Skipping.");
                // Still traverse its dependencies
                await InstallDependenciesRecursiveAsync(dep.ProjectId, processedIds);
                continue;
            }

            _log?.Log("ServerPluginBrowser.Dependencies", $"Installing required dependency '{depDetail.Name}' ({dep.ProjectId})...");
            
            if (depVersions == null || depVersions.Count == 0)
            {
                _log?.Log("ServerPluginBrowser.Dependencies", $"No compatible version found for dependency '{depDetail.Name}' for MC {_server.MinecraftVersion}.");
                continue;
            }

            var depVersionToInstall = depVersions
                .OrderByDescending(v => v.DatePublished)
                .FirstOrDefault(v => v.VersionType == "release" || v.VersionType == null)
                ?? depVersions.First();

            if (string.IsNullOrWhiteSpace(depVersionToInstall.DownloadUrl))
                continue;

            var targetDir = GetInstallDirectory();
            Directory.CreateDirectory(targetDir);

            var bytes = await _modrinthClient.DownloadModAsync(depVersionToInstall.DownloadUrl);
            var fileName = depVersionToInstall.FileName ?? $"{dep.ProjectId}.jar";
            var destPath = Path.Combine(targetDir, fileName);

            await File.WriteAllBytesAsync(destPath, bytes);
            _installedFileNames.Add(fileName);

            _log?.Log("ServerPluginBrowser.Dependencies", $"Successfully installed dependency '{depDetail.Name}' v{depVersionToInstall.VersionNumber} ({fileName})");

            // Recurse
            await InstallDependenciesRecursiveAsync(dep.ProjectId, processedIds);
        }
    }

    private async void RecheckDependencies_Click(object sender, RoutedEventArgs e)
    {
        if (_modrinthClient == null) return;

        if (string.IsNullOrWhiteSpace(_server.ServerDirectory))
        {
            MessageBox.Show("Server directory is not set.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        StatusTextBlock.Text = "Re-checking dependencies...";
        RefreshInstalledSet();

        var targetDir = GetInstallDirectory();
        if (!Directory.Exists(targetDir))
        {
            StatusTextBlock.Text = "No installed files found.";
            return;
        }

        var files = Directory.GetFiles(targetDir, "*.jar");
        var projectIds = new List<string>();
        var installedCount = 0;

        foreach (var file in files)
        {
            try
            {
                var bytes = await File.ReadAllBytesAsync(file);
                var sha1Bytes = System.Security.Cryptography.SHA1.HashData(bytes);
                var hashHex = Convert.ToHexString(sha1Bytes).ToLowerInvariant();

                var version = await _modrinthClient.GetVersionByHashAsync(hashHex);
                if (version != null && !string.IsNullOrWhiteSpace(version.ModrinthId))
                {
                    projectIds.Add(version.ModrinthId);
                }
            }
            catch (Exception ex)
            {
                _log?.LogError("ServerPluginBrowser.Recheck", $"Failed checking hash for {Path.GetFileName(file)}: {ex.Message}", ex);
            }
        }

        if (projectIds.Count == 0)
        {
            StatusTextBlock.Text = "Could not identify any Modrinth projects from installed jars.";
            return;
        }

        var processedIds = new HashSet<string>(projectIds, StringComparer.OrdinalIgnoreCase);
        
        try
        {
            foreach (var projectId in projectIds)
            {
                // We run recursive dependency check/install for each identified mod.
                // We construct a fake ModSearchResult or just pass a custom method
                var beforeCount = _installedFileNames.Count;
                await InstallDependenciesRecursiveAsync(projectId, processedIds);
                var afterCount = _installedFileNames.Count;
                installedCount += (afterCount - beforeCount);
            }

            RefreshInstalledSet();
            StatusTextBlock.Text = $"Dependency check complete. Installed {installedCount} missing dependency/dependencies.";
        }
        catch (Exception ex)
        {
            _log?.LogError("ServerPluginBrowser.Recheck", $"Failed checking dependencies: {ex.Message}", ex);
            StatusTextBlock.Text = "Dependency check failed.";
        }
    }
}
