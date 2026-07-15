# Content Management System

## Overview

`ContentService` manages non-mod content for Minecraft installations: resource packs, shader packs, and world saves. It handles listing, enabling/disabling, installing from Modrinth, and organizing files within each installation directory.

---

## Supported Content Types

| Type | Subfolder | File extensions |
|---|---|---|
| Resource packs | `resourcepacks/` | `.zip` |
| Shader packs | `shaderpacks/` | `.zip` |
| World saves | `saves/` | Directory |

---

## IContentService

```csharp
public interface IContentService
{
    Task<IReadOnlyList<ContentItem>> GetResourcePacksAsync(string installationId);
    Task<IReadOnlyList<ContentItem>> GetShaderPacksAsync(string installationId);
    Task<IReadOnlyList<WorldSave>> GetWorldSavesAsync(string installationId);
    Task InstallFromFileAsync(string installationId, ContentType type, string filePath);
    Task RemoveAsync(string installationId, string itemId);
    Task SetEnabledAsync(string installationId, string itemId, bool enabled);
}
```

---

## ContentItem Model

```csharp
public class ContentItem
{
    public string  Id          { get; init; }
    public string  Name        { get; init; }
    public string  FileName    { get; init; }
    public bool    IsEnabled   { get; init; }
    public string? ThumbnailPath { get; init; }
    public ContentType Type    { get; init; }
}
```

---

## Enable / Disable

Content items are enabled/disabled by renaming the file:
- Enabled: `OptiFine_1.21.1.zip`
- Disabled: `OptiFine_1.21.1.zip.disabled`

---

## Modrinth Integration

The Content page's search bar queries Modrinth with content-type-specific facets:

| Content type | Modrinth project type | Additional facet |
|---|---|---|
| Resource packs | `resourcepack` | None |
| Shader packs | `shader` | Platform: Iris/Oculus/Sodium |
| Worlds | `world` | None |

---

## Related Pages

- [Content Browsing and Discovery](Content-Browsing-and-Discovery)
- [Asset Management and Organization](Asset-Management-and-Organization)
- [Modpack Export and Import](Modpack-Export-and-Import)
