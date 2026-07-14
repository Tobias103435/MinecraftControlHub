using System.Windows;
using System.Windows.Input;
using MinecraftControlHub.Core.Models;
using MinecraftControlHub.UI.ViewModels;

namespace MinecraftControlHub.UI.Windows;

/// <summary>
/// Standalone version-picker dialog for a single installed mod.
/// Opens from the "Change version" button in the mod list row.
/// </summary>
public partial class ModVersionPickerWindow : Window
{
    private readonly InstalledModRowViewModel _rowVm;
    private List<ModVersion> _allVersions = new();

    /// <summary>The version the user picked, or null if cancelled.</summary>
    public ModVersion? ChosenVersion { get; private set; }

    public ModVersionPickerWindow(InstalledModRowViewModel rowVm)
    {
        _rowVm = rowVm;
        InitializeComponent();

        ModNameLabel.Text        = rowVm.Mod?.Name ?? "(unknown mod)";
        CurrentVersionLabel.Text = $"Currently installed: {rowVm.InstalledVersionLabel}";

        Loaded += async (_, _) => await LoadVersionsAsync();
    }

    private async Task LoadVersionsAsync()
    {
        LoadingText.Visibility  = Visibility.Visible;
        VersionList.ItemsSource = null;
        CountLabel.Text         = string.Empty;

        try
        {
            await _rowVm.EnsureVersionsLoadedAsync();
            _allVersions = _rowVm.AvailableVersions;
        }
        catch (Exception ex)
        {
            LoadingText.Visibility = Visibility.Collapsed;
            StatusLabel.Text = $"Could not load versions: {ex.Message}";
            CountLabel.Text  = "0 versions";
            return;
        }

        LoadingText.Visibility = Visibility.Collapsed;

        if (_allVersions.Count == 0)
        {
            CountLabel.Text  = "No compatible versions found";
            StatusLabel.Text = "No versions found on Modrinth for this installation's Minecraft version.";
            return;
        }

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var stableOnly = StableOnlyCheckBox.IsChecked == true;
        var filtered   = stableOnly
            ? _allVersions.Where(v => !v.IsPrerelease).ToList()
            : _allVersions;

        VersionList.ItemsSource = filtered;
        CountLabel.Text         = $"{filtered.Count} version{(filtered.Count == 1 ? "" : "s")}";
    }

    private void FilterChanged(object sender, RoutedEventArgs e) => ApplyFilter();

    private async void VersionRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.FrameworkElement { Tag: ModVersion version }) return;

        // 1. Tell the user what's happening.
        StatusLabel.Text = $"Switching to {version.VersionNumber}...";
        // 2. Disable the whole window so the user can't double-click another row
        //    while the swap is in flight (the previous code only fire-and-forgot the
        //    swap and closed immediately, so the actual file swap was never awaited
        //    and could still be running — or could fail silently — after Close()).
        IsEnabled = false;

        // 3. Actually perform the version swap and AWAIT it so we only close once it
        //    really finished (success or failure).
        try
        {
            await _rowVm.ApplyVersionChangeDirectAsync(version);
        }
        catch (Exception ex)
        {
            // The swap threw — keep the window open so the user sees the error and
            // can pick a different version instead of silently disappearing.
            StatusLabel.Text = $"Could not switch version: {ex.Message}";
            IsEnabled = true;
            return;
        }

        // 4. Only close once the swap actually succeeded.
        ChosenVersion = version;
        DialogResult  = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
