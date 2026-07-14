using MinecraftControlHub.Core.Models;
using MinecraftControlHub.Core.Services;

namespace MinecraftControlHub.UI.ViewModels;

/// <summary>
/// Tunnel-provider settings partial — live-bound text fields for exe paths
/// and API keys, plus a save action that flushes to AppSettings + disk.
/// </summary>
public partial class SettingsPageViewModel
{
    // -----------------------------------------------------------------------
    // Exe path properties (two-way, immediate — no separate save needed for paths)
    // -----------------------------------------------------------------------

    public string PlayitExePath
    {
        get => GetExePath("playit");
        set { SetExePath("playit", value); SetExePath("playit-premium", value); OnPropertyChanged(); }
    }

    public string NgrokExePath
    {
        get => GetExePath("ngrok");
        set { SetExePath("ngrok", value); SetExePath("ngrok-pro", value); OnPropertyChanged(); }
    }

    public string BoreExePath
    {
        get => GetExePath("bore");
        set { SetExePath("bore", value); OnPropertyChanged(); }
    }

    public string FrpExePath
    {
        get => GetExePath("frp");
        set { SetExePath("frp", value); OnPropertyChanged(); }
    }

    // -----------------------------------------------------------------------
    // API key properties — stored in memory only; flushed on SaveTunnelSettings
    // (PasswordBox doesn't support two-way binding so we use code-behind)
    // -----------------------------------------------------------------------

    private string _ngrokApiKey = string.Empty;
    private string _frpApiKey   = string.Empty;

    public string NgrokApiKey
    {
        get => _ngrokApiKey;
        set => _ngrokApiKey = value;
    }

    public string FrpApiKey
    {
        get => _frpApiKey;
        set => _frpApiKey = value;
    }

    // -----------------------------------------------------------------------
    // Save (called by the "Save tunnel settings" button)
    // -----------------------------------------------------------------------

    public void SaveTunnelSettings()
    {
        // Flush API keys to settings
        if (!string.IsNullOrWhiteSpace(_ngrokApiKey))
        {
            _settingsService.Settings.TunnelApiKeys["ngrok"]     = _ngrokApiKey;
            _settingsService.Settings.TunnelApiKeys["ngrok-pro"] = _ngrokApiKey;
        }

        if (!string.IsNullOrWhiteSpace(_frpApiKey))
            _settingsService.Settings.TunnelApiKeys["frp"] = _frpApiKey;

        _ = _settingsService.SaveAsync();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private string GetExePath(string providerId)
    {
        if (_settingsService.Settings.TunnelExePaths.TryGetValue(providerId, out var p))
            return p;
        return string.Empty;
    }

    private void SetExePath(string providerId, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            _settingsService.Settings.TunnelExePaths.Remove(providerId);
        else
            _settingsService.Settings.TunnelExePaths[providerId] = path;

        _ = _settingsService.SaveAsync();
    }
}
