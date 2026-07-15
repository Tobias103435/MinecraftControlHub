# External API Integration Patterns

## Overview

The app integrates with multiple external APIs. All HTTP clients are managed by `IHttpClientFactory` (via `AddHttpClient`) to handle connection pooling correctly. Every integration follows the same pattern: typed HTTP client → interface → DI registration.

---

## Common Patterns

### Typed HTTP Clients

```csharp
// Registration
services.AddHttpClient<INexoraApiService, NexoraApiService>();

// Usage — HttpClient is injected by the factory
public class NexoraApiService : INexoraApiService
{
    private readonly HttpClient _http;
    public NexoraApiService(HttpClient http) => _http = http;
}
```

### Error Handling

All API calls use a `EnsureSuccessAsync` helper that reads the response body before throwing, so error messages surface the actual API error rather than a generic HTTP status:

```csharp
private static async Task EnsureSuccessAsync(HttpResponseMessage response)
{
    if (response.IsSuccessStatusCode) return;
    var body = await response.Content.ReadAsStringAsync();
    throw new HttpRequestException(
        $"API request failed ({(int)response.StatusCode}): {body}",
        statusCode: response.StatusCode);
}
```

### JSON Deserialization

Responses are deserialized with `PropertyNameCaseInsensitive = true` to handle both camelCase and snake_case API responses without custom converters.

---

## External APIs

### Mojang APIs

| Endpoint | Usage |
|---|---|
| `https://launchermeta.mojang.com/mc/game/version_manifest_v2.json` | All Minecraft version metadata |
| `https://resources.download.minecraft.net/` | Asset downloads (textures, sounds) |
| `https://libraries.minecraft.net/` | Library JAR downloads |
| `https://piston-meta.mojang.com/` | Version-specific asset index and JAR URLs |

### Modrinth REST API v2

Base URL: `https://api.modrinth.com/v2/`

| Endpoint | Usage |
|---|---|
| `GET /search` | Search mods with facets (loader, game version, category) |
| `GET /project/{id}` | Project metadata |
| `GET /project/{id}/version` | All versions for a project |
| `GET /version/{id}` | Single version with download URLs |
| `POST /version_files` | Batch fingerprint lookup for update detection |

No API key required for read-only access. Rate limits apply (300 req/min).

### CurseForge (via Nexora server-side proxy)

CurseForge requires an API key that must not be distributed in client apps. The launcher calls a Nexora-hosted proxy that forwards requests to CurseForge with the server-side key. The launcher never holds or transmits the CurseForge API key.

### Microsoft OAuth 2.0 (Device Code Flow)

| Endpoint | Usage |
|---|---|
| `https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode` | Request device code |
| `https://login.microsoftonline.com/consumers/oauth2/v2.0/token` | Poll for access token |

Scopes: `XboxLive.signin offline_access`

### Xbox Live + XSTS

| Endpoint | Usage |
|---|---|
| `https://user.auth.xboxlive.com/user/authenticate` | Exchange MS access token for XBL token |
| `https://xsts.auth.xboxlive.com/xsts/authorize` | Exchange XBL token for XSTS token |

### Minecraft Services

| Endpoint | Usage |
|---|---|
| `https://api.minecraftservices.com/authentication/login_with_xbox` | Exchange XSTS for Minecraft access token |
| `https://api.minecraftservices.com/minecraft/profile` | Get UUID and username |

### OpenAI-compatible Chat Completions

```
POST <endpoint>/chat/completions
Authorization: Bearer <api_key>
{ "model": "...", "messages": [...], "stream": true/false }
```

Default endpoint: `https://api.openai.com/v1`. Configurable to any compatible endpoint (OpenRouter, local Ollama, etc.).

### Google Gemini

```
POST https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={api_key}
POST https://generativelanguage.googleapis.com/v1beta/models/{model}:streamGenerateContent?alt=sse&key={api_key}
```

Model discovery: `GET https://generativelanguage.googleapis.com/v1beta/models?key={api_key}`

### Mod Loader APIs

| Loader | Metadata source |
|---|---|
| Fabric | `https://meta.fabricmc.net/v2/versions/loader` |
| Quilt | `https://meta.quiltmc.org/v3/versions/loader` |
| Forge | Maven at `https://files.minecraftforge.net/net/minecraftforge/forge/` |
| NeoForge | Maven at `https://maven.neoforged.net/releases/net/neoforged/neoforge/` |

### Paper / Purpur

| Server type | API |
|---|---|
| Paper | `https://api.papermc.io/v2/projects/paper/versions/{version}/builds` |
| Purpur | `https://api.purpurmc.org/v2/purpur/{version}/latest` |

### Adoptium (Java downloads)

`https://api.adoptium.net/v3/assets/latest/{feature_version}/hotspot?os=windows&arch=x64&image_type=jre`

Used when the app needs to download a specific Java version that is not already installed.
