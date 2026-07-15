using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using MinecraftControlHub.Core.Models;
using MinecraftControlHub.Core.Services;

namespace MinecraftControlHub.UI.Windows;

public partial class ServerPreviewWindow : Window
{
    private readonly Server _server;
    private readonly IServerService? _serverService;
    private readonly IModService? _modService;
    private readonly IServerProvisioningService? _provisioningService;
    private readonly ObservableCollection<PluginFileRow> _pluginFiles = new();
    private readonly ObservableCollection<DependencyIssue> _pluginIssues = new();
    private readonly HealthCheckService? _healthCheckService;
    private readonly INexoraApiService? _nexoraApiService;
    private readonly INexoraAccountService? _nexoraAccountService;
    private readonly ObservableCollection<ServerFriendRow> _serverFriends = new();
    private System.Windows.Threading.DispatcherTimer? _healthTimer;
    private readonly List<double> _ramHistory = new();
    private const int RamHistoryMax = 40;

    private string _previousMinecraftVersion;

    public ServerPreviewWindow(Server server)
    {
        InitializeComponent();
        _server = server;
        _previousMinecraftVersion = server.MinecraftVersion;
        DataContext = server;
        var serviceProvider = (Application.Current as App)?.ServiceProvider;
        _serverService = serviceProvider?.GetService<IServerService>();
        _modService = serviceProvider?.GetService<IModService>();
        _provisioningService = serviceProvider?.GetService<IServerProvisioningService>();
        if (_serverService != null)
        {
            _serverService.ServerOutputReceived += OnServerOutputReceived;
        }
 
        PluginFilesItemsControl.ItemsSource = _pluginFiles;
        PluginIssuesItemsControl.ItemsSource = _pluginIssues;
 
        _healthCheckService = serviceProvider?.GetService<HealthCheckService>();
        _nexoraApiService = serviceProvider?.GetService<INexoraApiService>();
        _nexoraAccountService = serviceProvider?.GetService<INexoraAccountService>();
        ServerFriendsList.ItemsSource = _serverFriends;
        _healthTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _healthTimer.Tick += async (_, _) => await RefreshServerHealthAsync();
 
        SetActiveSection("Terminal");
        LoadTerminalHistory();
        LoadLogs();
        UpdateTerminalStartStopButton();

        Closed += (_, _) =>
        {
            if (_serverService != null)
                _serverService.ServerOutputReceived -= OnServerOutputReceived;
        };
    }

    /// <summary>
    /// Trigger an auto-sync of plugins/mods if the server's settings request it.
    /// Presently a thin wrapper around RefreshPluginSectionAsync to allow callers
    /// (like the plugin browser) to request a sync after making changes.
    /// </summary>
    public async Task TriggerAutoSyncIfEnabledAsync()
    {
        // For now always refresh plugin section to ensure UI is up to date.
        await RefreshPluginSectionAsync(runCompatibilityCheck: true);
    }

    private void NavRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton radio && radio.Tag is string sectionName)
        {
            SetActiveSection(sectionName);
        }
    }

    private void OpenServerFolder_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_server.ServerDirectory))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _server.ServerDirectory,
                UseShellExecute = true
            });
        }
        catch
        {
            try
            {
                Process.Start("explorer.exe", _server.ServerDirectory);
            }
            catch
            {
                // Ignore opening failures for now.
            }
        }
    }

    private void AdvancedTerminal_Click(object sender, RoutedEventArgs e)
    {
        SetActiveSection("Terminal");
    }

    private void AdvancedLogs_Click(object sender, RoutedEventArgs e)
    {
        SetActiveSection("Logs");
    }

    private void SetActiveSection(string sectionName)
    {
        // Guard: during InitializeComponent, named elements may not exist yet
        if (TerminalSection == null) return;

        TerminalSection.Visibility = Visibility.Collapsed;
        HealthSection.Visibility   = Visibility.Collapsed;
        FriendsSection.Visibility  = Visibility.Collapsed;
        SettingsSection.Visibility = Visibility.Collapsed;
        PluginsSection.Visibility  = Visibility.Collapsed;
        AdvancedSection.Visibility = Visibility.Collapsed;
        LogsSection.Visibility     = Visibility.Collapsed;

        switch (sectionName)
        {
            case "Friends":
                FriendsSection.Visibility = Visibility.Visible;
                _ = LoadServerFriendsAsync();
                break;
            case "Settings":
                SettingsSection.Visibility = Visibility.Visible;
                break;
            case "Plugins":
                PluginsSection.Visibility = Visibility.Visible;
                _ = RefreshPluginSectionAsync(runCompatibilityCheck: true);
                break;
            case "Advanced":
                AdvancedSection.Visibility = Visibility.Visible;
                break;
            case "Logs":
                LogsSection.Visibility = Visibility.Visible;
                break;
            case "Health":
                HealthSection.Visibility = Visibility.Visible;
                // start polling while the Health section is visible and the server is running
                if (_healthTimer != null)
                {
                    _ = RefreshServerHealthAsync();
                    _healthTimer.Start();
                }
                break;
            default:
                TerminalSection.Visibility = Visibility.Visible;
                break;
        }

        // stop timer when leaving Health section
        if (sectionName != "Health" && _healthTimer != null && _healthTimer.IsEnabled)
            _healthTimer.Stop();
    }

    private void UpdateRamSparkline()
    {
        if (ServerRamSparkline == null || ServerRamSparklineCanvas == null) return;

        var points = new System.Windows.Media.PointCollection();
        var width = ServerRamSparklineCanvas.ActualWidth;
        var height = ServerRamSparklineCanvas.ActualHeight;
        if (width <= 0) width = ServerRamSparklineCanvas.Width; // fallback
        if (height <= 0) height = ServerRamSparklineCanvas.Height;

        var count = Math.Max(1, _ramHistory.Count);
        for (var i = 0; i < _ramHistory.Count; i++)
        {
            var x = (i / (double)(RamHistoryMax - 1)) * width;
            var val = Math.Max(0.0, Math.Min(100.0, _ramHistory[i]));
            // invert Y (0% at bottom)
            var y = height - (val / 100.0 * height);
            points.Add(new System.Windows.Point(x, y));
        }

        // If there's only one point, add a zero at start so it's visible
        if (points.Count == 1)
        {
            points.Insert(0, new System.Windows.Point(0, height));
        }

        ServerRamSparkline.Points = points;
    }

    private void LoadLogs()
    {
        if (string.IsNullOrWhiteSpace(_server.ServerDirectory))
        {
            LogsTextBox.Text = "No server directory available yet.";
            return;
        }

        var candidates = new[]
        {
            Path.Combine(_server.ServerDirectory, "server.log"),
            Path.Combine(_server.ServerDirectory, "logs", "latest.log"),
            Path.Combine(_server.ServerDirectory, "logs", "server.log")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                try
                {
                    var content = File.ReadAllText(candidate);
                    LogsTextBox.Text = string.IsNullOrWhiteSpace(content) ? "No log entries found in this file." : content;
                    return;
                }
                catch
                {
                    LogsTextBox.Text = "Unable to read the log file.";
                    return;
                }
            }
        }

        LogsTextBox.Text = "No logs have been generated yet. Start the server to create log output.";
    }

    private void LoadTerminalHistory()
    {
        if (string.IsNullOrWhiteSpace(_server.ServerDirectory))
        {
            TerminalOutputTextBox.Text = "No server directory available yet.";
            return;
        }

        var candidates = new[]
        {
            Path.Combine(_server.ServerDirectory, "server.log"),
            Path.Combine(_server.ServerDirectory, "logs", "latest.log"),
            Path.Combine(_server.ServerDirectory, "logs", "server.log")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                try
                {
                    var content = File.ReadAllText(candidate);
                    TerminalOutputTextBox.Text = string.IsNullOrWhiteSpace(content) ? "No server output yet." : content;
                    TerminalOutputTextBox.ScrollToEnd();
                    return;
                }
                catch
                {
                    TerminalOutputTextBox.Text = "Unable to read the log file.";
                    return;
                }
            }
        }

        TerminalOutputTextBox.Text = "No server output yet. Start the server to see live console messages.";
    }

    private void AppendTerminalLine(string line, bool isError = false)
    {
        if (TerminalOutputTextBox == null)
            return;

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var prefix = isError ? "[ERR]" : "[OUT]";
            var formatted = $"[{timestamp}] {prefix} {line}";

            if (string.IsNullOrEmpty(TerminalOutputTextBox.Text))
                TerminalOutputTextBox.Text = formatted;
            else
                TerminalOutputTextBox.AppendText(Environment.NewLine + formatted);

            // Max 5000 lines to prevent memory issues
            var lines = TerminalOutputTextBox.Text.Split('\n');
            if (lines.Length > 5000)
            {
                TerminalOutputTextBox.Text = string.Join("\n", lines.Skip(lines.Length - 5000));
            }

            TerminalOutputTextBox.ScrollToEnd();
        }));
    }

    private void RefreshServerHealth_Click(object sender, RoutedEventArgs e)
        => _ = RefreshServerHealthAsync();

    private async Task RefreshServerHealthAsync()
    {
        await Task.Yield(); // ensure the caller doesn't block the UI thread
        try
        {
            // Java check
            var javaScore = 0; var javaAdvice = "";
            if (!string.IsNullOrWhiteSpace(_server.JavaPath))
            {
                if (File.Exists(_server.JavaPath))
                {
                    javaScore = 25; javaAdvice = $"✔ Java found at {_server.JavaPath}";
                }
                else
                {
                    javaScore = 0; javaAdvice = "✗ Java executable not found at configured path.";
                }
            }
            else
            {
                javaScore = 0; javaAdvice = "⚠ No Java path set for this server.";
            }

            // RAM check - parse GC logs in server logs directory
            var ramScore = 0; var ramAdvice = ""; double usagePercent = 0;
            var maxHeap = _server.MaxMemoryMB > 0 ? _server.MaxMemoryMB : 2048;
            try
            {
                var logDir = string.IsNullOrWhiteSpace(_server.ServerDirectory) ? null : Path.Combine(_server.ServerDirectory, "logs");
                var usageList = new List<long>();
                if (!string.IsNullOrWhiteSpace(logDir) && Directory.Exists(logDir))
                {
                    var gcLogs = Directory.GetFiles(logDir, "gc*.log").OrderByDescending(f => File.GetLastWriteTime(f)).ToList();
                    if (gcLogs.Count > 0)
                    {
                        var latest = gcLogs.First();
                        var lines = File.ReadAllLines(latest).TakeLast(1000).ToList();
                        var usedPattern = new System.Text.RegularExpressions.Regex("used\\s+(\\d+(?:,\\d+)?)\\s*([MK])?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        foreach (var l in lines)
                        {
                            var m = usedPattern.Match(l);
                            if (!m.Success) continue;
                            if (!long.TryParse(m.Groups[1].Value.Replace(",", ""), out var v)) continue;
                            var unit = (m.Groups[2].Value ?? "M").ToUpperInvariant();
                            var vMB = unit == "K" ? v / 1024 : v;
                            usageList.Add(vMB);
                        }
                    }
                }

                if (usageList.Count > 0)
                {
                    var avg = (long)usageList.Average();
                    var peak = usageList.Max();
                    usagePercent = maxHeap > 0 ? (avg * 100.0) / maxHeap : 0;
                    // classify
                    if (usagePercent >= 50 && usagePercent <= 85) { ramScore = 25; ramAdvice = $"✔ RAM usage healthy ({usagePercent:F1}% of {maxHeap} MB used)"; }
                    else if (usagePercent < 40) { ramScore = 15; ramAdvice = $"⚠ RAM underutilized ({usagePercent:F1}%). Consider reducing max heap."; }
                    else if (usagePercent > 85) { ramScore = 10; ramAdvice = $"✗ RAM overutilized ({usagePercent:F1}%). Increase max heap or reduce load."; }
                    else { ramScore = 20; ramAdvice = "⚠ RAM usage outside optimal range."; }
                }
                else
                {
                    ramScore = 0; ramAdvice = "⚠ No GC logs found to determine RAM usage.";
                }
            }
            catch (Exception ex)
            {
                ramScore = 0; ramAdvice = $"⚠ Could not analyze RAM: {ex.Message}";
            }

            // Mods check (basic): look for plugins/mods folder
            var modsScore = 25; var modsAdvice = "✔ No mods/plugins found.";
            try
            {
                var modsDir = string.IsNullOrWhiteSpace(_server.ServerDirectory) ? null : Path.Combine(_server.ServerDirectory, "mods");
                if (modsDir == null || !Directory.Exists(modsDir))
                {
                    // check plugins for Bukkit-like servers
                    var pluginsDir = string.IsNullOrWhiteSpace(_server.ServerDirectory) ? null : Path.Combine(_server.ServerDirectory, "plugins");
                    if (pluginsDir != null && Directory.Exists(pluginsDir))
                    {
                        var files = Directory.GetFiles(pluginsDir, "*.jar");
                        if (files.Length > 0) { modsScore = 20; modsAdvice = $"⚠ {files.Length} plugin(s) detected. Check compatibility."; }
                    }
                }
                else
                {
                    var files = Directory.GetFiles(modsDir, "*.jar");
                    if (files.Length > 0) { modsScore = 20; modsAdvice = $"⚠ {files.Length} mod(s) detected. Check compatibility."; }
                }
            }
            catch { modsScore = 15; modsAdvice = "⚠ Could not scan mods/plugins."; }

            // Stability check: look for crash logs in server logs directory
            var stabilityScore = 25; var stabilityAdvice = "✔ No recent crashes.";
            try
            {
                var logDir2 = string.IsNullOrWhiteSpace(_server.ServerDirectory) ? null : Path.Combine(_server.ServerDirectory, "logs");
                if (!string.IsNullOrWhiteSpace(logDir2) && Directory.Exists(logDir2))
                {
                    var crashes = Directory.GetFiles(logDir2, "*crash*.log").OrderByDescending(f => File.GetLastWriteTime(f)).ToList();
                    if (crashes.Count > 0)
                    {
                        var newest = crashes.First();
                        var age = DateTime.Now - File.GetLastWriteTime(newest);
                        if (age.TotalHours < 1) { stabilityScore = 8; stabilityAdvice = $"✗ Recent crash {age.TotalMinutes:F0}m ago."; }
                        else if (age.TotalHours < 24) { stabilityScore = 15; stabilityAdvice = $"⚠ Crash {age.TotalHours:F0}h ago."; }
                        else { stabilityScore = 20; stabilityAdvice = $"✔ Last crash {age.TotalDays:F0}d ago."; }
                    }
                }
            }
            catch { stabilityScore = 15; stabilityAdvice = "⚠ Could not check crash logs."; }

            var overall = (javaScore + ramScore + modsScore + stabilityScore) / 4;

            // Update history and UI on dispatcher
            if (_ramHistory.Count >= RamHistoryMax) _ramHistory.RemoveAt(0);
            _ramHistory.Add(usagePercent);

            await Dispatcher.InvokeAsync(() =>
            {
                ServerOverallScoreText.Text = overall.ToString();
                ServerOverallStatusText.Text = overall >= 75 ? "Healthy" : (overall >= 50 ? "Warning" : "Critical");
                ServerOverallAdviceText.Text = string.Join(" ", new[] { javaAdvice, ramAdvice, modsAdvice, stabilityAdvice }.Where(s => !string.IsNullOrWhiteSpace(s))); 

                ServerRamProgress.Value = usagePercent;
                ServerRamText.Text = $"{usagePercent:F1}% of {maxHeap}MB";
                ServerJavaText.Text = javaAdvice;
                ServerModsText.Text = modsAdvice;
                ServerStabilityText.Text = stabilityAdvice;

                UpdateRamSparkline();
            });
        }
        catch
        {
            // ignore
        }
    }
    private void OnServerOutputReceived(object? sender, ServerConsoleOutputEventArgs e)
    {
        if (e.ServerId != _server.Id)
            return;

        Dispatcher.BeginInvoke(new Action(() => AppendTerminalLine(e.Line, e.IsError)));
    }
 
    private void UpdateTerminalStartStopButton()
    {
        if (TerminalStartStopButton == null)
            return;
 
        switch (_server.Status)
        {
            case ServerStatus.Running:
                TerminalStartStopButton.Content = "Stop";
                TerminalStartStopButton.IsEnabled = true;
                break;
            case ServerStatus.Starting:
                TerminalStartStopButton.Content = "Starting...";
                TerminalStartStopButton.IsEnabled = false;
                break;
            case ServerStatus.Stopping:
                TerminalStartStopButton.Content = "Stopping...";
                TerminalStartStopButton.IsEnabled = false;
                break;
            default:
                TerminalStartStopButton.Content = "Start";
                TerminalStartStopButton.IsEnabled = true;
                break;
        }
    }
 
    private async void TerminalStartStop_Click(object sender, RoutedEventArgs e)
    {
        if (_serverService == null)
            return;
  
        if (_server.Status == ServerStatus.Running)
        {
            _server.Status = ServerStatus.Stopping;
            UpdateTerminalStartStopButton();
            await _serverService.StopServerAsync(_server.Id);
        }
        else
        {
            _server.Status = ServerStatus.Starting;
            UpdateTerminalStartStopButton();
            await _serverService.StartServerAsync(_server.Id);
        }
  
        UpdateTerminalStartStopButton();
    }

    private async void SaveServerSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_serverService == null)
            return;

        var oldVersion = _previousMinecraftVersion;
        var newVersion = _server.MinecraftVersion;

        try
        {
            await _serverService.UpdateServerAsync(_server);
            await _serverService.UpdateServerPropertiesAsync(_server);
            _previousMinecraftVersion = newVersion;
            MessageBox.Show("Server settings have been saved.", "Settings saved", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch
        {
            MessageBox.Show("Could not save server settings. Please try again.", "Save failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // If version changed, prompt mod/plugin compatibility check
        if (!string.Equals(oldVersion, newVersion, StringComparison.OrdinalIgnoreCase)
            && _modService != null
            && !string.IsNullOrWhiteSpace(_server.ServerDirectory))
        {
            await PromptPluginCompatibilityCheckAsync(oldVersion, newVersion);
        }
    }

    private void BrowseJavaPath_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Java executable",
            Filter = "Java executable (*.exe)|*.exe|All files (*.*)|*.*",
            FileName = _server.JavaPath
        };

        if (dialog.ShowDialog() != true)
            return;

        _server.JavaPath = dialog.FileName;
    }

    private void DetectJarFile_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_server.ServerDirectory) || !Directory.Exists(_server.ServerDirectory))
        {
            MessageBox.Show("Server directory is not available. Please save the server settings or create the server folder first.", "Cannot detect jar", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var jarFiles = Directory.GetFiles(_server.ServerDirectory, "*.jar", SearchOption.TopDirectoryOnly)
            .Where(file => !Path.GetFileName(file).Contains("installer", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(file => new FileInfo(file).Length)
            .Select(Path.GetFileName)
            .ToList();

        if (jarFiles.Count == 0)
        {
            MessageBox.Show("No jar files were found in the server folder.", "Detect jar", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _server.JarFileName = jarFiles[0];
        MessageBox.Show($"Selected '{jarFiles[0]}' as the preferred server jar.", "Detect jar", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void SaveAdvancedSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_serverService == null)
            return;

        if (!string.IsNullOrWhiteSpace(_server.JavaPath) && !File.Exists(_server.JavaPath))
        {
            MessageBox.Show("The selected Java path does not exist. Please choose a valid java.exe file.", "Invalid Java path", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            await _serverService.UpdateServerAsync(_server);
            await _serverService.UpdateServerPropertiesAsync(_server);
            if (_provisioningService != null)
            {
                await _provisioningService.WriteStartScriptsAsync(_server);
            }
            MessageBox.Show("Advanced server settings have been saved.", "Settings saved", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch
        {
            MessageBox.Show("Could not save advanced server settings. Please try again.", "Save failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void RegenerateStartScripts_Click(object sender, RoutedEventArgs e)
    {
        if (_provisioningService == null)
        {
            MessageBox.Show("Unable to regenerate start scripts because the provisioning service is unavailable.", "Action failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            await _provisioningService.WriteStartScriptsAsync(_server);
            MessageBox.Show("Start scripts have been regenerated.", "Done", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch
        {
            MessageBox.Show("Could not regenerate start scripts. Please try again.", "Action failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BrowseServerPlugins_Click(object sender, RoutedEventArgs e)
    {
        var window = new ServerPluginBrowserWindow(_server)
        {
            Owner = this
        };
        window.ShowDialog();
        _ = RefreshPluginSectionAsync(runCompatibilityCheck: false);
    }

    private async void BrowsePlugins_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_server.ServerDirectory))
        {
            MessageBox.Show("Server directory is not set. Cannot browse for plugins/mods.", "Unable to browse", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Select plugin or mod files",
            Filter = "Jar files (*.jar)|*.jar",
            Multiselect = true
        };

        if (dialog.ShowDialog() != true || dialog.FileNames.Length == 0)
            return;

        await CopyPluginFilesAsync(dialog.FileNames);
        await RefreshPluginSectionAsync(runCompatibilityCheck: true);
    }

    private async void CheckPluginCompatibility_Click(object sender, RoutedEventArgs e)
    {
        await RefreshPluginSectionAsync(runCompatibilityCheck: true);
    }

    private void OpenPluginFolder_Click(object sender, RoutedEventArgs e)
    {
        var targetFolder = GetServerPluginFolder();
        if (string.IsNullOrWhiteSpace(targetFolder))
            return;

        try
        {
            if (!Directory.Exists(targetFolder))
                Directory.CreateDirectory(targetFolder);

            Process.Start(new ProcessStartInfo
            {
                FileName = targetFolder,
                UseShellExecute = true
            });
        }
        catch
        {
            try
            {
                Process.Start("explorer.exe", targetFolder);
            }
            catch
            {
                // ignore
            }
        }
    }

    private void PluginDropZone_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void PluginDropZone_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return;

        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        if (files == null || files.Length == 0)
            return;

        await CopyPluginFilesAsync(files);
        await RefreshPluginSectionAsync(runCompatibilityCheck: true);
    }

    private async void RemovePluginFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not PluginFileRow row)
            return;

        try
        {
            if (!string.IsNullOrWhiteSpace(row.FilePath) && File.Exists(row.FilePath))
                File.Delete(row.FilePath);
        }
        catch
        {
            // ignore file delete errors for now
        }

        await RefreshPluginSectionAsync(runCompatibilityCheck: true);
    }

    private async void FixPluginDependencyIssue_Click(object sender, RoutedEventArgs e)
    {
        if (_modService == null)
            return;

        if (sender is not Button button || button.DataContext is not DependencyIssue issue)
            return;

        var installation = BuildServerInstallation();
        if (installation == null)
        {
            UpdatePluginStatus("Cannot fix dependency issues because server details are incomplete.", isError: true);
            return;
        }

        try
        {
            var fixedOne = await _modService.FixDependencyIssueAsync(installation, issue);
            UpdatePluginStatus(fixedOne
                ? "One dependency issue was fixed. Re-running compatibility checks..."
                : "Could not automatically fix that issue. Check the server's mod list and dependencies manually.",
                isError: !fixedOne);
        }
        catch (Exception ex)
        {
            UpdatePluginStatus($"Error while fixing issue: {ex.Message}", isError: true);
        }

        await RefreshPluginSectionAsync(runCompatibilityCheck: true);
    }

    private async void FixAllPluginDependencyIssues_Click(object sender, RoutedEventArgs e)
    {
        if (_modService == null)
            return;

        var installation = BuildServerInstallation();
        if (installation == null)
        {
            UpdatePluginStatus("Cannot fix dependency issues because server details are incomplete.", isError: true);
            return;
        }

        var fixedAny = false;
        foreach (var issue in _pluginIssues.ToList())
        {
            try
            {
                if (await _modService.FixDependencyIssueAsync(installation, issue))
                    fixedAny = true;
            }
            catch
            {
                // if one issue fails, continue trying the rest
            }
        }

        UpdatePluginStatus(fixedAny
            ? "One or more dependency issues were fixed. Re-running compatibility checks..."
            : "Could not fix any dependency issues automatically.",
            isError: !fixedAny);

        await RefreshPluginSectionAsync(runCompatibilityCheck: true);
    }

    private Task CopyPluginFilesAsync(IEnumerable<string> sourceFiles)
    {
        if (string.IsNullOrWhiteSpace(_server.ServerDirectory))
            return Task.CompletedTask;

        var targetFolder = GetServerPluginFolder();
        if (string.IsNullOrWhiteSpace(targetFolder))
            return Task.CompletedTask;

        Directory.CreateDirectory(targetFolder);
        foreach (var source in sourceFiles.Where(f => f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var dest = Path.Combine(targetFolder, Path.GetFileName(source));
                File.Copy(source, dest, overwrite: true);
            }
            catch (Exception ex)
            {
                _ = MessageBox.Show($"Could not add {Path.GetFileName(source)}: {ex.Message}", "Install failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        return Task.CompletedTask;
    }

    private async Task RefreshPluginSectionAsync(bool runCompatibilityCheck)
    {
        var pluginFolder = GetServerPluginFolder();
        PluginFolderInfoTextBlock.Text = string.IsNullOrWhiteSpace(pluginFolder)
            ? "Server directory not configured."
            : $"Install folder: {pluginFolder}";

        _pluginFiles.Clear();
        if (!string.IsNullOrWhiteSpace(pluginFolder))
        {
            Directory.CreateDirectory(pluginFolder);

            // Scan for both active (*.jar) and disabled (*.jar.disabled) files.
            // For Fabric/Forge/NeoForge/Quilt servers, GetServerPluginFolder returns
            // the mods/ directory; for Paper/Purpur it returns plugins/.
            var jarFiles = Directory.GetFiles(pluginFolder, "*.jar")
                .Concat(Directory.GetFiles(pluginFolder, "*.jar.disabled"))
                .OrderBy(Path.GetFileName);
            foreach (var file in jarFiles)
            {
                _pluginFiles.Add(new PluginFileRow(Path.GetFileName(file), file));
            }

            PluginEmptyHintBorder.Visibility = _pluginFiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            PluginHintTextBlock.Visibility = _pluginFiles.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        if (runCompatibilityCheck)
        {
            await RunCompatibilityCheckAsync();
        }
        else
        {
            PluginIssuesItemsControl.Visibility = Visibility.Collapsed;
            PluginFixAllButton.Visibility = Visibility.Collapsed;
            if (_pluginFiles.Count == 0)
                UpdatePluginStatus("Drop a .jar file or click Browse to install a plugin or mod.", isError: false);
        }
    }

    private async Task RunCompatibilityCheckAsync()
    {
        if (_modService == null)
        {
            UpdatePluginStatus("Mod service is not available. Cannot check compatibility.", isError: true);
            return;
        }

        if (!SupportsAutomaticDependencyChecks())
        {
            UpdatePluginStatus("Automatic compatibility checks are only available for Fabric/Quilt/Forge/NeoForge servers. Plugins for Paper/Purpur are installed into the plugins folder but cannot be validated automatically here.", isError: false);
            _pluginIssues.Clear();
            PluginIssuesItemsControl.Visibility = Visibility.Collapsed;
            PluginFixAllButton.Visibility = Visibility.Collapsed;
            return;
        }

        var installation = BuildServerInstallation();
        if (installation == null)
        {
            UpdatePluginStatus("Server information is incomplete. Cannot check compatibility.", isError: true);
            _pluginIssues.Clear();
            PluginIssuesItemsControl.Visibility = Visibility.Collapsed;
            PluginFixAllButton.Visibility = Visibility.Collapsed;
            return;
        }

        try
        {
            var result = await _modService.CheckDependencyCompatibilityAsync(installation);
            UpdatePluginStatus(result.Summary, isError: result.Issues.Count > 0);
            _pluginIssues.Clear();
            foreach (var issue in result.Issues)
                _pluginIssues.Add(issue);

            PluginIssuesItemsControl.Visibility = _pluginIssues.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            PluginFixAllButton.Visibility = _pluginIssues.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            UpdatePluginStatus($"Compatibility check failed: {ex.Message}", isError: true);
            _pluginIssues.Clear();
            PluginIssuesItemsControl.Visibility = Visibility.Collapsed;
            PluginFixAllButton.Visibility = Visibility.Collapsed;
        }
    }

    private bool SupportsAutomaticDependencyChecks()
    {
        return _server.Type is ServerType.Fabric or ServerType.Quilt or ServerType.Forge or ServerType.NeoForge;
    }

    private Installation? BuildServerInstallation()
    {
        if (string.IsNullOrWhiteSpace(_server.ServerDirectory) || string.IsNullOrWhiteSpace(_server.MinecraftVersion))
            return null;

        var loader = _server.Type switch
        {
            ServerType.Fabric => LoaderType.Fabric,
            ServerType.Quilt => LoaderType.Quilt,
            ServerType.Forge => LoaderType.Forge,
            ServerType.NeoForge => LoaderType.NeoForge,
            _ => LoaderType.Vanilla
        };

        return new Installation
        {
            Id = _server.Id,
            Name = _server.Name,
            MinecraftVersion = _server.MinecraftVersion,
            Loader = loader,
            GameDirectory = _server.ServerDirectory
        };
    }

    private string GetServerPluginFolder()
    {
        if (string.IsNullOrWhiteSpace(_server.ServerDirectory))
            return string.Empty;

        return _server.Type switch
        {
            ServerType.Paper or ServerType.Purpur => Path.Combine(_server.ServerDirectory, "plugins"),
            _ => Path.Combine(_server.ServerDirectory, "mods")
        };
    }

    private void UpdatePluginStatus(string message, bool isError)
    {
        if (PluginStatusTextBlock == null)
            return;

        PluginStatusTextBlock.Text = message;
        PluginStatusTextBlock.Foreground = isError
            ? (System.Windows.Media.Brush)FindResource("BrushDanger")
            : (System.Windows.Media.Brush)FindResource("BrushTextSecondary");
    }

    private class PluginFileRow
    {
        public PluginFileRow(string fileName, string filePath)
        {
            FileName = fileName;
            FilePath = filePath;
        }

        public string FileName { get; }
        public string FilePath { get; }
    }

    private async Task SendTerminalCommandAsync()
    {
        var command = TerminalInputTextBox?.Text?.Trim();
        if (string.IsNullOrEmpty(command) || _serverService == null)
            return;

        try
        {
            AppendTerminalLine($"> {command}");
            TerminalInputTextBox!.Text = string.Empty;
            await _serverService.SendServerCommandAsync(_server.Id, command);
        }
        catch (Exception ex)
        {
            AppendTerminalLine($"Failed to send command: {ex.Message}", isError: true);
        }
    }

    private async void SendTerminalCommand_Click(object sender, RoutedEventArgs e)
    {
        await SendTerminalCommandAsync();
    }

    private async void TerminalInputTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await SendTerminalCommandAsync();
        }
    }

    private void ClearTerminal_Click(object sender, RoutedEventArgs e)
    {
        TerminalOutputTextBox.Text = string.Empty;
    }

    // ── Friends / Whitelist ───────────────────────────────────────────────

    private async Task LoadServerFriendsAsync()
    {
        FriendsLoadingText.Visibility = Visibility.Visible;
        FriendsEmptyText.Visibility   = Visibility.Collapsed;
        ServerFriendsList.Visibility   = Visibility.Collapsed;

        var token = _nexoraAccountService?.Current?.Token;
        if (string.IsNullOrEmpty(token) || _nexoraApiService == null)
        {
            FriendsLoadingText.Visibility = Visibility.Collapsed;
            FriendsEmptyText.Visibility   = Visibility.Visible;
            FriendsEmptyText.Text         = "Log in to your Nexora account to manage your server whitelist.";
            return;
        }

        try
        {
            var result = await _nexoraApiService.GetFriendsAsync(token);
            FriendsLoadingText.Visibility = Visibility.Collapsed;

            if (!result.Success || result.Data == null || result.Data.Count == 0)
            {
                FriendsEmptyText.Text       = result.Success
                    ? "No friends found. Add friends from the Social page first."
                    : (result.Error ?? "Could not load friends.");
                FriendsEmptyText.Visibility = Visibility.Visible;
                return;
            }

            // Get current whitelisted player names
            var currentWhitelist = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(_server.WhitelistedPlayers))
            {
                foreach (var name in _server.WhitelistedPlayers.Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    currentWhitelist.Add(name.Trim());
            }

            _serverFriends.Clear();
            foreach (var friend in result.Data)
            {
                var isSelected = friend.MinecraftUsername != null
                    && currentWhitelist.Contains(friend.MinecraftUsername);
                _serverFriends.Add(new ServerFriendRow(
                    friend.WebsiteUsername,
                    friend.MinecraftUsername,
                    friend.Initial,
                    isSelected));
            }

            ServerFriendsList.Visibility = Visibility.Visible;
            UpdateWhitelistSelectionState();
        }
        catch (Exception ex)
        {
            FriendsLoadingText.Visibility = Visibility.Collapsed;
            FriendsEmptyText.Text       = $"Failed to load friends: {ex.Message}";
            FriendsEmptyText.Visibility = Visibility.Visible;
        }
    }

    private void FriendWhitelist_Changed(object sender, RoutedEventArgs e)
        => UpdateWhitelistSelectionState();

    private void WhitelistToggle_Changed(object sender, RoutedEventArgs e)
    {
        // No extra logic needed; binding handles it.
    }

    private void UpdateWhitelistSelectionState()
    {
        var selected = _serverFriends.Where(f => f.IsSelected).ToList();
        var count    = selected.Count;

        FriendsSelectedCountText.Text = count > 0 ? $"{count} whitelisted" : string.Empty;
    }

    private async void SaveWhitelist_Click(object sender, RoutedEventArgs e)
    {
        if (_serverService == null) return;

        // Build comma-separated list of Minecraft usernames from selected friends
        var mcNames = _serverFriends
            .Where(f => f.IsSelected && !string.IsNullOrWhiteSpace(f.MinecraftUsername))
            .Select(f => f.MinecraftUsername!)
            .ToList();

        _server.WhitelistedPlayers = string.Join(",", mcNames);

        // If whitelist is toggled off, clear it
        if (!_server.WhiteListEnabled)
            _server.WhitelistedPlayers = string.Empty;

        try
        {
            await _serverService.UpdateServerAsync(_server);
            await _serverService.UpdateServerPropertiesAsync(_server);
            WhitelistStatusText.Text       = $"\u2713 Whitelist saved with {mcNames.Count} player(s).";
            WhitelistStatusText.Foreground = (System.Windows.Media.Brush)FindResource("BrushSuccess");
        }
        catch (Exception ex)
        {
            WhitelistStatusText.Text       = $"Could not save whitelist: {ex.Message}";
            WhitelistStatusText.Foreground = (System.Windows.Media.Brush)FindResource("BrushDanger");
        }
    }

    private class ServerFriendRow : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _isSelected;

        public string  WebsiteUsername   { get; }
        public string? MinecraftUsername { get; }
        public string  Initial           { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        public ServerFriendRow(string websiteUsername, string? minecraftUsername, string initial, bool isSelected)
        {
            WebsiteUsername   = websiteUsername;
            MinecraftUsername = minecraftUsername;
            Initial           = initial;
            _isSelected       = isSelected;
        }
    }

    /// <summary>
    /// Prompts the user to run a mod/plugin compatibility check after the Minecraft version changed.
    /// Uses the ModCompatibilityWindow to show all results at once.
    /// </summary>
    private async Task PromptPluginCompatibilityCheckAsync(string oldVersion, string newVersion)
    {
        if (_modService == null || string.IsNullOrWhiteSpace(_server.ServerDirectory)) return;

        // Build a fake Installation to use with modService
        var loader = _server.Type switch
        {
            ServerType.Fabric   => Core.Models.LoaderType.Fabric,
            ServerType.Quilt    => Core.Models.LoaderType.Quilt,
            ServerType.Forge    => Core.Models.LoaderType.Forge,
            ServerType.NeoForge => Core.Models.LoaderType.NeoForge,
            _                   => Core.Models.LoaderType.Vanilla
        };

        var installation = new Core.Models.Installation
        {
            Id               = _server.Id,
            Name             = _server.Name,
            MinecraftVersion = newVersion,
            Loader           = loader,
            GameDirectory    = _server.ServerDirectory
        };

        var mods = await _modService.GetInstalledModsAsync(installation.Id);
        if (mods.Count == 0) return;

        var answer = MessageBox.Show(
            $"Minecraft version changed from {oldVersion} to {newVersion}.\n\n" +
            $"Would you like to check if your {mods.Count} plugin(s)/mod(s) are compatible?\n" +
            "Compatible ones will be auto-updated. For incompatible ones you can choose to disable or uninstall.",
            "Plugin/Mod Compatibility Check",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes) return;

        var window = new ModCompatibilityWindow(installation, _modService, oldVersion, newVersion)
        {
            Owner = this
        };

        var result = window.ShowDialog();

        if (result == true && window.Result.Applied)
        {
            foreach (var row in window.Result.Rows.Where(r => !r.IsCompatible))
            {
                switch (row.SelectedActionIndex)
                {
                    case 1: // Disable
                        if (row.Mod.IsEnabled)
                            await _modService.ToggleModEnabledAsync(installation, row.Mod.Id);
                        break;
                    case 2: // Uninstall
                        await _modService.UninstallModAsync(installation.Id, row.Mod.Id);
                        break;
                }
            }
        }

        // Refresh the plugins section
        await RefreshPluginSectionAsync(runCompatibilityCheck: false);
    }

}
