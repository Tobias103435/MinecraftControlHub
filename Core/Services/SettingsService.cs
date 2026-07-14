using System.IO;
using System.Text.Json;
using MinecraftControlHub.Core.Models;

namespace MinecraftControlHub.Core.Services;

public interface ISettingsService
{
    /// <summary>The current, in-memory settings (never null).</summary>
    AppSettings Settings { get; }

    /// <summary>Convenience accessor — same as Settings, but callable by code that only has an interface reference.</summary>
    AppSettings GetSettings();

    /// <summary>Persists the current settings to disk.</summary>
    Task SaveAsync();
}

public class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public AppSettings Settings { get; }

    public SettingsService()
    {
        Settings = Load();
    }

    public AppSettings GetSettings() => Settings;

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                var json     = File.ReadAllText(AppPaths.SettingsFile);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (settings != null) return settings;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Settings load failed: {ex.Message}");
        }

        return new AppSettings();
    }

    public async Task SaveAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(Settings, JsonOptions);
            await File.WriteAllTextAsync(AppPaths.SettingsFile, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Settings save failed: {ex.Message}");
        }
    }
}
