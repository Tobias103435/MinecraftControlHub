using System.Windows;
using MinecraftControlHub.UI.Pages;
using MinecraftControlHub.UI.ViewModels;

namespace MinecraftControlHub.UI.Windows;

/// <summary>
/// Modal window that hosts the existing ModsPage as a standalone browser,
/// opened from the installation Settings → Mods → Browse Mods button.
/// Uses ModsPage.InjectedViewModel to inject the pre-configured ViewModel
/// before InitializeComponent runs (avoids the default DI-resolved VM overwrite).
/// </summary>
public partial class BrowseModsWindow : Window
{
    public BrowseModsWindow(ModsPageViewModel viewModel)
    {
        // Inject the ViewModel BEFORE InitializeComponent so ModsPage's
        // constructor picks it up instead of creating a fresh one from DI.
        ModsPage.InjectedViewModel = viewModel;
        InitializeComponent();
    }
}
