# External API Integrations

## Mojang APIs

| Endpoint | Usage |
|---|---|
| `https://launchermeta.mojang.com/mc/game/version_manifest_v2.json` | All Minecraft release and snapshot versions |
| `https://piston-meta.mojang.com/v1/packages/...` | Version-specific metadata (JAR URL, asset index, libraries) |
| `https://resources.download.minecraft.net/` | Asset downloads (sounds, textures) |
| `https://libraries.minecraft.net/` | Library JAR downloads |

---

## Modrinth REST API v2

Base URL: `https://api.modrinth.com/v2/`

No API key required for read operations. Rate limit: 300 req/min.

| Endpoint | Usage |
|---|---|
| `GET /search` | Search mods, resource packs, shaders, worlds |
| `GET /project/{id}` | Project metadata |
| `GET /project/{id}/version` | All versions with loader/game-version filters |
| `GET /version/{id}` | Single version with download URLs and dependencies |
| `POST /version_files` | Batch SHA-1 fingerprint lookup for installed mods |
| `POST /version_files/update` | Batch update check — returns newer versions if available |

---

## CurseForge (Nexora Proxy)

CurseForge requires an API key that cannot be distributed in client apps. All CurseForge requests go through a Nexora-hosted proxy at `nexoragames.nl/api/curseforge-proxy/`. The launcher never holds the CurseForge API key.

---

## Microsoft OAuth 2.0 — Device Code Flow

| Endpoint | Purpose |
|---|---|
| `POST /consumers/oauth2/v2.0/devicecode` | Request device code and user code |
| `POST /consumers/oauth2/v2.0/token` | Poll for access/refresh token |

Scope: `XboxLive.signin offline_access`

---

## Xbox Live + XSTS

| Endpoint | Purpose |
|---|---|
| `POST https://user.auth.xboxlive.com/user/authenticate` | Exchange MS access token for XBL token |
| `POST https://xsts.auth.xboxlive.com/xsts/authorize` | Exchange XBL for XSTS token |

---

## Minecraft Services

| Endpoint | Purpose |
|---|---|
| `POST https://api.minecraftservices.com/authentication/login_with_xbox` | Exchange XSTS for Minecraft access token |
| `GET https://api.minecraftservices.com/minecraft/profile` | Get UUID and username |

---

## Nexora Launcher API

Base URL: `https://nexoragames.nl/api/launcher/`

See [Nexora API Reference](Nexora-API-Reference) for full endpoint documentation.

---

## OpenAI-compatible Chat Completions

```
POST <endpoint>/chat/completions
Authorization: Bearer <api_key>
Content-Type: application/json

{ "model": "...", "stream": true, "messages": [...] }
```

Default endpoint: `https://api.openai.com/v1`. Configurable to any compatible endpoint.

---

## Google Gemini

| Endpoint | Purpose |
|---|---|
| `POST /v1beta/models/{model}:generateContent?key={key}` | Non-streaming completion |
| `POST /v1beta/models/{model}:streamGenerateContent?alt=sse&key={key}` | SSE streaming |
| `GET /v1beta/models?key={key}&pageSize=100` | List available models |

---

## Mod Loader APIs

| Loader | Endpoint |
|---|---|
| Fabric loader versions | `https://meta.fabricmc.net/v2/versions/loader/{mcVersion}` |
| Quilt loader versions | `https://meta.quiltmc.org/v3/versions/loader/{mcVersion}` |
| Forge versions | Maven at `https://files.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml` |
| NeoForge versions | Maven at `https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml` |

---

## Server Jar Downloads

| Server type | API |
|---|---|
| Paper | `https://api.papermc.io/v2/projects/paper/versions/{version}/builds` |
| Purpur | `https://api.purpurmc.org/v2/purpur/{version}/latest/download` |
| Fabric server | Fabric installer JAR from `https://maven.fabricmc.net/` |
| Forge installer | Forge Maven |
| NeoForge installer | NeoForge Maven |

---

## Adoptium (Java Downloads)

```
GET https://api.adoptium.net/v3/assets/latest/{featureVersion}/hotspot
    ?os=windows&arch=x64&image_type=jre
```

Used to download the correct JRE when none is detected on the system.

---

## Crafatar (Player Avatars)

```
GET https://crafatar.com/avatars/{uuid}?size=32&overlay=true
```

Used to display player head avatars on the Friends page.
