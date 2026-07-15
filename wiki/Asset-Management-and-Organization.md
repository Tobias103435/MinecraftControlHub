# Asset Management and Organization

## Overview

Content items (resource packs, shader packs, worlds) are organized in per-installation subfolders. The launcher manages these files through `ContentService`, providing enable/disable, version switching, and cleanup.

---

## File Layout

```
instances/<installation-id>/
  resourcepacks/
    OptiFine_1.21.1.zip
    VanillaTweaks.zip.disabled      ← disabled by renaming
  shaderpacks/
    ComplementaryUnbound.zip
    BSL_v8.zip
  saves/
    My World/
      level.dat
      region/
```

---

## Enable / Disable

Disabling a content item renames the file with a `.disabled` suffix. Minecraft ignores `.disabled` files. Re-enabling reverses the rename.

```csharp
public async Task SetEnabledAsync(string installationId, string itemId, bool enabled)
{
    var item = await FindItemAsync(installationId, itemId);
    if (enabled && item.FileName.EndsWith(".disabled"))
        File.Move(item.FilePath, item.FilePath[..^".disabled".Length]);
    else if (!enabled && !item.FileName.EndsWith(".disabled"))
        File.Move(item.FilePath, item.FilePath + ".disabled");
}
```

---

## Version Switching

For content items that have multiple versions installed (e.g. two versions of the same shader pack), the launcher lets users switch between them:

1. All versions of the item are listed in the version picker
2. Selecting a version disables the current active version and enables the selected one
3. Only one version of an item can be active at a time

---

## Thumbnails

Resource packs include a `pack.png` inside the ZIP. The launcher extracts it on first load and caches it as a thumbnail. If no `pack.png` is found, a generic icon is shown.

---

## World Saves

World saves are directories, not ZIP files. The Content page lists each save folder under `saves/`:

- Shows the world name (from `level.dat`)
- Shows the last-played date
- Allows deletion (moves to a `.trash` subfolder before permanent deletion for safety)
