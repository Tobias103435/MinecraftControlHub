using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using MinecraftControlHub.Core.Models;
using MinecraftControlHub.Core.Services;

namespace MinecraftControlHub.UI.Windows;

/// <summary>
/// Row data for each mod shown in the compatibility check window.
/// </summary>
public class ModCompatibilityRow : INotifyPropertyChanged
{
    private int _selectedActionIndex;
    private bool _isEnabled;

    public Mod Mod { get; init; } = null!;
    public string Name => Mod.Name;
    public bool IsCompatible { get; set; }
    public bool WasAutoUpdated { get; set; }
    public bool ShowActions => !IsCompatible && !WasAutoUpdated;

    /// <summary>Whether the mod is currently enabled on disk.</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set { _isEnabled = value; OnPropertyChanged(); }
    }

    public string StatusText
    {
        get
        {
            if (WasAutoUpdated) return "\u2713 Auto-updated to compatible version";
            if (IsCompatible) return "\u2713 Compatible";
            return "\u2717 No compatible version found";
        }
    }

    /// <summary>0 = Skip, 1 = Disable, 2 = Uninstall</summary>
    public int SelectedActionIndex
    {
        get => _selectedActionIndex;
        set { _selectedActionIndex = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Result returned after the user closes the ModCompatibilityWindow.
/// </summary>
public class ModCompatibilityResult
{
    public bool Applied { get; set; }
    public List<ModCompatibilityRow> Rows { get; set; } = new();
}

/// <summary>
/// Shows all mods with their compatibility status and lets the user pick actions
/// for incompatible mods (skip/disable/uninstall) all at once.
/// </summary>
public partial class ModCompatibilityWindow : Window
{
    private readonly Installation _installation;
    private readonly IModService _modService;
    private readonly string _newVersion;
    private List<ModCompatibilityRow> _rows = new();

    public ModCompatibilityResult Result { get; } = new();

    public ModCompatibilityWindow(
        Installation installation,
        IModService modService,
        string oldVersion,
        string newVersion)
    {
        _installation = installation;
        _modService = modService;
        _newVersion = newVersion;

        InitializeComponent();

        SubHeaderText.Text = $"Checking compatibility: {oldVersion} → {newVersion}";
        Loaded += async (_, _) => await ScanModsAsync();
    }

    private async Task ScanModsAsync()
    {
        ProgressPanel.Visibility = Visibility.Visible;
        ApplyButton.IsEnabled = false;

        try
        {
            var mods = await _modService.GetInstalledModsAsync(_installation.Id);
            var total = mods.Count;
            var scanned = 0;

            ProgressBar.IsIndeterminate = false;
            ProgressBar.Maximum = total;

            foreach (var mod in mods)
            {
                scanned++;
                ProgressText.Text = $"Scanning {scanned}/{total}: {mod.Name}…";
                ProgressBar.Value = scanned;

                var row = new ModCompatibilityRow { Mod = mod, IsEnabled = mod.IsEnabled };

                if (string.IsNullOrWhiteSpace(mod.ModrinthId))
                {
                    // Can't check — assume compatible (manual mods)
                    row.IsCompatible = true;
                }
                else
                {
                    try
                    {
                        var versions = await _modService.GetModVersionsAsync(
                            mod.ModrinthId, _newVersion, _installation.Loader);

                        if (versions.Count > 0)
                        {
                            // Try auto-update
                            try
                            {
                                var didUpdate = await _modService.UpdateModAsync(_installation, mod.Id);
                                row.IsCompatible = true;
                                row.WasAutoUpdated = didUpdate;
                            }
                            catch
                            {
                                row.IsCompatible = true; // version exists even if update failed
                            }
                        }
                        else
                        {
                            row.IsCompatible = false;
                            row.SelectedActionIndex = 1; // Default to "Disable"
                        }
                    }
                    catch
                    {
                        // API error — mark as compatible (benefit of the doubt)
                        row.IsCompatible = true;
                    }
                }

                _rows.Add(row);
            }

            // Sort: incompatible first, then auto-updated, then compatible
            _rows = _rows
                .OrderBy(r => r.IsCompatible)
                .ThenByDescending(r => r.WasAutoUpdated)
                .ThenBy(r => r.Name)
                .ToList();

            ModList.ItemsSource = _rows;

            var incompatCount = _rows.Count(r => !r.IsCompatible);
            var updatedCount = _rows.Count(r => r.WasAutoUpdated);

            SummaryLabel.Text = incompatCount == 0
                ? $"All {total} mod(s) are compatible. {updatedCount} auto-updated."
                : $"{incompatCount} incompatible · {updatedCount} auto-updated · {total - incompatCount - updatedCount} OK";

            ProgressPanel.Visibility = Visibility.Collapsed;
            ApplyButton.IsEnabled = incompatCount > 0;

            // If everything is fine, just let them close
            if (incompatCount == 0)
            {
                ApplyButton.Content = "Done";
                ApplyButton.IsEnabled = true;
                CancelButton.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            ProgressText.Text = $"Error: {ex.Message}";
            ProgressBar.Visibility = Visibility.Collapsed;
            ApplyButton.IsEnabled = false;
        }
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        Result.Applied = true;
        Result.Rows = _rows;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Result.Applied = false;
        DialogResult = false;
        Close();
    }

    private async void ModToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Primitives.ToggleButton tb) return;
        if (tb.DataContext is not ModCompatibilityRow row) return;

        try
        {
            var ok = await _modService.ToggleModEnabledAsync(_installation, row.Mod.Id);
            if (ok)
                row.IsEnabled = !row.IsEnabled;
            else
                tb.IsChecked = row.IsEnabled; // revert toggle on failure
        }
        catch
        {
            tb.IsChecked = row.IsEnabled; // revert toggle on failure
        }
    }
}
