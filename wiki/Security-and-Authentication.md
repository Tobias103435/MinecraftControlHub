# Security and Authentication

## Overview

Authentication in MinecraftControlHub spans two separate account systems that can be linked:

- **Minecraft (Microsoft) account** — required to launch the game. Uses Microsoft OAuth 2.0 device code flow via Xbox Live.
- **Nexora account** — optional, enables social features (friends, sharing, notifications). Uses email/password login with optional TOTP or email-based 2FA.

---

## Microsoft / Minecraft Authentication

### Device Code Flow

The launcher uses the OAuth 2.0 device authorization grant, which avoids the need for a redirect URI:

```
1. POST https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode
   → Returns: device_code, user_code, verification_uri, expires_in

2. User visits verification_uri and enters user_code in their browser

3. Launcher polls:
   POST https://login.microsoftonline.com/consumers/oauth2/v2.0/token
   → Returns: access_token, refresh_token (when user completes authorization)

4. Authenticate with Xbox Live (XBL)
   POST https://user.auth.xboxlive.com/user/authenticate

5. Authorize with XSTS
   POST https://xsts.auth.xboxlive.com/xsts/authorize

6. Login to Minecraft Services
   POST https://api.minecraftservices.com/authentication/login_with_xbox

7. Get Minecraft profile
   GET https://api.minecraftservices.com/minecraft/profile
```

### Token Storage

Tokens are stored in `%LocalAppData%\MinecraftControlHub\minecraft_account.json`. The `refresh_token` is used to obtain new access tokens silently on startup, so the user only goes through the device code flow once.

---

## Nexora Account Authentication

### Sign-In Flow

```
POST /api/launcher/login.php
{ "email": "...", "password": "..." }
OR
{ "username": "...", "password": "..." }

→ Success (no 2FA):  { success: true, token, username, userId }
→ 2FA required:      { success: true, twoFactorRequired: true, method, challenge }

POST /api/launcher/verify-2fa.php
{ "challenge": "...", "code": "123456" }
→ { success: true, token, username, userId }
```

### Token Lifecycle

| Action | Endpoint | Result |
|---|---|---|
| Login | `POST login.php` | Issues launcher token |
| Verify 2FA | `POST verify-2fa.php` | Issues token after code check |
| Validate | `POST validate.php` | Confirms token is still valid |
| Logout | `POST logout.php` | Invalidates token server-side |

Tokens are stored in `nexora_account.json` and validated on each startup via `ValidateStoredTokenAsync()`.

### Required HTTP Headers

Every request from the launcher must include:

```
Accept: application/json
X-Launcher-Secret: mch-launcher-2026
User-Agent: MinecraftControlHub/1.0
```

`X-Launcher-Secret` is used by the PHP backend to identify trusted launcher clients (e.g. to bypass Cloudflare bot checks).

---

## 2FA

The server supports two 2FA methods:

- **TOTP** — RFC 6238 authenticator app codes (Google Authenticator, Authy, etc.)
- **Email OTP** — a one-time code sent to the account's email address

The `method` field in the login response indicates which was triggered. The `challenge` string from the login response must be sent back with the verification code. Challenges expire server-side.

---

## Minecraft Account Linking

After completing the Microsoft device code flow, the Minecraft UUID and username can be linked to the Nexora account:

```csharp
POST /api/launcher/link.php
{ "token": "...", "uuid": "...", "username": "..." }
```

The server validates:
- UUID must be 32 hex characters (dashes stripped)
- Username must be 3–16 alphanumeric/underscore characters
- UUID cannot already be linked to a different Nexora account

---

## Server-Side Security (Nexora Platform)

| Layer | Mechanism |
|---|---|
| Transport | HTTPS enforced; HSTS headers |
| Passwords | Peppered Argon2ID hashing |
| Rate limiting | Nginx rate zones + Redis-backed counters |
| Bot protection | Cloudflare + `X-Launcher-Secret` header |
| Sessions | Secure cookie flags; Redis-backed |
| 2FA | TOTP (RFC 6238) + email OTP |
| Passkeys | WebAuthn for web logins |
| Fail2Ban | IP bans after repeated 401/429 responses |

---

## Security Checklist

- All communication is over HTTPS — never log raw token values
- Only persist `NexoraAccount` when `TwoFactorRequired` is `false` and `Token` is non-empty
- Call `LogoutAsync` on sign-out to invalidate the server-side token
- Never share Microsoft access tokens with the Nexora backend
- Treat `settings.json` as sensitive — it contains AI API keys
