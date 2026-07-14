using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using MinecraftControlHub.Core.Models;
using MinecraftControlHub.Core.Services;
using MinecraftControlHub.UI.ViewModels;

namespace MinecraftControlHub.UI.Windows;

public partial class InstallationSettingsWindow : Window
{
    private readonly HomePageViewModel _viewModel;
    private readonly IContentService _contentService;
    private readonly IAppLogService _log;
    private readonly IInstallationService? _installationService;

    // Content tab view models (lazy-initialized the first time Content nav is opened)
    private ContentTabViewModel? _resourcePackTab;
    private ContentTabViewModel? _shaderPackTab;

    // Which content sub-panel is currently visible
    private string _contentSubSection = "Mods";
    private readonly List<ContentItem> _installedMaps = new();

    // Server Sync
    private readonly System.Collections.ObjectModel.ObservableCollection<ServerSyncRow> _serverSyncRows = new();
    private IServerService? _serverService;
    private IServerProvisioningService? _serverProvisioningService;
    private IModrinthApiClient? _modrinthClientForServerSync;
    private IModService? _modServiceForServerSync;

    public InstallationSettingsWindow(HomePageViewModel viewModel)
    {
        // Assign _viewModel and DataContext BEFORE InitializeComponent() so that
        // the RadioButton.Checked event fired during XAML parsing doesn't NPE.
        _viewModel  = viewModel;
        DataContext = viewModel;

        var sp = (Application.Current as App)?.ServiceProvider;
        _contentService = sp?.GetRequiredService<IContentService>()
            ?? throw new InvalidOperationException("IContentService not registered in DI.");
        _log = sp?.GetService<IAppLogService>() ?? new AppLogService();
        _serverService = sp?.GetService<IServerService>();
        _serverProvisioningService = sp?.GetService<IServerProvisioningService>();
        _modrinthClientForServerSync = sp?.GetService<IModrinthApiClient>();
        _modServiceForServerSync = sp?.GetService<IModService>();
        _installationService = sp?.GetService<IInstallationService>();

        InitializeComponent();
    }

    // ── Main left-nav ─────────────────────────────────────────────────────────

    private void DetailNav_Checked(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is RadioButton { Tag: string tag })
        {
            _viewModel.DetailSection = tag;
            if (tag == "Content")
                EnsureContentTabsInitialized();
            if (tag == "ServerSync")
                _ = LoadServerSyncAsync();
            if (tag == "Screenshots")
                _ = LoadScreenshotsAsync();
        }
    }

    // ── Mods tab ──────────────────────────────────────────────────────────────

    private async void CheckModUpdates_Click(object sender, RoutedEventArgs e)
        => await _viewModel.CheckModUpdatesAsync();

    private async void CheckDependencies_Click(object sender, RoutedEventArgs e)
        => await _viewModel.CheckDependenciesAsync();

    private void OpenModsFolder_Click(object sender, RoutedEventArgs e)
        => _viewModel.OpenModsFolder();

    private async void FixDependencyIssue_Click(object sender, RoutedEventArgs e)
    {
        try
        {

            if ((sender as Button)?.DataContext is DependencyIssue issue)
                await _viewModel.FixDependencyIssueAsync(issue);
    
        }
        catch (Exception __ex)
        {
            _log.LogError("FixDependencyIssue_Click", $"Unhandled in FixDependencyIssue_Click: {__ex.Message}\n{__ex.StackTrace}", __ex);
        }
    }

    private async void FixAllDependencyIssues_Click(object sender, RoutedEventArgs e)
    {
        try
        {

            var issues = _viewModel.DetailDependencyIssues.ToList();
            foreach (var issue in issues)
                await _viewModel.FixDependencyIssueAsync(issue);
    
        }
        catch (Exception __ex)
        {
            _log.LogError("FixAllDependencyIssues_Click", $"Unhandled in FixAllDependencyIssues_Click: {__ex.Message}\n{__ex.StackTrace}", __ex);
        }
    }

    private async void ModToggle_Click(object sender, RoutedEventArgs e)
    {
        try
        {

            if (sender is System.Windows.Controls.Primitives.ToggleButton tb
                && tb.DataContext is InstalledModRowViewModel rowVm)
            {
                await rowVm.ToggleEnabledAsync();
            }
    
        }
        catch (Exception __ex)
        {
            _log.LogError("ModToggle_Click", $"Unhandled in ModToggle_Click: {__ex.Message}\n{__ex.StackTrace}", __ex);
        }
    }

    private async void ChangeVersion_Click(object sender, RoutedEventArgs e)
    {
        try
        {

            if (sender is not System.Windows.Controls.Button btn) return;
            if (btn.DataContext is not InstalledModRowViewModel rowVm) return;

            if (string.IsNullOrWhiteSpace(rowVm.Mod?.ModrinthId))
            {
                System.Windows.MessageBox.Show(
                    "This mod was not installed from Modrinth — version history is unavailable.",
                    "Version picker",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
                return;
            }

            try
            {
                var picker = new ModVersionPickerWindow(rowVm) { Owner = this };
                picker.ShowDialog();
                await _viewModel.RefreshDetailModsPublicAsync();
            }
            catch (Exception ex)
            {
                _log.LogError("ChangeVersion", $"ChangeVersion_Click failed for mod={rowVm.Mod?.Name} — {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}", ex);
                System.Windows.MessageBox.Show(
                    $"Could not open version picker: {ex.Message}",
                    "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
    
        }
        catch (Exception __ex)
        {
            _log.LogError("ChangeVersion_Click", $"Unhandled in ChangeVersion_Click: {__ex.Message}\n{__ex.StackTrace}", __ex);
        }
    }

        private async void UpdateModRow_Click(object sender, RoutedEventArgs e)
    {
        try
        {

            if ((sender as Button)?.DataContext is InstalledModRowViewModel row)
                await _viewModel.UpdateModRowAsync(row);
    
        }
        catch (Exception __ex)
        {
            _log.LogError("UpdateModRow_Click", $"Unhandled in UpdateModRow_Click: {__ex.Message}\n{__ex.StackTrace}", __ex);
        }
    }

    
    private void ModLink_Click(object sender, MouseButtonEventArgs e)
    {
        var modrinthId = ((sender as FrameworkElement)?.DataContext as InstalledModRowViewModel)?.Mod?.ModrinthId;
        if (!string.IsNullOrWhiteSpace(modrinthId))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    $"https://modrinth.com/mod/{modrinthId}") { UseShellExecute = true });
            }
            catch { /* ignore */ }
        }
    }

    private async void UninstallDetailMod_Click(object sender, RoutedEventArgs e)
    {
        try
        {

            if ((sender as Button)?.DataContext is Mod mod)
                await _viewModel.UninstallDetailModAsync(mod);
    
        }
        catch (Exception __ex)
        {
            _log.LogError("UninstallDetailMod_Click", $"Unhandled in UninstallDetailMod_Click: {__ex.Message}\n{__ex.StackTrace}", __ex);
        }
    }

    /// <summary>Alias wired from XAML Click="UninstallMod_Click" (on the mods DataTemplate button).</summary>
    private async void UninstallMod_Click(object sender, RoutedEventArgs e)
    {
        try { await UninstallDetailModAsync(sender); }
        catch (Exception __ex) { _log.LogError("UninstallMod_Click", $"Unhandled: {__ex.Message}\n{__ex.StackTrace}", __ex); }
    }

    private async Task UninstallDetailModAsync(object sender)
    {
        if ((sender as System.Windows.Controls.Button)?.DataContext is Mod mod)
            await _viewModel.UninstallDetailModAsync(mod);
    }

    /// <summary>Opens the ContentBrowserWindow pre-configured for mods — installation is pre-selected, selector hidden.</summary>
    private async void BrowseMods_Click(object sender, RoutedEventArgs e)
    {
        try
        {

            if (_viewModel.DetailInstallation == null) return;
            var sp = (Application.Current as App)?.ServiceProvider;
            if (sp == null) return;

            var modService      = sp.GetRequiredService<IModService>();
            var installationSvc = sp.GetRequiredService<IInstallationService>();
            var contentSvc      = sp.GetRequiredService<IContentService>();
            var modrinthClient  = sp.GetRequiredService<IModrinthApiClient>();
            var modsVm          = new ModsPageViewModel(modService, installationSvc, contentSvc, modrinthClient);
            _ = modsVm.PreSelectInstallationAsync(_viewModel.DetailInstallation.Id);

            // Tell ModsPage to hide the installation selector — it is already chosen
            MinecraftControlHub.UI.Pages.ModsPage.HideInstallationSelectorOnNext = true;

            var win = new BrowseModsWindow(modsVm) { Owner = this };
            win.ShowDialog();
            await _viewModel.RefreshDetailModsPublicAsync();
    
        }
        catch (Exception __ex)
        {
            _log.LogError("BrowseMods_Click", $"Unhandled in BrowseMods_Click: {__ex.Message}\n{__ex.StackTrace}", __ex);
        }
    }

    // Drag & drop for the mods list
    private void ModsDropZone_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void ModsDropZone_Drop(object sender, DragEventArgs e)
    {
        try
        {

            if (_viewModel.DetailInstallation == null) return;
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files == null) return;

            var destDir = Path.Combine(AppPaths.InstanceDir(_viewModel.DetailInstallation.Id), "mods");
            Directory.CreateDirectory(destDir);

            foreach (var file in files.Where(f => f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)))
            {
                var dest = Path.Combine(destDir, Path.GetFileName(file));
                try { File.Copy(file, dest, overwrite: true); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Mod drop copy error: {ex.Message}"); }
            }

            await _viewModel.RefreshDetailModsPublicAsync();
    
        }
        catch (Exception __ex)
        {
            _log.LogError("ModsDropZone_Drop", $"Unhandled in ModsDropZone_Drop: {__ex.Message}\n{__ex.StackTrace}", __ex);
        }
    }

    // ── Advanced tab ──────────────────────────────────────────────────────────

    private void CalculateRam_Click(object sender, RoutedEventArgs e)
        => _viewModel.CalculateRecommendedRam();

    private async void SaveDetail_Click(object sender, RoutedEventArgs e)
        => await _viewModel.SaveInstallationDetailAsync();

    private async void ExportInstallationDetail_Click(object sender, RoutedEventArgs e)
    {
        try
        {

            if (_viewModel.DetailInstallation == null) return;
            var safeName = string.Concat(_viewModel.DetailInstallation.Name.Select(c => char.IsLetterOrDigit(c) ? c : '-'));
            if (string.IsNullOrWhiteSpace(safeName)) safeName = "installation";
            var dialog = new SaveFileDialog
            {
                Filter = "Modrinth pack (*.mrpack)|*.mrpack",
                FileName = $"{safeName}.mrpack",
                Title = "Export installation to .mrpack"
            };
            if (dialog.ShowDialog() == true)
                await _viewModel.ExportInstallationDetailAsync(dialog.FileName);
    
        }
        catch (Exception __ex)
        {
            _log.LogError("ExportInstallationDetail_Click", $"Unhandled in ExportInstallationDetail_Click: {__ex.Message}\n{__ex.StackTrace}", __ex);
        }
    }

    private async void ExportInstallationDetailPrism_Click(object sender, RoutedEventArgs e)
    {
        try
        {

            if (_viewModel.DetailInstallation == null) return;
            var safeName = string.Concat(_viewModel.DetailInstallation.Name.Select(c => char.IsLetterOrDigit(c) ? c : '-'));
            if (string.IsNullOrWhiteSpace(safeName)) safeName = "installation";
            var dialog = new SaveFileDialog
            {
                Filter = "Prism Launcher instance zip (*.zip)|*.zip",
                FileName = $"{safeName}.zip",
                Title = "Export as Prism Launcher instance zip"
            };
            if (dialog.ShowDialog() == true)
                await _viewModel.ExportInstallationDetailPrismAsync(dialog.FileName);
    
        }
        catch (Exception __ex)
        {
            _log.LogError("ExportInstallationDetailPrism_Click", $"Unhandled in ExportInstallationDetailPrism_Click: {__ex.Message}\n{__ex.StackTrace}", __ex);
        }
    }

    private async void DownloadJava_Click(object sender, RoutedEventArgs e)
        => await _viewModel.DownloadJavaForDetailAsync();

    // ── Content tab ───────────────────────────────────────────────────────────

    private void EnsureContentTabsInitialized()
    {
        if (_viewModel.DetailInstallation == null) return;

        var inst = _viewModel.DetailInstallation;

        // Re-create tabs if the installation changed or first time. LoadAsync() is
        // ONLY called when the tabs were actually (re)created — calling it every time
        // this method runs (e.g. when quickly switching back to Shaders after
        // installing a shader) would start two concurrent loads on the same tab and
        // corrupt the UI.
        var tabsJustCreated = false;

        if (_resourcePackTab == null || _resourcePackTab.InstallationId != inst.Id)
        {
            _log.Log("ContentTabs", $"Creating new content tabs for installation={inst.Id} ({inst.Name}), MC={inst.MinecraftVersion}, loader={inst.Loader}");
            _resourcePackTab = new ContentTabViewModel(_contentService, inst.Id, inst.MinecraftVersion, inst.Loader, ContentType.ResourcePack);
            _shaderPackTab   = new ContentTabViewModel(_contentService, inst.Id, inst.MinecraftVersion, inst.Loader, ContentType.ShaderPack);
            tabsJustCreated  = true;
        }

        // Safely assign DataContext — panels may not exist yet if called too early
        if (ResourcePacksPanel != null) ResourcePacksPanel.DataContext = _resourcePackTab;
        if (ShaderPacksPanel   != null) ShaderPacksPanel.DataContext   = _shaderPackTab;

        if (tabsJustCreated)
        {
            _log.Log("ContentTabs", "Starting initial LoadAsync for ResourcePack and ShaderPack tabs");
            _ = _resourcePackTab.LoadAsync();
            _ = _shaderPackTab?.LoadAsync();
        }

        ShowContentSubSection("Mods");
    }

    private void ContentSubNav_Checked(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is RadioButton { Tag: string tag })
            ShowContentSubSection(tag);
    }

    private void ShowContentSubSection(string section)
    {
        _contentSubSection = section;
        if (ModsContentPanel == null) return;
        ModsContentPanel.Visibility    = section == "Mods"          ? Visibility.Visible : Visibility.Collapsed;
        ResourcePacksPanel.Visibility  = section == "ResourcePacks" ? Visibility.Visible : Visibility.Collapsed;
        ShaderPacksPanel.Visibility    = section == "ShaderPacks"   ? Visibility.Visible : Visibility.Collapsed;
        if (MapsPanel != null)
            MapsPanel.Visibility       = section == "Maps"          ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void BrowseResourcePacks_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            EnsureContentTabsInitialized();
            await OpenContentBrowserAsync(ContentType.ResourcePack, _resourcePackTab);
        }
        catch (Exception ex)
        {
            _log.LogError("Browse", "BrowseResourcePacks_Click failed", ex);
            System.Windows.MessageBox.Show($"Error opening resource pack browser: {ex.Message}", "Error",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private async void BrowseShaderPacks_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            EnsureContentTabsInitialized();
            await OpenContentBrowserAsync(ContentType.ShaderPack, _shaderPackTab);
        }
        catch (Exception ex)
        {
            _log.LogError("Browse", "BrowseShaderPacks_Click failed", ex);
            System.Windows.MessageBox.Show($"Error opening shader pack browser: {ex.Message}", "Error",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private void BrowseModpacks_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_viewModel.DetailInstallation == null) return;
            var sp = (Application.Current as App)?.ServiceProvider;
            if (sp == null) return;

            var modService = sp.GetRequiredService<IModService>();
            var installationSvc = sp.GetRequiredService<IInstallationService>();
            var contentSvc = sp.GetRequiredService<IContentService>();
            var modrinthClient = sp.GetRequiredService<IModrinthApiClient>();
            var modsVm = new ModsPageViewModel(modService, installationSvc, contentSvc, modrinthClient);
            _ = modsVm.PreSelectInstallationAsync(_viewModel.DetailInstallation.Id);

            // Tell ModsPage to hide the installation selector — it is already chosen
            MinecraftControlHub.UI.Pages.ModsPage.HideInstallationSelectorOnNext = true;
            // Request that the ModsPage open the Modpacks tab initially
            MinecraftControlHub.UI.Pages.ModsPage.InitialTabOnNext = "Modpacks";

            var win = new BrowseModsWindow(modsVm) { Owner = this };
            win.ShowDialog();
        }
        catch (Exception ex)
        {
            _log.LogError("BrowseModpacks_Click", $"Unhandled in BrowseModpacks_Click: {ex.Message}\n{ex.StackTrace}", ex);
        }
    }

    private async Task OpenContentBrowserAsync(ContentType type, ContentTabViewModel? tab)
    {
        if (_viewModel.DetailInstallation == null || tab == null) return;
        _log.Log("ContentBrowser", $"Opening ContentBrowser for type={type}");
        try
        {
            var sp = (Application.Current as App)?.ServiceProvider;
            var modSvc = sp?.GetService(typeof(IModService)) as IModService;
            var inst = _viewModel.DetailInstallation;
            var vm = new ContentBrowserViewModel(
                _contentService,
                inst.Id, inst.MinecraftVersion, inst.Loader,
                type,
                tab.InstalledItems,
                modSvc);
            var win = new ContentBrowserWindow(vm) { Owner = this };
            win.ShowDialog();
            _log.Log("ContentBrowser", $"ContentBrowser closed for type={type}, reloading installed items...");
            // Await the reload so UI updates happen on the UI thread properly.
            await tab.LoadAsync();
            _log.Log("ContentBrowser", $"Reload complete for type={type}, {tab.InstalledItems.Count} items");
        }
        catch (Exception ex)
        {
            _log.LogError("ContentBrowser", $"OpenContentBrowserAsync failed for type={type}: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}", ex);
            throw; // re-throw so BrowseShaderPacks_Click catch can show the message
        }
    }

    // Drag & drop for content panels
    private void ContentPanel_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void ContentPanel_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        if (files == null) return;

        EnsureContentTabsInitialized();

        var tab = _contentSubSection switch
        {
            "ResourcePacks" => _resourcePackTab,
            "ShaderPacks"   => _shaderPackTab,
            _ => null
        };
        if (tab == null) return;

        foreach (var filePath in files)
            _ = tab.AddDroppedFileAsync(filePath);
    }

    private async void DeleteContentItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {

            if ((sender as Button)?.DataContext is not ContentItem item) return;
            var tab = item.Type switch
            {
                ContentType.ResourcePack => _resourcePackTab,
                ContentType.ShaderPack   => _shaderPackTab,
                _ => null
            };
            if (tab != null)
                await tab.DeleteItemAsync(item);
    
        }
        catch (Exception __ex)
        {
            _log.LogError("DeleteContentItem_Click", $"Unhandled in DeleteContentItem_Click: {__ex.Message}\n{__ex.StackTrace}", __ex);
        }
    }
    private void MapsPanel_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        var zips  = files.Where(f => f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                                  || System.IO.Directory.Exists(f)).ToArray();
        if (zips.Length == 0) return;

        foreach (var path in zips)
        {
            try
            {
                var worldsDir = System.IO.Path.Combine(
                    _viewModel.DetailInstallation?.GameDirectory ?? string.Empty, "saves");
                System.IO.Directory.CreateDirectory(worldsDir);

                if (System.IO.Directory.Exists(path))
                {
                    // Dragged a folder directly — copy it
                    var dest = System.IO.Path.Combine(worldsDir, System.IO.Path.GetFileName(path));
                    CopyDirectory(path, dest);
                }
                else
                {
                    // .zip — extract into saves/
                    System.IO.Compression.ZipFile.ExtractToDirectory(path, worldsDir, overwriteFiles: true);
                }

                // Add to UI list
                var name = System.IO.Path.GetFileNameWithoutExtension(path);
                var item = new ContentItem { Name = name, Type = ContentType.World };
                _installedMaps.Add(item);
                RefreshMapsUI();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Could not install world: {ex.Message}", "Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }
    }

    // ── Screenshots ───────────────────────────────────────────────────────────

    private async Task LoadScreenshotsAsync()
    {
        var gameDir = _viewModel.DetailInstallation?.GameDirectory;
        if (string.IsNullOrWhiteSpace(gameDir)) return;

        var screenshotsDir = System.IO.Path.Combine(gameDir, "screenshots");

        ScreenshotsPanel.Children.Clear();
        ScreenshotsEmptyState.Visibility = Visibility.Collapsed;
        ScreenshotsPanel.Visibility      = Visibility.Collapsed;

        if (!System.IO.Directory.Exists(screenshotsDir))
        {
            ScreenshotsEmptyState.Visibility = Visibility.Visible;
            ScreenshotCountText.Text         = "No screenshots folder";
            return;
        }

        var files = await Task.Run(() =>
            System.IO.Directory.GetFiles(screenshotsDir, "*", System.IO.SearchOption.TopDirectoryOnly)
                .Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => System.IO.File.GetLastWriteTime(f))
                .ToArray());

        if (files.Length == 0)
        {
            ScreenshotsEmptyState.Visibility = Visibility.Visible;
            ScreenshotCountText.Text         = "No screenshots yet";
            return;
        }

        ScreenshotCountText.Text = $"{files.Length} screenshot{(files.Length == 1 ? "" : "s")}";

        foreach (var file in files)
        {
            var card = BuildScreenshotCard(file);
            ScreenshotsPanel.Children.Add(card);
        }

        ScreenshotsPanel.Visibility = Visibility.Visible;
    }

    private System.Windows.Controls.Border BuildScreenshotCard(string filePath)
    {
        // Load thumbnail asynchronously on background thread
        var img = new System.Windows.Controls.Image
        {
            Width           = 200,
            Height          = 112,
            Stretch         = System.Windows.Media.Stretch.UniformToFill,
            SnapsToDevicePixels = true,
            ToolTip         = System.IO.Path.GetFileNameWithoutExtension(filePath)
        };

        // Load from disk on background thread
        _ = Task.Run(() =>
        {
            try
            {
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource        = new Uri(filePath, UriKind.Absolute);
                bitmap.CacheOption      = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 400; // decode at 2x thumbnail width for crisp display
                bitmap.EndInit();
                bitmap.Freeze();
                Dispatcher.Invoke(() => img.Source = bitmap);
            }
            catch { /* corrupt / unreadable image — leave blank */ }
        });

        var card = new System.Windows.Controls.Border
        {
            Width           = 200,
            Height          = 130,
            CornerRadius    = new System.Windows.CornerRadius(8),
            Margin          = new Thickness(0, 0, 12, 12),
            ClipToBounds    = true,
            Cursor          = System.Windows.Input.Cursors.Hand,
            Background      = (System.Windows.Media.Brush)FindResource("BrushPanelElevated"),
            BorderBrush     = (System.Windows.Media.Brush)FindResource("BrushBorder"),
            BorderThickness = new Thickness(1)
        };

        var grid = new System.Windows.Controls.Grid();

        var nameBar = new System.Windows.Controls.Border
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            Background        = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(0xCC, 0x0, 0x0, 0x0)),
            Padding           = new Thickness(8, 4, 8, 4)
        };
        nameBar.Child = new System.Windows.Controls.TextBlock
        {
            Text              = System.IO.Path.GetFileNameWithoutExtension(filePath),
            FontFamily        = (System.Windows.Media.FontFamily)FindResource("FontBody"),
            FontSize          = 10,
            Foreground        = System.Windows.Media.Brushes.White,
            TextTrimming      = System.Windows.TextTrimming.CharacterEllipsis
        };

        grid.Children.Add(img);
        grid.Children.Add(nameBar);
        card.Child = grid;

        // Click: open full image in default viewer
        card.MouseLeftButtonUp += (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(filePath) { UseShellExecute = true }); }
            catch { }
        };

        return card;
    }

    private static void CopyDirectory(string src, string dst)
    {
        System.IO.Directory.CreateDirectory(dst);
        foreach (var file in System.IO.Directory.GetFiles(src))
            System.IO.File.Copy(file, System.IO.Path.Combine(dst, System.IO.Path.GetFileName(file)), true);
        foreach (var dir in System.IO.Directory.GetDirectories(src))
            CopyDirectory(dir, System.IO.Path.Combine(dst, System.IO.Path.GetFileName(dir)));
    }

    private void DeleteMapItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: ContentItem item })
        {
            _installedMaps.Remove(item);
            RefreshMapsUI();
        }
    }

    private void RefreshMapsUI()
    {
        if (MapsListControl != null)
            MapsListControl.ItemsSource = null;
        if (MapsListControl != null)
            MapsListControl.ItemsSource = _installedMaps;
    }

    // ── Content item: enable/disable toggle ──────────────────────────────────

    private async void ToggleContentItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {

            if ((sender as System.Windows.Controls.Primitives.ToggleButton)?.DataContext is not ContentItem item) return;
            var tab = item.Type switch
            {
                ContentType.ResourcePack => _resourcePackTab,
                ContentType.ShaderPack   => _shaderPackTab,
                _ => null
            };
            if (tab != null)
                await tab.ToggleItemEnabledAsync(item);
    
        }
        catch (Exception __ex)
        {
            _log.LogError("ToggleContentItem_Click", $"Unhandled in ToggleContentItem_Click: {__ex.Message}\n{__ex.StackTrace}", __ex);
        }
    }

    // ── Content item: version picker ─────────────────────────────────────────

    private async void ContentVersionPicker_Click(object sender, RoutedEventArgs e)
    {
        try
        {

            if (sender is not System.Windows.Controls.Button btn) return;
            if (btn.DataContext is not ContentItem item) return;
            if (string.IsNullOrWhiteSpace(item.ModrinthId))
            {
                System.Windows.MessageBox.Show(
                    "This shader/resource pack was not installed via the built-in browser — version history is only available for items installed from Modrinth through this app.",
                    "Version picker", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }

            // Ensure tabs exist before resolving the correct tab instance.
            EnsureContentTabsInitialized();

            var tab = item.Type switch
            {
                ContentType.ResourcePack => _resourcePackTab,
                ContentType.ShaderPack   => _shaderPackTab,
                _ => null
            };
            if (tab == null) return;

            btn.IsEnabled = false;
            btn.Content   = "Loading…";
            try
            {
                var versions = await tab.GetVersionsAsync(item);
                if (versions.Count == 0)
                {
                    System.Windows.MessageBox.Show("No versions found on Modrinth for this Minecraft version.",
                        "Version picker", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                    return;
                }

                // Simple picker: show a dialog with a ListBox
                var picker = new ContentVersionPickerDialog(versions) { Owner = this };
                if (picker.ShowDialog() == true && picker.SelectedVersion != null)
                {
                    var msg = await tab.ChangeVersionAsync(item, picker.SelectedVersion);
                    System.Windows.MessageBox.Show(msg, "Version changed",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                _log.LogError("ContentVersionPicker", $"Version picker failed for '{item.Name}'", ex);
                System.Windows.MessageBox.Show(
                    $"Could not load versions: {ex.Message}",
                    "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                btn.IsEnabled = true;
                btn.Content   = "Version";
            }
    
        }
        catch (Exception __ex)
        {
            _log.LogError("ContentVersionPicker_Click", $"Unhandled in ContentVersionPicker_Click: {__ex.Message}\n{__ex.StackTrace}", __ex);
        }
    }


    // ── Screenshots ───────────────────────────────────────────────────────────

    private Border CreateScreenshotThumb(string filePath)
    {
        var image = new System.Windows.Controls.Image
        {
            Stretch = System.Windows.Media.Stretch.UniformToFill,
            Cursor = System.Windows.Input.Cursors.Hand
        };

        try
        {
            var bitmap = new System.Windows.Media.Imaging.BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
            bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            image.Source = bitmap;
        }
        catch { /* Failed to load image */ }

        var border = new Border
        {
            Width = 120,
            Height = 90,
            CornerRadius = new System.Windows.CornerRadius(6),
            Margin = new System.Windows.Thickness(6),
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 30, 30)),
            BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(70, 70, 70)),
            BorderThickness = new System.Windows.Thickness(1),
            Child = image,
            Tag = filePath
        };

        var mouseEnter = new System.Windows.Input.MouseEventHandler((s, e) =>
            border.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(120, 120, 120)));

        var mouseLeave = new System.Windows.Input.MouseEventHandler((s, e) =>
            border.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(70, 70, 70)));

        var mouseDown = new System.Windows.Input.MouseButtonEventHandler((s, e) =>
        {
            if (border.Tag is string path)
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
                }
                catch { /* ignore */ }
        });

        border.MouseEnter += mouseEnter;
        border.MouseLeave += mouseLeave;
        border.MouseDown += mouseDown;

        return border;
    }

    private void OpenScreenshotsFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.DetailInstallation == null) return;

        var screenshotsDir = Path.Combine(
            AppPaths.InstanceDir(_viewModel.DetailInstallation.Id),
            "screenshots");

        Directory.CreateDirectory(screenshotsDir);

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(screenshotsDir) { UseShellExecute = true });
        }
        catch { /* ignore */ }
    }

    private async void RefreshScreenshots_Click(object sender, RoutedEventArgs e)
        => await LoadScreenshotsAsync();


    // ── Server Sync ───────────────────────────────────────────────────────────

    private async Task LoadServerSyncAsync()
    {
        if (_serverService == null) return;
        ServerSyncStatusTextBlock.Text = string.Empty;

        var servers = await _serverService.GetAllServersAsync();
        var installation = _viewModel.DetailInstallation;
        if (installation == null) return;

        _serverSyncRows.Clear();
        foreach (var server in servers)
        {
            bool compatible = IsServerCompatibleWithInstallation(server, installation);
            _serverSyncRows.Add(new ServerSyncRow(server, compatible));
        }

        ServerSyncItemsControl.ItemsSource = null;
        ServerSyncItemsControl.ItemsSource = _serverSyncRows;
        ServerSyncEmptyState.Visibility = _serverSyncRows.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool IsServerCompatibleWithInstallation(Server server, Installation inst)
    {
        if (server.MinecraftVersion != inst.MinecraftVersion) return false;
        return inst.Loader switch
        {
            LoaderType.Fabric   => server.Type == ServerType.Fabric,
            LoaderType.Forge    => server.Type == ServerType.Forge,
            LoaderType.NeoForge => server.Type == ServerType.NeoForge,
            LoaderType.Quilt    => server.Type == ServerType.Quilt,
            LoaderType.Vanilla  => server.Type == ServerType.Paper
                                || server.Type == ServerType.Purpur
                                || server.Type == ServerType.Vanilla,
            _ => false
        };
    }

    private async void ServerSyncInstance_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not ServerSyncRow row) return;
        btn.IsEnabled = false;
        btn.Content   = "Syncing…";
        ServerSyncStatusTextBlock.Text = $"Syncing from {row.Name}…";
        try
        {
            var result = await RunServerSyncAsync(row.Server);
            ServerSyncStatusTextBlock.Text = result;
        }
        catch (Exception ex)
        {
            ServerSyncStatusTextBlock.Text = $"Sync failed: {ex.Message}";
        }
        finally
        {
            btn.Content   = "Sync now";
            btn.IsEnabled = true;
        }
    }

    private async void ServerSyncAll_Click(object sender, RoutedEventArgs e)
    {
        var toSync = _serverSyncRows.Where(r => r.IsCompatible && r.SyncEnabled).ToList();
        if (toSync.Count == 0)
        {
            ServerSyncStatusTextBlock.Text = "No servers toggled for sync. Enable at least one compatible server.";
            return;
        }

        ServerSyncAllButton.IsEnabled = false;
        var summaries = new List<string>();
        foreach (var row in toSync)
        {
            ServerSyncStatusTextBlock.Text = $"Syncing from {row.Name}…";
            try   { summaries.Add($"{row.Name}: {await RunServerSyncAsync(row.Server)}"); }
            catch (Exception ex) { summaries.Add($"{row.Name}: failed — {ex.Message}"); }
        }
        ServerSyncStatusTextBlock.Text = string.Join("\n", summaries);
        ServerSyncAllButton.IsEnabled  = true;
    }


    private async void MakeServer_Click(object sender, RoutedEventArgs e)
    {
        if (_serverService == null) return;

        MakeServerButton.IsEnabled = false;
        MakeServerButton.Content   = "Creating…";
        ServerSyncStatusTextBlock.Text = "Creating server from this instance…";

        try
        {
            var installation = _viewModel.DetailInstallation;
            if (installation == null) return;

            // Map loader → server type
            // Vanilla instance → Vanilla server
            var serverType = installation.Loader switch
            {
                LoaderType.Fabric   => ServerType.Fabric,
                LoaderType.Forge    => ServerType.Forge,
                LoaderType.NeoForge => ServerType.NeoForge,
                LoaderType.Quilt    => ServerType.Quilt,
                _                   => ServerType.Vanilla
            };

            var server = new Server
            {
                Name             = $"{installation.Name} (Server)",
                Type             = serverType,
                MinecraftVersion = installation.MinecraftVersion,
                MaxMemoryMB      = installation.MaxMemoryMB ?? 2048,
                MinMemoryMB      = installation.MinMemoryMB ?? 1024,
                AllowOnlineMode  = true,
                MaxPlayers       = 10,
                CurrentPlayers   = 0,
                LoadPercent      = 0,
                Gamemode         = "survival",
                Difficulty       = "normal",
                AllowCheats      = false,
                WhiteListEnabled = false,
                Motd             = installation.Name
            };

            await _serverService.CreateServerAsync(server);

            ServerSyncStatusTextBlock.Text =
                $"✓ Server '{server.Name}' created ({serverType} {installation.MinecraftVersion}). " +
                "You can find it in the Servers tab.";

            // Refresh the server list so the new server shows up immediately
            await LoadServerSyncAsync();
        }
        catch (Exception ex)
        {
            ServerSyncStatusTextBlock.Text = $"Failed to create server: {ex.Message}";
        }
        finally
        {
            MakeServerButton.IsEnabled = true;
            MakeServerButton.Content   = "➕ Make Server";
        }
    }

    private async void RefreshServerSync_Click(object sender, RoutedEventArgs e)
        => await LoadServerSyncAsync();

    // ── Health Check ───────────────────────────────────────────────────────────

    private async void RefreshHealth_Click(object sender, RoutedEventArgs e)
        => await RefreshHealthAsync();

    private async Task RefreshHealthAsync()
    {
        if (_viewModel.DetailInstallation == null) return;
        await _viewModel.LoadDetailHealthCheckAsync();
        UpdateHealthStatusIcons();
    }

    private void UpdateHealthStatusIcons()
    {
        var d = _viewModel.DetailHealthCheck;
        UpdateStatusIcon(JavaStatusIcon, d != null ? d.JavaStatus.ToString() : null);
        UpdateStatusIcon(RamStatusIcon, d != null ? d.RamStatus.ToString() : null);
        UpdateStatusIcon(ModsStatusIcon, d != null ? d.ModsStatus.ToString() : null);
        UpdateStatusIcon(StabilityStatusIcon, d != null ? d.StabilityStatus.ToString() : null);

        if (d != null)
        {
            var overall = d.OverallScore;
            var overallLabel = overall >= 75 ? "Healthy" : (overall >= 50 ? "Warning" : "Critical");
            UpdateStatusIcon(OverallStatusText, overallLabel);
        }
        else
        {
            UpdateStatusIcon(OverallStatusText, null);
        }
    }

    private void UpdateStatusIcon(TextBlock statusTextBlock, string? status)
    {
        if (statusTextBlock == null) return;
        statusTextBlock.Text = status switch
        {
            "Healthy" => "✓",
            "Warning" => "⚠",
            "Critical" => "✗",
            _ => ""
        };
        statusTextBlock.Foreground = status switch
        {
            "Healthy" => new SolidColorBrush(Colors.Green) { Opacity = 0.8 },
            "Warning" => new SolidColorBrush(Colors.Orange) { Opacity = 0.8 },
            "Critical" => new SolidColorBrush(Colors.Red) { Opacity = 0.8 },
            _ => (Brush)FindResource("BrushTextMuted")
        };
    }

    private async Task<string> RunServerSyncAsync(Server server)
    {
        if (_modServiceForServerSync == null)
            throw new InvalidOperationException("Mod service unavailable.");

        var installation = _viewModel.DetailInstallation;
        if (installation == null) return "No installation selected.";
        var modsFound    = await ScanServerModFilesAsync(server);

        if (modsFound.Count == 0)
            return "No Modrinth-identifiable mods found in server folder.";

        var alreadyInstalled = await _modServiceForServerSync.GetInstalledModsAsync(installation.Id);
        var installedIds = new HashSet<string>(
            alreadyInstalled.Where(m => m.ModrinthId != null).Select(m => m.ModrinthId!),
            StringComparer.OrdinalIgnoreCase);

        var installed = 0;
        var skipped   = 0;
        foreach (var sr in modsFound)
        {
            if (installedIds.Contains(sr.ModrinthId)) { skipped++; continue; }
            try { await _modServiceForServerSync.InstallModFromSearchAsync(installation, sr); installed++; }
            catch { skipped++; }
        }

        var depResult = await _modServiceForServerSync.CheckDependencyCompatibilityAsync(installation);
        var fixedDeps = 0;
        foreach (var issue in depResult.Issues)
            try { if (await _modServiceForServerSync.FixDependencyIssueAsync(installation, issue)) fixedDeps++; }
            catch { /* ignore */ }

        return $"Done. {installed} mods installed, {skipped} already up to date" +
               (fixedDeps > 0 ? $", {fixedDeps} dependencies fixed." : ".");
    }

    private async Task<List<ModSearchResult>> ScanServerModFilesAsync(Server server)
    {
        if (_modrinthClientForServerSync == null || string.IsNullOrWhiteSpace(server.ServerDirectory))
            return new List<ModSearchResult>();

        var folderName = server.Type switch
        {
            ServerType.Fabric   => "mods",
            ServerType.Forge    => "mods",
            ServerType.NeoForge => "mods",
            ServerType.Quilt    => "mods",
            _                   => "plugins"
        };

        var dir = Path.Combine(server.ServerDirectory, folderName);
        if (!Directory.Exists(dir)) return new List<ModSearchResult>();

        var jars    = Directory.GetFiles(dir, "*.jar");
        var results = new List<ModSearchResult>();

        foreach (var jar in jars)
        {
            try
            {
                var bytes   = await File.ReadAllBytesAsync(jar);
                var hashHex = Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant();

                var version = await _modrinthClientForServerSync.GetVersionByHashAsync(hashHex);
                if (version == null || string.IsNullOrWhiteSpace(version.ModrinthId)) continue;

                var detail = await _modrinthClientForServerSync.GetModDetailAsync(version.ModrinthId);
                if (detail == null) continue;

                results.Add(new ModSearchResult
                {
                    ModrinthId  = detail.ModrinthId,
                    Name        = detail.Name,
                    Description = detail.Description,
                    Author      = detail.Author,
                    IconUrl     = detail.IconUrl,
                    Downloads   = detail.Downloads
                });
            }
            catch { /* skip unidentifiable jars */ }
        }
        return results;
    }

    private async void AutoTuneRam_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var inst = _viewModel.DetailInstallation;
            var health = _viewModel.DetailHealthCheck;
            if (inst == null || health == null)
            {
                MessageBox.Show("No installation selected or health data unavailable.", "Auto-tune RAM", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var current = (inst.MaxMemoryMB.HasValue && inst.MaxMemoryMB.Value > 0) ? inst.MaxMemoryMB.Value : 2048;
            var usage = health.RamUsagePercent; // 0-100
            int recommended;

            int RoundUpToMultiple(int value, int multiple)
            {
                if (multiple <= 0) return value;
                return ((value + multiple - 1) / multiple) * multiple;
            }

            if (usage < 40)
            {
                // reduce to half (but at least 512MB)
                var half = current / 2;
                recommended = Math.Max(512, RoundUpToMultiple(half, 128));
            }
            else if (usage > 85)
            {
                // increase by ~25%, rounded up to 128MB
                var increased = (current * 5 + 3) / 4; // approximate ceil of *1.25
                recommended = Math.Max(current + 128, RoundUpToMultiple(increased, 128));
            }
            else
            {
                // within optimal range — no change
                MessageBox.Show($"RAM usage is within the healthy range ({usage:F1}%). No change recommended.", "Auto-tune RAM", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            RamRecommendationText.Text = $"Recommended: {recommended} MB";

            var res = MessageBox.Show($"Apply recommended max heap of {recommended} MB to this installation?\n\nThis will update the installation settings and persist the change.", "Auto-tune RAM", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res != MessageBoxResult.Yes) return;

            inst.MaxMemoryMB = recommended;
            if (_installationService != null)
                await _installationService.UpdateInstallationAsync(inst);

            // Save via viewmodel and refresh health
            await _viewModel.LoadDetailHealthCheckAsync();
            UpdateHealthStatusIcons();
            MessageBox.Show("RAM recommendation applied.", "Auto-tune RAM", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _log.LogError("AutoTuneRam_Click", $"Auto-tune failed: {ex.Message}", ex);
            MessageBox.Show($"Auto-tune failed: {ex.Message}", "Auto-tune RAM", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

}

/// <summary>
/// Row view-model for the Server Sync list. Mirrors InstanceSyncRow in ServerPreviewWindow
/// but holds a Server instead of an Installation.
/// </summary>
public class ServerSyncRow : System.ComponentModel.INotifyPropertyChanged
{
    public Server Server { get; }

    public string Name         { get; }
    public string VersionLabel { get; }
    public string Initial      { get; }
    public bool   IsCompatible { get; }
    public double RowOpacity   => IsCompatible ? 1.0 : 0.45;

    public string BadgeText => IsCompatible ? "Compatible" : "Different version";

    public System.Windows.Media.Brush BadgeBackground => IsCompatible
        ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x22, 0x22, 0xC5, 0x5E))
        : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x18, 0x80, 0x80, 0x80));

    public System.Windows.Media.Brush BadgeForeground => IsCompatible
        ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x22, 0xC5, 0x5E))
        : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x80, 0x80, 0x80));

    public System.Windows.Media.Brush BadgeBorder => IsCompatible
        ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x44, 0x22, 0xC5, 0x5E))
        : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x30, 0x80, 0x80, 0x80));

    private bool _syncEnabled;
    public bool SyncEnabled
    {
        get => _syncEnabled;
        set
        {
            _syncEnabled = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(SyncEnabled)));
        }
    }

    public ServerSyncRow(Server server, bool compatible)
    {
        Server       = server;
        Name         = server.Name;
        VersionLabel = $"{server.MinecraftVersion} • {server.Type}";
        Initial      = string.IsNullOrEmpty(server.Name) ? "?" : server.Name[0].ToString().ToUpper();
        IsCompatible = compatible;
        SyncEnabled  = compatible;
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}
