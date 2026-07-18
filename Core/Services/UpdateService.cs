using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Threading;
using MinecraftControlHub.Core.Models;

namespace MinecraftControlHub.Core.Services;

// Models for the new update.json format
public record UpdateManifest(
    string Version,
    int Build,
    bool Mandatory,
    string ReleaseDate,
    string[] ReleaseNotes,
    UpdateDownloads Downloads
);

public record UpdateDownloads(
    UpdateWindowsDownloads Windows,
    UpdateLinuxDownloads Linux,
    UpdateMacOSDownloads MacOS
);

public record UpdateWindowsDownloads(
    UpdateWindowsVariantDownloads Standalone,
    UpdateWindowsVariantDownloads Installer);

public record UpdateWindowsVariantDownloads(
    string X64,
    string X86);

public record UpdateLinuxDownloads(
    string X64Tar,
    string X64AppImage
);

public record UpdateMacOSDownloads(
    string Arm64,
    string Intel
);

// GitHub Release Models (for backwards compatibility)
public record GitHubRelease(
    string TagName,
    string Name,
    bool Prerelease,
    GitHubAsset[] Assets,
    string Body
);

public record GitHubAsset(
    string Name,
    string BrowserDownloadUrl,
    long Size
);

/// <summary>
/// Checks whether a newer version of Nexora Launcher is available by
/// checking either update.json or GitHub Releases.
/// </summary>
public class UpdateService : IUpdateService
{
    private readonly HttpClient _http;
    // Change this to your update.json URL (e.g., https://raw.githubusercontent.com/OWNER/REPO/main/update.json)
    private const string UpdateManifestUrl = "https://raw.githubusercontent.com/Tobias103435/MinecraftControlHub/main/update.json";
    // Change to your GitHub repo (owner/repo) for GitHub Releases fallback
    private const string GitHubRepo = "Tobias103435/MinecraftControlHub";
    private const string GitHubLatestReleaseUrl = $"https://api.github.com/repos/{GitHubRepo}/releases/latest";

    public string CurrentVersion { get; }
    public UpdateCheckResult? LastResult { get; private set; }

    public event EventHandler<UpdateCheckResult>? UpdateChecked;

    public UpdateService(HttpClient http)
    {
        _http = http;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("MinecraftControlHub-UpdateService/1.0");

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
            // First try to get update.json
            var json = await _http.GetStringAsync(UpdateManifestUrl);
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            if (manifest == null)
            {
                throw new InvalidOperationException("Failed to parse update manifest.");
            }

            // Get the correct download URL for the current platform
            string? downloadUrl = GetPlatformDownloadUrlFromManifest(manifest.Downloads);
            // Combine release notes into a single string
            string? releaseNotes = manifest.ReleaseNotes != null ? string.Join("\n", manifest.ReleaseNotes) : null;

            // Compare versions
            var isUpdateAvailable = IsVersionNewer(CurrentVersion, manifest.Version);

            result = new UpdateCheckResult(
                IsUpdateAvailable: isUpdateAvailable,
                LatestVersion: manifest.Version,
                CurrentVersion: CurrentVersion,
                DownloadUrl: downloadUrl,
                ReleaseNotes: releaseNotes);
        }
        catch
        {
            // If update.json fails, fall back to GitHub Releases
            try
            {
                var json = await _http.GetStringAsync(GitHubLatestReleaseUrl);
                var release = JsonSerializer.Deserialize<GitHubRelease>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                });

                if (release == null)
                {
                    throw new InvalidOperationException("Failed to parse GitHub release.");
                }

                // Get version from tag name (strip leading 'v' if present)
                string latestVersion = release.TagName.StartsWith('v') ? release.TagName.Substring(1) : release.TagName;
                string? downloadUrl = GetPlatformDownloadUrlFromGitHub(release.Assets);
                string? releaseNotes = release.Body;

                // Compare versions
                var isUpdateAvailable = IsVersionNewer(CurrentVersion, latestVersion);

                result = new UpdateCheckResult(
                    IsUpdateAvailable: isUpdateAvailable,
                    LatestVersion: latestVersion,
                    CurrentVersion: CurrentVersion,
                    DownloadUrl: downloadUrl,
                    ReleaseNotes: releaseNotes);
            }
            catch
            {
                // If both fail, treat as no update
                result = new UpdateCheckResult(
                    IsUpdateAvailable: false,
                    LatestVersion: CurrentVersion,
                    CurrentVersion: CurrentVersion,
                    DownloadUrl: null,
                    ReleaseNotes: null);
            }
        }

        LastResult = result;

        // Always fire on the UI thread so subscribers can update bindings safely
        Dispatcher.UIThread.Post(() => UpdateChecked?.Invoke(this, result));

        return result;
    }

    public void OpenDownloadPage()
    {
        var url = LastResult?.DownloadUrl ?? $"https://github.com/{GitHubRepo}/releases/latest";
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
        // Determine file name from download URL
        var uri = new Uri(LastResult.DownloadUrl);
        var fileName = Path.GetFileName(uri.LocalPath);
        if (string.IsNullOrEmpty(fileName))
        {
            // Fallback to default name
            fileName = "Nexora_Update.exe";
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

    private string? GetPlatformDownloadUrlFromManifest(UpdateDownloads downloads)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (RuntimeInformation.OSArchitecture == Architecture.X64)
            {
                // Prefer installer first, then standalone
                return downloads.Windows?.Installer?.X64 ?? downloads.Windows?.Standalone?.X64;
            }
            else if (RuntimeInformation.OSArchitecture == Architecture.X86)
            {
                return downloads.Windows?.Installer?.X86 ?? downloads.Windows?.Standalone?.X86;
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Prefer AppImage first
            return downloads.Linux?.X64AppImage ?? downloads.Linux?.X64Tar;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            if (RuntimeInformation.OSArchitecture == Architecture.Arm64)
            {
                return downloads.MacOS?.Arm64;
            }
            else
            {
                return downloads.MacOS?.Intel;
            }
        }

        return null;
    }

    private string? GetPlatformDownloadUrlFromGitHub(GitHubAsset[] assets)
    {
        // Match assets by name patterns
        foreach (var asset in assets)
        {
            var name = asset.Name.ToLowerInvariant();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (RuntimeInformation.OSArchitecture == Architecture.X64)
                {
                    if (name.Contains("setup") && name.Contains("x64")) return asset.BrowserDownloadUrl;
                    if (name.Contains("standalone") && name.Contains("x64")) return asset.BrowserDownloadUrl;
                    if (name.Contains("win-x64")) return asset.BrowserDownloadUrl;
                }
                else if (RuntimeInformation.OSArchitecture == Architecture.X86)
                {
                    if (name.Contains("setup") && name.Contains("x86")) return asset.BrowserDownloadUrl;
                    if (name.Contains("standalone") && name.Contains("x86")) return asset.BrowserDownloadUrl;
                    if (name.Contains("win-x86")) return asset.BrowserDownloadUrl;
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // Prefer AppImage first
                if (name.Contains("appimage")) return asset.BrowserDownloadUrl;
                if (name.Contains("linux-x64")) return asset.BrowserDownloadUrl;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                if (RuntimeInformation.OSArchitecture == Architecture.Arm64)
                {
                    if (name.Contains("arm64") || name.Contains("apple-silicon")) return asset.BrowserDownloadUrl;
                }
                else
                {
                    if (name.Contains("x64") && !name.Contains("arm64")) return asset.BrowserDownloadUrl;
                }
            }
        }

        // If no match found, return the first asset as fallback
        return assets.Length > 0 ? assets[0].BrowserDownloadUrl : null;
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
