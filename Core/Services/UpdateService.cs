using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Threading;
using MinecraftControlHub.Core.Models;

namespace MinecraftControlHub.Core.Services;

/// <summary>
/// Represents the structure of the update.json file.
/// </summary>
public record UpdateManifest(
    string Version,
    int Build,
    bool Mandatory,
    string ReleaseDate,
    UpdateDownloads Downloads,
    string[] ReleaseNotes);

public record UpdateDownloads(
    string? Windows_x64,
    string? Windows_x86,
    string? Linux_x64,
    string? Linux_AppImage,
    string? Mac_arm64,
    string? Mac_intel);

/// <summary>
/// Checks whether a newer version of Nexora Launcher is available by
/// checking the update.json file and comparing semantic versions.
/// </summary>
public class UpdateService : IUpdateService
{
    private readonly HttpClient _http;
    // Update this URL to your actual GitHub raw URL or your own server!
    private const string UpdateManifestUrl = "https://raw.githubusercontent.com/Tobias103435/MinecraftControlHub/main/update.json";

    public string CurrentVersion { get; }
    public UpdateCheckResult? LastResult { get; private set; }

    public event EventHandler<UpdateCheckResult>? UpdateChecked;

    public UpdateService(HttpClient http)
    {
        _http = http;

        // Read the version from assembly metadata (set via <Version> in .csproj)
        var ver = Assembly.GetEntryAssembly()?.GetName().Version;
        CurrentVersion = ver is null
            ? "1.0.0"
            : $"{ver.Major}.{ver.Minor}.{ver.Build}";
    }

    public async Task<UpdateCheckResult> CheckAsync()
    {
        UpdateCheckResult result;
        try
        {
            var json = await _http.GetStringAsync(UpdateManifestUrl);
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            });

            if (manifest == null)
            {
                throw new InvalidOperationException("Failed to parse update manifest.");
            }

            // Get the correct download URL for the current platform
            string? downloadUrl = GetPlatformDownloadUrl(manifest.Downloads);

            // Compare versions
            var isUpdateAvailable = IsVersionNewer(CurrentVersion, manifest.Version);

            // Join release notes with newlines
            string? releaseNotes = manifest.ReleaseNotes != null && manifest.ReleaseNotes.Length > 0 
                ? string.Join(Environment.NewLine, manifest.ReleaseNotes) 
                : null;

            result = new UpdateCheckResult(
                IsUpdateAvailable: isUpdateAvailable,
                LatestVersion: manifest.Version,
                CurrentVersion: CurrentVersion,
                DownloadUrl: downloadUrl,
                ReleaseNotes: releaseNotes);
        }
        catch
        {
            // If anything fails (network error, invalid JSON, etc.), treat as no update
            result = new UpdateCheckResult(
                IsUpdateAvailable: false,
                LatestVersion: CurrentVersion,
                CurrentVersion: CurrentVersion,
                DownloadUrl: null,
                ReleaseNotes: null);
        }

        LastResult = result;

        // Always fire on the UI thread so subscribers can update bindings safely
        Dispatcher.UIThread.Post(() => UpdateChecked?.Invoke(this, result));

        return result;
    }

    public void OpenDownloadPage()
    {
        var url = LastResult?.DownloadUrl ?? "https://nexoragames.nl/launcher";
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { /* best effort */ }
    }

    public async Task DownloadAndStartUpdateAsync(IProgress<double>? progress = null)
    {
        if (LastResult?.DownloadUrl == null)
        {
            throw new InvalidOperationException("No download URL available.");
        }

        // Download the file to temp
        var tempPath = Path.GetTempPath();
        // Determine file extension from download URL
        var uri = new Uri(LastResult.DownloadUrl);
        var fileName = Path.GetFileName(uri.LocalPath);
        // Fallback to default names if we can't get the filename
        if (string.IsNullOrEmpty(fileName))
        {
            fileName = "Nexora_Update.exe"; // Default for Windows
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                fileName = "Nexora_Update.AppImage";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                fileName = "Nexora_Update";
            }
        }

        var installerPath = Path.Combine(tempPath, fileName);

        // Download with progress
        using var response = await _http.GetAsync(LastResult.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? 0;
        var readBytes = 0L;

        await using var fileStream = File.Create(installerPath);
        await using var contentStream = await response.Content.ReadAsStreamAsync();
        var buffer = new byte[8192];
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
            readBytes += bytesRead;

            if (totalBytes > 0)
            {
                progress?.Report((double)readBytes / totalBytes * 100);
            }
        }

        // On Windows, set executable (though it's .exe so it's fine)
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // Make executable on Unix-like systems
            File.SetUnixFileMode(installerPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        // Start the installer and close current app
        StartInstallerAndClose(installerPath);
    }

    private string? GetPlatformDownloadUrl(UpdateDownloads downloads)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return RuntimeInformation.OSArchitecture == Architecture.X64
                ? downloads.Windows_x64
                : downloads.Windows_x86;
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Prefer AppImage first, then x64
            return downloads.Linux_AppImage ?? downloads.Linux_x64;
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return RuntimeInformation.OSArchitecture == Architecture.Arm64
                ? downloads.Mac_arm64
                : downloads.Mac_intel;
        }
        return null;
    }

    private bool IsVersionNewer(string currentVersion, string latestVersion)
    {
        if (!Version.TryParse(currentVersion, out var current) ||
            !Version.TryParse(latestVersion, out var latest))
        {
            return false;
        }
        return latest > current;
    }

    private void StartInstallerAndClose(string installerPath)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = installerPath,
                UseShellExecute = true
            };

            // For Inno Setup on Windows, use /VERYSILENT for unattended install
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                startInfo.Arguments = "/VERYSILENT";
            }

            Process.Start(startInfo);

            // Close the current application
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to start installer: {ex.Message}", ex);
        }
    }
}
