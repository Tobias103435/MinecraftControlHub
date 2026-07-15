# Account Management Service

## Overview

Two account services handle authentication:
- `MinecraftAccountService` — Microsoft/Xbox/Minecraft authentication via OAuth 2.0 device code flow
- `NexoraAccountService` — Nexora platform login with optional 2FA

---

## MinecraftAccountService

### IMinecraftAccountService

```csharp
public interface IMinecraftAccountService
{
    Task<DeviceCodeResponse> StartDeviceCodeFlowAsync();
    Task<MinecraftAccount?> PollForTokenAsync(DeviceCodeResponse deviceCode);
    Task<MinecraftAccount?> LoadStoredAccountAsync();
    Task<MinecraftAccount?> RefreshTokenAsync(MinecraftAccount account);
    Task SignOutAsync();
    MinecraftAccount? Current { get; }
}
```

### Device Code Flow

```
1. StartDeviceCodeFlowAsync()
   → POST https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode
   → Returns: user_code, verification_uri, device_code, expires_in, interval

2. UI shows user_code and opens verification_uri in browser

3. PollForTokenAsync() polls every <interval> seconds
   → POST https://login.microsoftonline.com/consumers/oauth2/v2.0/token
   → Returns: access_token, refresh_token when user completes authorization

4. Exchange for XBL token, then XSTS, then Minecraft token
5. GET Minecraft profile → uuid + username
6. Store to minecraft_account.json
```

### MinecraftAccount Model

```csharp
public class MinecraftAccount
{
    public string Uuid            { get; init; }
    public string Username        { get; init; }
    public string AccessToken     { get; init; }
    public string RefreshToken    { get; init; }
    public DateTime ExpiresAt     { get; init; }
}
```

---

## NexoraAccountService

See [Authentication and Security](Nexora-Authentication-and-Security) for the full sign-in flow.

### INexoraAccountService

```csharp
public interface INexoraAccountService
{
    NexoraAccount? Current { get; }
    Task SignInAsync(string emailOrUsername, string password);
    Task<TwoFactorResult> Verify2FAAsync(string challenge, string code);
    Task SignOutAsync();
    Task ValidateStoredTokenAsync();
    Task<NexoraAccount> GetProfileAsync();
    Task LinkMinecraftAccountAsync(MinecraftAccount minecraft);
    event EventHandler? AccountChanged;
}
```

### AccountChanged Event

Fires when:
- Sign-in succeeds (including after 2FA verification)
- Sign-out occurs
- `ValidateStoredTokenAsync` restores or clears the account

ViewModels subscribe to this event to show/hide Nexora-dependent UI elements.
