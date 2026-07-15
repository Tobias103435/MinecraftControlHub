# Content Browsing and Discovery

## Overview

The Content page lets users search and install resource packs, shader packs, and world saves directly from Modrinth — the same search experience as the Mods page, but scoped to content rather than code mods.

---

## Search

The search bar queries `ModrinthApiClient.SearchAsync()` with the appropriate `project_type` facet:

```csharp
var results = await _modrinth.SearchAsync(
    query: userInput,
    facets: new[]
    {
        $"[\"project_type:{projectType}\"]",      // resourcepack | shader | world
        $"[\"game_version:{gameVersion}\"]"
    }
);
```

Results show the project name, description, download count, and a thumbnail image.

---

## Install Flow

Clicking **Install** on a search result:

1. Fetches the latest compatible version from Modrinth
2. Downloads the ZIP file
3. Copies it to the appropriate subfolder of the selected installation
4. Refreshes the installed items list

---

## Shader Dependencies

When installing a shader pack, the service checks whether the installation has a shader loader installed:

- **Fabric / Quilt** — requires Iris Shaders
- **Forge** — requires Oculus

If the required mod is not installed, the user is prompted to install it alongside the shader pack.

---

## Version Filtering

Search results are filtered to only show versions compatible with the selected installation's Minecraft version. Incompatible versions are hidden from the version picker.

---

## Local Import

In addition to Modrinth search, users can install content by:
- Dragging a `.zip` file into the Content page
- Clicking **Import from File** and selecting a `.zip`

The file is copied directly to the correct subfolder without any version validation.
