using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Microsoft.Extensions.DependencyInjection;
using MinecraftControlHub.Core.Models;
using MinecraftControlHub.UI.ViewModels;

namespace MinecraftControlHub.UI.Pages;

public partial class ModsPage : UserControl
{
    private ModsPageViewModel? _viewModel;

    // Set this BEFORE constructing a ModsPage to inject a pre-configured ViewModel
    // (used by BrowseModsWindow to avoid the default DI-resolved VM overwriting its own)
    public static ModsPageViewModel? InjectedViewModel { get; set; }

    /// <summary>When true, the installation selector panel is hidden (the installation is pre-selected).</summary>
    public static bool HideInstallationSelectorOnNext { get; set; }

    // Optional initial tab to open when ModsPage is shown inside BrowseModsWindow.
    // Valid values: "Search", "Installed", "Modpacks". When null, default UI is used.
    public static string? InitialTabOnNext { get; set; }

    public ModsPage()
    {
        // Consume injected ViewModel if provided (must happen before InitializeComponent
        // so DataContext is set before bindings resolve)
        if (InjectedViewModel != null)
        {
            _viewModel = InjectedViewModel;
            InjectedViewModel = null;
            DataContext = _viewModel;
            InitializeComponent();
            if (HideInstallationSelectorOnNext)
            {
                HideInstallationSelectorOnNext = false;
                if (InstallationSelectorPanel != null)
                    InstallationSelectorPanel.IsVisible = false;
            }
            // Apply any initial tab requested by the caller
            if (!string.IsNullOrWhiteSpace(InitialTabOnNext))
            {
                ApplyInitialTab();
                InitialTabOnNext = null;
            }
            return;
        }
        InitializeComponent();
        // Apply initial tab if set for the normal (non-injected) construction path as well
        if (!string.IsNullOrWhiteSpace(InitialTabOnNext))
        {
            ApplyInitialTab();
            InitialTabOnNext = null;
        }
        var serviceProvider = (Application.Current as App)?.ServiceProvider;
        if (serviceProvider != null)
        {
            _viewModel = serviceProvider.GetRequiredService<ModsPageViewModel>();
            DataContext = _viewModel;
        }
    }

    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
        {
            await _viewModel.SearchModsAsync();
        }
    }

    private async void SearchBox_KeyDown(object sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == Avalonia.Input.Key.Enter && _viewModel != null)
        {
            await _viewModel.SearchModsAsync();
        }
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null && (sender as Button)?.DataContext is ModSearchResult searchResult)
        {
            await _viewModel.InstallModAsync(searchResult);
        }
    }

    private void View_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null && (sender as Button)?.DataContext is ModSearchResult searchResult)
        {
            _viewModel.ShowDetail(searchResult);
        }
    }

    private void BackToList_Click(object sender, RoutedEventArgs e)
    {
        _viewModel?.CloseDetail();
    }

    private void Finish_Click(object sender, RoutedEventArgs e)
    {
        _viewModel?.FinishInstall();
    }

    private void CopyInstallLog_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_viewModel?.InstallOverlayMessage))
        {
            try { _ = TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(_viewModel.InstallOverlayMessage); } catch { /* clipboard can be locked by another app - not worth failing over */ }
        }
    }

    private void PrevImage_Click(object sender, RoutedEventArgs e)
    {
        _viewModel?.PreviousImage();
    }

    private void NextImage_Click(object sender, RoutedEventArgs e)
    {
        _viewModel?.NextImage();
    }

    // Ensures the page scrolls even when the mouse is over inner content
    // (cards, images, etc.) that would otherwise swallow the wheel event.
    private void ScrollViewer_PreviewMouseWheel(object sender, Avalonia.Input.PointerWheelEventArgs e)
    {
        if (e.Handled || sender is not ScrollViewer scrollViewer)
            return;

        scrollViewer.Offset = new Avalonia.Vector(scrollViewer.Offset.X, scrollViewer.Offset.Y - e.Delta.Y);
        e.Handled = true;
    }

    private async void DetailInstall_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel?.SelectedMod != null)
        {
            await _viewModel.InstallModAsync(_viewModel.SelectedMod);
        }
    }

    private async void CheckModUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
        {
            await _viewModel.CheckForUpdatesAsync();
        }
    }

    private async void CheckDependencies_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
        {
            await _viewModel.CheckDependenciesAsync();
        }
    }

    private async void FixDependencyIssue_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null && (sender as Button)?.DataContext is DependencyIssue issue)
        {
            await _viewModel.FixDependencyIssueAsync(issue);
        }
    }

    private async void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null && (sender as Button)?.DataContext is Mod mod)
        {
            await _viewModel.UninstallModAsync(mod);
        }
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null && (sender as Button)?.DataContext is Mod mod)
        {
            // Warn before updating a mod that was itself installed as someone else's
            // dependency - bumping its version can silently break whatever mod pulled
            // it in (a stricter version range, a removed/renamed API, etc.) without any
            // obvious symptom until the game (or that other mod) crashes on next launch.
            if (mod.IsDependency)
            {
                var owner = TopLevel.GetTopLevel(this) as Window;
                if (owner != null)
                {
                    var confirm = await UI.Helpers.SimpleDialog.ConfirmAsync(owner,
                        $"\"{mod.Name}\" was installed automatically as a dependency of another mod.\n\n" +
                        "Updating it can break whatever mod relies on it (e.g. if it needs an exact " +
                        "or older version). Only continue if you're sure the mod that needs it also " +
                        "supports this newer version.\n\nUpdate anyway?",
                        "This mod is a dependency");

                    if (!confirm)
                        return;
                }
            }

            await _viewModel.UpdateModAsync(mod);
        }
    }
    private async void PrevPage_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
        {
            await _viewModel.PreviousPageAsync();
        }
    }

    private async void NextPage_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
        {
            await _viewModel.NextPageAsync();
        }
    }

    private async void PageNumber_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null && (sender as Button)?.DataContext is int page)
        {
            await _viewModel.GoToPageAsync(page);
        }
    }

    private void SearchTabButton_Click(object sender, RoutedEventArgs e)
    {
        SearchTabContent.IsVisible = true;
        InstalledTabContent.IsVisible = false;
        ModpacksTabContent.IsVisible = false;
        SearchTabButton.Theme = this.TryFindResource("PrimaryButtonStyle", out var st1) ? st1 as ControlTheme : null;
        InstalledTabButton.Theme = this.TryFindResource("SecondaryButtonStyle", out var st2) ? st2 as ControlTheme : null;
        ModpacksTabButton.Theme = this.TryFindResource("SecondaryButtonStyle", out var st3) ? st3 as ControlTheme : null;
    }

    private void InstalledTabButton_Click(object sender, RoutedEventArgs e)
    {
        SearchTabContent.IsVisible = false;
        InstalledTabContent.IsVisible = true;
        ModpacksTabContent.IsVisible = false;
        InstalledTabButton.Theme = this.TryFindResource("PrimaryButtonStyle", out var st1) ? st1 as ControlTheme : null;
        SearchTabButton.Theme = this.TryFindResource("SecondaryButtonStyle", out var st2) ? st2 as ControlTheme : null;
        ModpacksTabButton.Theme = this.TryFindResource("SecondaryButtonStyle", out var st3) ? st3 as ControlTheme : null;

        if (_viewModel != null)
        {
            _ = _viewModel.LoadInstalledModsAsync();
        }
    }

    private void ModpacksTabButton_Click(object sender, RoutedEventArgs e)
    {
        SearchTabContent.IsVisible = false;
        InstalledTabContent.IsVisible = false;
        ModpacksTabContent.IsVisible = true;
        ModpacksTabButton.Theme = this.TryFindResource("PrimaryButtonStyle", out var st1) ? st1 as ControlTheme : null;
        SearchTabButton.Theme = this.TryFindResource("SecondaryButtonStyle", out var st2) ? st2 as ControlTheme : null;
        InstalledTabButton.Theme = this.TryFindResource("SecondaryButtonStyle", out var st3) ? st3 as ControlTheme : null;

        // Auto-search when switching to modpacks tab if no results are loaded yet
        if (_viewModel != null && !_viewModel.HasModpackResults)
        {
            _ = _viewModel.SearchModpacksAsync();
        }
    }

    // Applies the initial tab specified in InitialTabOnNext (if any).
    private void ApplyInitialTab()
    {
        switch (InitialTabOnNext)
        {
            case "Search":
                SearchTabButton_Click(SearchTabButton, null!);
                break;
            case "Installed":
                InstalledTabButton_Click(InstalledTabButton, null!);
                break;
            case "Modpacks":
                ModpacksTabButton_Click(ModpacksTabButton, null!);
                break;
            default:
                // No-op for unknown values
                break;
        }
    }

    private async void BrowseModpacks_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel?.SelectedInstallation == null) return;
        var sp = (Avalonia.Application.Current as App)?.ServiceProvider;
        if (sp == null) return;

        var contentSvc = sp.GetRequiredService<MinecraftControlHub.Core.Services.IContentService>();
        var installation = _viewModel.SelectedInstallation;
        var installed = await contentSvc.GetInstalledContentAsync(installation.Id, MinecraftControlHub.Core.Models.ContentType.Modpack);
        var modSvc = sp.GetService(typeof(MinecraftControlHub.Core.Services.IModService)) as MinecraftControlHub.Core.Services.IModService;
        var vm = new MinecraftControlHub.UI.ViewModels.ContentBrowserViewModel(
            contentSvc, installation.Id, installation.MinecraftVersion, installation.Loader,
            MinecraftControlHub.Core.Models.ContentType.Modpack, installed, modSvc);
        var win = new MinecraftControlHub.UI.Windows.ContentBrowserWindow(vm);
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner != null)
            await win.ShowDialog(owner);
        else
            win.Show();
    }
    private void ModpackSearchBox_KeyDown(object sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == Avalonia.Input.Key.Enter)
        {
            e.Handled = true;
            _ = _viewModel?.SearchModpacksAsync();
        }
    }

    private void ModpackSearch_Click(object sender, RoutedEventArgs e)
    {
        _ = _viewModel?.SearchModpacksAsync();
    }

    private async void InstallModpack_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        if ((sender as Avalonia.Controls.Button)?.Tag is MinecraftControlHub.Core.Models.ContentSearchResult modpack)
        {
            await _viewModel.InstallModpackAsync(modpack);
        }
    }


    // ── Modpack expand / mod toggle ──────────────────────────────────────────

    private void ExpandModpack_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is ModpackCardViewModel card)
            card.ToggleExpand();
    }

    // ── Modpack pagination ────────────────────────────────────────────────────

    private async void ModpackPrevPage_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
            await _viewModel.ModpackPreviousPageAsync();
    }

    private async void ModpackNextPage_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
            await _viewModel.ModpackNextPageAsync();
    }

    private async void ModpackPageNumber_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        if ((sender as Button)?.Tag is int page)
            await _viewModel.ModpackGoToPageAsync(page);
    }

    private async void ModpackModToggle_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Avalonia.Controls.Primitives.ToggleButton)?.DataContext is ModRowViewModel row)
            await row.ToggleAsync();
    }

}
