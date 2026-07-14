using MinecraftControlHub.Core.Models;
using MinecraftControlHub.Core.Services;

namespace MinecraftControlHub.UI.ViewModels;

/// <summary>
/// ViewModel for one content-type panel (ResourcePacks, ShaderPacks, or Worlds)
/// in the installation Settings window's Content tab.
/// </summary>
public class ContentTabViewModel : ViewModelBase
{
    private readonly IContentService _contentService;
    private readonly Guid _installationId;
    private readonly string _mcVersion;
    private readonly LoaderType _loader;

    private List<ContentItem> _installedItems = new();
    private bool _isLoading;
    private string _status = string.Empty;

    public ContentType ContentType { get; }
    public Guid InstallationId => _installationId;

    public string TabTitle => ContentType switch
    {
        ContentType.ResourcePack => "Resource Packs",
        ContentType.ShaderPack   => "Shader Packs",
        ContentType.World        => "Worlds",
        _ => "Content"
    };

    public string DropHintText => ContentType switch
    {
        ContentType.ResourcePack => "Drop .zip resource pack files here",
        ContentType.ShaderPack   => "Drop .zip shader pack files here",
        ContentType.World        => "Drop world folders or .zip files here",
        _ => "Drop files here"
    };

    public List<ContentItem> InstalledItems
    {
        get => _installedItems;
        set
        {
            if (SetProperty(ref _installedItems, value))
                OnPropertyChanged(nameof(HasItems));
        }
    }

    public bool HasItems => InstalledItems.Count > 0;

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public string Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
                OnPropertyChanged(nameof(HasStatus));
        }
    }

    public bool HasStatus => !string.IsNullOrEmpty(Status);

    public ContentTabViewModel(IContentService contentService, Guid installationId, string mcVersion, LoaderType loader, ContentType type)
    {
        _contentService = contentService;
        _installationId = installationId;
        _mcVersion      = mcVersion;
        _loader         = loader;
        ContentType     = type;
    }

    private Task? _pendingLoad;

    public Task LoadAsync()
    {
        // Guard against concurrent loads — if already loading, return the running task.
        if (_pendingLoad != null && !_pendingLoad.IsCompleted)
            return _pendingLoad;
        _pendingLoad = LoadInternalAsync();
        return _pendingLoad;
    }

    private async Task LoadInternalAsync()
    {
        IsLoading = true;
        Status    = string.Empty;
        try
        {
            System.Diagnostics.Debug.WriteLine($"[ContentTabViewModel] LoadInternalAsync start: type={ContentType}, installationId={_installationId}");
            InstalledItems = await _contentService.GetInstalledContentAsync(_installationId, ContentType);
            System.Diagnostics.Debug.WriteLine($"[ContentTabViewModel] LoadInternalAsync done: {InstalledItems.Count} items for type={ContentType}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ContentTabViewModel] LoadInternalAsync FAILED: type={ContentType} — {ex.GetType().Name}: {ex.Message}");
            Status = $"Error loading: {ex.Message}";
            InstalledItems = new();
        }
        finally { IsLoading = false; }
    }

    public async Task DeleteItemAsync(ContentItem item)
    {
        await _contentService.DeleteContentAsync(_installationId, item);
        await LoadAsync();
    }

    public async Task AddDroppedFileAsync(string sourcePath)
    {
        _contentService.AddFromDrop(_installationId, ContentType, sourcePath);
        await LoadAsync();
    }

    /// <summary>Toggles the item between enabled and disabled and refreshes.</summary>
    public async Task ToggleItemEnabledAsync(ContentItem item)
    {
        await _contentService.ToggleContentEnabledAsync(_installationId, item);
        await LoadAsync();
    }

    /// <summary>Fetches available versions from Modrinth for the given item (must have a ModrinthId).</summary>
    public Task<List<ContentVersionInfo>> GetVersionsAsync(ContentItem item)
    {
        if (string.IsNullOrWhiteSpace(item.ModrinthId)) return Task.FromResult(new List<ContentVersionInfo>());
        return _contentService.GetVersionsForContentAsync(item.ModrinthId, _mcVersion);
    }

    /// <summary>Downloads and installs a specific version of the item.</summary>
    public async Task<string> ChangeVersionAsync(ContentItem item, ContentVersionInfo version)
    {
        var result = await _contentService.ChangeContentVersionAsync(_installationId, item, version);
        await LoadAsync();
        return result.Success ? result.Summary : (result.Error ?? "Unknown error");
    }
}
