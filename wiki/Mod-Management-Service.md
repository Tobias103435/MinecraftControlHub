# Mod Management Service

## Overview

`ModService` handles searching, installing, updating, and removing mods for both installations and servers. It integrates with `ModrinthApiClient` for Modrinth and uses a Nexora-hosted proxy for CurseForge.

---

## IModService

```csharp
public interface IModService
{
    Task<IReadOnlyList<Mod>> GetInstalledModsAsync(string targetId);
    Task<InstallResult> InstallModAsync(string targetId, string modNameOrId);
    Task RemoveModAsync(string targetId, string modId);
    Task<IReadOnlyList<ModUpdate>> CheckUpdatesAsync(string targetId);
    Task UpdateModAsync(string targetId, string modId);
    Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query, string loader, string gameVersion);
}
```

---

## Mod Model

```csharp
public class Mod
{
    public string  Id          { get; init; }
    public string  Name        { get; init; }
    public string  Version     { get; init; }
    public string  FileName    { get; init; }
    public string  Platform    { get; init; }  // "modrinth" | "curseforge" | "local"
    public string? ProjectId   { get; init; }  // null for local JARs
    public bool    IsEnabled   { get; init; }
}
```

---

## Installation Flow

```
InstallModAsync("Fabric Pack", "Sodium")
  → SearchAsync("Sodium", "Fabric", "1.21.1")
  → Pick best matching version (platform priority: Modrinth first)
  → Download JAR to instances/<id>/mods/
  → Resolve and install dependencies recursively
  → Write mods.json metadata
  → Fire ModsChanged event
```

---

## Dependency Resolution

When a mod is installed, its dependencies (listed in the Modrinth/CurseForge metadata) are checked against the already-installed mods. Missing dependencies are installed automatically. Conflicts (two mods requiring incompatible versions of the same dependency) are reported rather than resolved silently.

---

## Update Detection

`CheckUpdatesAsync` uses Modrinth's fingerprint endpoint for Modrinth mods:

```csharp
// Compute SHA-1 fingerprints of all installed mod JARs
var fingerprints = installedMods.Select(m => ComputeFingerprint(m.FilePath));

// POST to Modrinth /version_files/update with fingerprints
// Response lists mods that have newer versions
```

For CurseForge mods, the file ID is used to check for a newer version against the Nexora proxy.

---

## Enable / Disable

Mods can be disabled without being deleted. The launcher renames the file:
- Enabled: `sodium-0.6.0.jar`
- Disabled: `sodium-0.6.0.jar.disabled`

The game ignores `.disabled` files. Re-enabling renames it back.

---

## Platform Notes

- **Modrinth mods** are downloaded directly from Modrinth's CDN
- **CurseForge mods** are downloaded from CurseForge's CDN via the Nexora proxy
- **Local JARs** can be imported by dragging them into the Mods page — they are marked as `platform: "local"` and not checked for updates
