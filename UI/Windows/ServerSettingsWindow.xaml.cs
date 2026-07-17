using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Microsoft.Extensions.DependencyInjection;
using MinecraftControlHub.Core.Models;
using MinecraftControlHub.Core.Services;
using MinecraftControlHub.UI.Helpers;
using MinecraftControlHub.UI.ViewModels;

namespace MinecraftControlHub.UI.Windows;

public partial class ServerSettingsWindow : Window
{
    private readonly ServerSettingsViewModel _viewModel;
    private readonly List<Button> _navButtons = new();

    public ServerSettingsWindow(Server server)
    {
        var sp  = (Application.Current as App)?.ServiceProvider;
        var svc = sp?.GetRequiredService<IServerService>()
                  ?? throw new InvalidOperationException("IServerService not registered.");
        var mod = sp?.GetRequiredService<IModService>()
                  ?? throw new InvalidOperationException("IModService not registered.");
        var log = sp?.GetService<IAppLogService>() ?? new AppLogService();

        _viewModel  = new ServerSettingsViewModel(server, svc, mod, log);
        DataContext = _viewModel;

        InitializeComponent();

        _navButtons.AddRange(new[] { NavGeneral, NavMods, NavAdvanced });

        // Pre-fill editable fields
        NameBox.Text       = server.Name;
        VersionBox.Text    = server.MinecraftVersion;
        TypeBox.Text       = server.Type.ToString();
        MotdBox.Text       = server.Motd;
        MaxPlayersBox.Text = server.MaxPlayers.ToString();
        CheatsBox.IsChecked    = server.AllowCheats;
        WhitelistBox.IsChecked = server.WhiteListEnabled;
        OnlineModeBox.IsChecked = server.AllowOnlineMode;
        MaxMemBox.Text     = server.MaxMemoryMB.ToString();
        MinMemBox.Text     = server.MinMemoryMB.ToString();
        ServerDirBox.Text  = server.ServerDirectory ?? string.Empty;

        SelectComboItem(GamemodeBox,   server.Gamemode);
        SelectComboItem(DifficultyBox, server.Difficulty);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Navigation
    // ─────────────────────────────────────────────────────────────────────────

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag) return;

        _viewModel.DetailSection = tag;

        foreach (var b in _navButtons)
        {
            b.Theme = b.Name == btn.Name
                ? (this.TryFindResource("SideNavActiveButtonStyle", out var activeTheme) ? activeTheme as ControlTheme : null)
                : (this.TryFindResource("SideNavButtonStyle", out var inactiveTheme) ? inactiveTheme as ControlTheme : null);
        }

        if (tag == "Mods")
            _ = _viewModel.LoadModsAsync();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Save / Cancel
    // ─────────────────────────────────────────────────────────────────────────

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.Server.Name         = NameBox.Text ?? string.Empty;
        _viewModel.Server.Motd         = MotdBox.Text ?? string.Empty;
        _viewModel.Server.MaxPlayers   = int.TryParse(MaxPlayersBox.Text, out var mp) ? mp : _viewModel.Server.MaxPlayers;
        _viewModel.Server.AllowCheats      = CheatsBox.IsChecked == true;
        _viewModel.Server.WhiteListEnabled = WhitelistBox.IsChecked == true;
        _viewModel.Server.AllowOnlineMode  = OnlineModeBox.IsChecked == true;
        _viewModel.Server.MaxMemoryMB  = int.TryParse(MaxMemBox.Text,  out var maxm) ? maxm : _viewModel.Server.MaxMemoryMB;
        _viewModel.Server.MinMemoryMB  = int.TryParse(MinMemBox.Text,  out var minm) ? minm : _viewModel.Server.MinMemoryMB;

        if (GamemodeBox.SelectedItem is ComboBoxItem gm)
            _viewModel.Server.Gamemode = gm.Content?.ToString() ?? _viewModel.Server.Gamemode;
        if (DifficultyBox.SelectedItem is ComboBoxItem diff)
            _viewModel.Server.Difficulty = diff.Content?.ToString() ?? _viewModel.Server.Difficulty;

        await _viewModel.SaveAsync();
        Close(true);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Close(false);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Mods tab — mirrors InstallationSettingsWindow exactly
    // ─────────────────────────────────────────────────────────────────────────

    private async void BrowseMods_Click(object sender, RoutedEventArgs e)
    {
        var win = new ServerPluginBrowserWindow(_viewModel.Server);
        await win.ShowDialog(this);
        await _viewModel.LoadModsAsync();
    }

    private async void CheckModUpdates_Click(object sender, RoutedEventArgs e)
        => await _viewModel.CheckModUpdatesAsync();

    private async void CheckDependencies_Click(object sender, RoutedEventArgs e)
        => await _viewModel.CheckDependenciesAsync();

    private void OpenModsFolder_Click(object sender, RoutedEventArgs e)
        => _viewModel.OpenModsFolder();

    private async void FixDependencyIssue_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is DependencyIssue issue)
            await _viewModel.FixDependencyIssueAsync(issue);
    }

    private async void FixAllDependencyIssues_Click(object sender, RoutedEventArgs e)
    {
        foreach (var issue in _viewModel.DependencyIssues.ToList())
            await _viewModel.FixDependencyIssueAsync(issue);
    }

    private async void ModToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Avalonia.Controls.Primitives.ToggleButton tb
            && tb.DataContext is InstalledModRowViewModel rowVm)
            await rowVm.ToggleEnabledAsync();
    }

    private async void ChangeVersion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.DataContext is not InstalledModRowViewModel rowVm) return;

        if (string.IsNullOrWhiteSpace(rowVm.Mod?.ModrinthId))
        {
            _ = SimpleDialog.InfoAsync(this, "This mod was not installed from Modrinth — version history is unavailable.",
                "Version picker");
            return;
        }

        var picker = new ModVersionPickerWindow(rowVm);
        await picker.ShowDialog(this);
        await _viewModel.LoadModsAsync();
    }

    private async void UpdateModRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is InstalledModRowViewModel row)
            await _viewModel.UpdateModRowAsync(row);
    }

    private void ModLink_Click(object sender, PointerPressedEventArgs e)
    {
        var id = ((sender as Control)?.DataContext as InstalledModRowViewModel)?.Mod?.ModrinthId;
        if (!string.IsNullOrWhiteSpace(id))
        {
            try { System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(
                    $"https://modrinth.com/mod/{id}") { UseShellExecute = true }); }
            catch { }
        }
    }

    private async void UninstallMod_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is Mod mod)
            await _viewModel.UninstallModAsync(mod);
    }

    // Drag & drop
    private void ModsDropZone_DragOver(object sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(Avalonia.Input.DataFormats.Files)
            ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void ModsDropZone_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.Contains(Avalonia.Input.DataFormats.Files)) return;
        var files = e.Data.GetFiles()?.Select(f => f.TryGetLocalPath() ?? f.Path.LocalPath).Where(p => p != null).Select(p => p!).ToArray();
        if (files == null) return;

        var folder = _viewModel.GetServerPluginFolder();
        if (string.IsNullOrWhiteSpace(folder)) return;
        Directory.CreateDirectory(folder);

        foreach (var file in files.Where(f => f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)))
        {
            var dest = Path.Combine(folder, Path.GetFileName(file));
            try { File.Copy(file, dest, overwrite: true); }
            catch { }
        }

        await _viewModel.LoadModsAsync();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Advanced
    // ─────────────────────────────────────────────────────────────────────────

    private void OpenServerFolder_Click(object sender, RoutedEventArgs e)
    {
        var dir = _viewModel.Server.ServerDirectory;
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return;
        try { System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo { FileName = dir, UseShellExecute = true }); }
        catch { try { System.Diagnostics.Process.Start("explorer.exe", dir); } catch { } }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static void SelectComboItem(ComboBox box, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        foreach (var obj in box.Items)
        {
            if (obj is not ComboBoxItem item) continue;
            if (item.Content?.ToString()?.Equals(value, StringComparison.OrdinalIgnoreCase) == true)
            {
                box.SelectedItem = item;
                return;
            }
        }
    }
}
