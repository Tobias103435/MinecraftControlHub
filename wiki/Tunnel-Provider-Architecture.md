# Tunnel Provider Architecture

## Overview

A tunnel provider is described by a `TunnelProvider` model. `TunnelProviderRegistry` holds a static list of all registered providers. This model-driven approach means adding a new provider only requires a new entry in the registry — no new classes.

---

## TunnelProvider Model

```csharp
public class TunnelProvider
{
    public string Id               { get; init; }  // e.g. "playit", "ngrok"
    public string DisplayName      { get; init; }  // e.g. "playit.gg"
    public string Description      { get; init; }
    public string? WebsiteUrl      { get; init; }
    public string Tier             { get; init; }  // "free", "freemium", "freemiumLimited"
    public string PriceLabel       { get; init; }  // "Free", "Free / Paid"
    public bool   SupportsTcp      { get; init; }
    public bool   SupportsUdp      { get; init; }
    public bool   RequiresAuth     { get; init; }
    public bool   RequiresInstall  { get; init; }
    public string? Limitation      { get; init; }  // e.g. "TCP only on free plan"
    public TunnelAddressSource AddressSource { get; init; }
    public string? LocalApiUrl     { get; init; }  // for LocalApi mode
    public string? LocalApiJsonPath { get; init; } // dot-path to address in JSON response
    public string? RemoteApiUrl    { get; init; }  // for RemoteApi mode
    public string? SecretKeyPath   { get; init; }  // for RemoteApi mode
    public string? StdoutRegex     { get; init; }  // for StdoutRegex mode
    public string? CommandTemplate { get; init; }  // {exe}, {port}, {protocol} tokens
}
```

---

## Address Source Enum

```csharp
public enum TunnelAddressSource
{
    StdoutRegex,  // Parse tunnel process stdout
    LocalApi,     // Poll local HTTP endpoint
    RemoteApi,    // Use remote REST API (e.g. playit.gg API)
    Manual        // User pastes address; no process launched
}
```

---

## Command Template

The `CommandTemplate` uses `{exe}`, `{port}`, and `{protocol}` tokens that `TunnelSession` substitutes at launch time:

```
ngrok:   "{exe} tcp {port}"
bore:    "{exe} local {port} --to bore.pub:7835"
serveo:  "ssh -R 0:localhost:{port} serveo.net"
frp:     "{exe} --server_addr {serverAddr} --token {token} tcp {port}"
```

---

## Recommendation Scoring

`TunnelProviderRegistry` includes a scoring method that ranks providers for a given server type. Factors:

- UDP support → +15 (critical for Bedrock)
- No auth required → +10
- Free tier → +5
- Has limitation → −5
- playit.gg bonus → **+20** (purpose-built for game servers)

playit.gg typically scores highest for Minecraft servers.

---

## Registry

```csharp
public static class TunnelProviderRegistry
{
    public static IReadOnlyList<TunnelProvider> All { get; } = new List<TunnelProvider>
    {
        PlayitFree, PlayitPro, Ngrok, NgrokPro, Bore, Serveo, Frp
    };

    public static TunnelProvider? GetById(string id)
        => All.FirstOrDefault(p => p.Id == id);
}
```
