# Authentication and Security

> For the general app security overview (Microsoft OAuth, Minecraft account), see [Security and Authentication](Security-and-Authentication).
> This page focuses on the Nexora-specific authentication implementation.

---

## Sign-In Flow

```
1. User enters email/username + password in NexoraLoginWindow
2. NexoraAccountService.SignInAsync()
3. POST /api/launcher/login.php
4a. No 2FA: { success: true, token, username, userId }
    → StoreAccountIfAuthenticated()
    → Account written to nexora_account.json
4b. 2FA required: { success: true, twoFactorRequired: true, method, challenge }
    → NexoraLoginWindow shows code prompt
5. POST /api/launcher/verify-2fa.php { challenge, code }
    → { success: true, token, username, userId }
    → StoreAccountIfAuthenticated()
```

The account is only stored when `TwoFactorRequired == false` AND `Token` is non-empty.

---

## Token Validation on Startup

```csharp
public async Task ValidateStoredTokenAsync()
{
    var account = LoadFromDisk(); // nexora_account.json
    if (account == null) return;

    var valid = await _api.ValidateTokenAsync(account.Token);
    if (!valid) { ClearAccount(); return; }

    Current = account;
    AccountChanged?.Invoke(this, EventArgs.Empty);
}
```

---

## NexoraAccount Model

```csharp
public class NexoraAccount
{
    public int            UserId        { get; set; }
    public string         Username      { get; set; }
    public string         Token         { get; set; }
    public MinecraftLink? MinecraftLink { get; set; }
}

public class MinecraftLink
{
    public string Uuid     { get; set; }
    public string Username { get; set; }
}
```

---

## Sign-Out

```csharp
public async Task SignOutAsync()
{
    if (Current == null) return;
    _ = _api.LogoutAsync(Current.Token);   // fire-and-forget, invalidates server-side
    Current = null;
    File.Delete(AppPaths.NexoraAccountFile);
    AccountChanged?.Invoke(this, EventArgs.Empty);
}
```

---

## Email vs Username Login

`LoginAsync` detects whether the input is an email or username by checking for `@`:

```csharp
object payload = input.Contains('@')
    ? new { email = input, password }
    : new { username = input, password };
```

---

## Minecraft Account Linking

```csharp
POST /api/launcher/link.php
{ "token": "...", "uuid": "...", "username": "..." }
```

The server validates:
- UUID: 32 hex characters after normalizing (strips dashes, lowercases)
- Username: 3–16 alphanumeric/underscore characters
- UUID not already linked to a different Nexora account

After linking, `GetProfileAsync()` refreshes the `MinecraftLink` in the local account file.

---

## AccountChanged Event

Fired on sign-in, sign-out, and token validation. All UI that depends on Nexora state subscribes to this event:

```csharp
event EventHandler? AccountChanged;
```
