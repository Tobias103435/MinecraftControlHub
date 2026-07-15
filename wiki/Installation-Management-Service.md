# Installation Management Service

## Overview

`InstallationService` manages Minecraft client installations — creating them, deleting them, migrating shared data to isolated directories, and importing from the official Minecraft launcher.

---

## IInstallationService

```csharp
public interface IInstallationService
{
    Task<IReadOnlyList<Installation>> GetAllInstallationsAsync();
    Task<Installation> CreateInstallationAsync(Installation installation);
    Task UpdateInstallationAsync(Installation installation);
    Task DeleteInstallationAsync(string id);
    Task<Installation?> FindByNameAsync(string name);
    event EventHandler? InstallationsChanged;
}
```

---

## Installation Model

```csharp
public class Installation
{
    public string  Id               { get; init; }  // GUID
    public string  Name             { get; set; }
    public string  MinecraftVersion { get; set; }
    public string  Loader           { get; set; }   // Vanilla | Fabric | Forge | NeoForge | Quilt
    public string? LoaderVersion    { get; set; }
    public string? JavaPath         { get; set; }   // null = auto-detect
    public int     MinRam           { get; set; }   // MB
    public int     MaxRam           { get; set; }   // MB
    public string? JvmArgs          { get; set; }
}
```

---

## Isolated Directories

Each installation gets its own directory under `%LocalAppData%\MinecraftControlHub\instances\<id>\`. This directory contains:

```
instances/<id>/
  mods/
  config/
  saves/
  resourcepacks/
  shaderpacks/
  logs/
```

The Minecraft version JARs, libraries, and asset indexes are shared in `minecraft/` (equivalent to `.minecraft` — one copy regardless of how many installations exist).

---

## Persistence

Installations are serialized to `instances/installations.json`. The service loads this file on startup and maintains an in-memory list. All mutations write the file synchronously after updating the in-memory list.

---

## InstallationsChanged Event

Every create, update, and delete fires `InstallationsChanged`. ViewModels subscribe:

```csharp
_installationService.InstallationsChanged += (_, _) =>
    App.Current.Dispatcher.InvokeAsync(async () => await LoadAsync());
```

---

## Import from Official Launcher

`ImportFromOfficialLauncherAsync()` reads the official launcher's `launcher_profiles.json` and creates a corresponding installation for each profile, pointing to the existing game data.

---

## Related Services

- `IMinecraftLauncherService` — builds JVM arguments and starts the game process for an installation
- `IJavaService` — detects Java and validates compatibility with the Minecraft version
- `ILoaderService` — fetches available loader versions for Fabric/Forge/NeoForge/Quilt
