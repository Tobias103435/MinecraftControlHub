# Modpack Export and Import

## Overview

`ModpackExportImportService` handles exporting Minecraft installations as `.mrpack` files and importing them back. The `.mrpack` format is compatible with Prism Launcher, the Modrinth App, and MultiMC.

---

## .mrpack Format

A `.mrpack` file is a ZIP archive containing:
- `modrinth.index.json` — manifest with mod list, loader version, MC version
- `overrides/` — any files not available on Modrinth/CurseForge (local mods, custom configs)

```json
// modrinth.index.json
{
    "formatVersion": 1,
    "game": "minecraft",
    "versionId": "1.0.0",
    "name": "My Modpack",
    "dependencies": {
        "minecraft": "1.21.1",
        "fabric-loader": "0.16.0"
    },
    "files": [
        {
            "path": "mods/sodium-0.6.0+mc1.21.1.jar",
            "hashes": { "sha512": "..." },
            "downloads": ["https://cdn.modrinth.com/data/..."]
        }
    ]
}
```

---

## Export Flow

```
ExportInstallationAsync(installationId)
  1. Read installed mods list
  2. For each Modrinth mod: add to files[] with CDN URL and SHA-512 hash
  3. For each CurseForge mod: add to files[] with CurseForge CDN URL
  4. For each local JAR: copy to overrides/mods/ in the ZIP
  5. Copy custom config files to overrides/config/ if they differ from defaults
  6. Write modrinth.index.json
  7. Return .mrpack byte array
```

---

## Import Flow

```
ImportModpackAsync(mrpackBytes)
  1. Extract ZIP
  2. Parse modrinth.index.json
  3. Create new Installation with loader/version from dependencies
  4. Download each file from its CDN URL
  5. Verify SHA-512 hash
  6. Copy overrides/ files to the new installation directory
  7. Return the created Installation
```

---

## Sharing via Nexora

Two sharing modes are available from `ShareInstanceWindow`:

### Friend Sharing

```csharp
// Export → base64 → POST share-instance.php
var mrpackBytes  = await _modpack.ExportInstallationAsync(id);
var base64       = Convert.ToBase64String(mrpackBytes);
var result       = await _instanceShare.ShareWithFriendsAsync(token, recipients, base64, name);
```

Recipients receive a notification in their launcher and can import with one click.

### Share Code

```csharp
// Export → base64 → POST create-instance-code.php → 8-char code
var code = await _instanceShare.CreateShareCodeAsync(token, base64, name);
// code: "AB12CD34", valid for 7 days
```

Anyone with the code can redeem it via `GET redeem-instance-code.php`.
