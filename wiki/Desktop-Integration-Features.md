# Desktop Integration Features

## Overview

This page covers the WPF application's integration with the Nexora platform — specifically how the launcher handles account sign-in, shares content, and connects the social features to the UI.

---

## App Startup

`App.OnStartup()` runs these steps in order:

1. Build DI container
2. Apply saved theme (before window renders — no flicker)
3. Create and show `MainWindow`
4. Wire server crash handler → AI terminal auto-diagnosis

```csharp
serverService.ServerCrashed += (_, args) =>
{
    Dispatcher.InvokeAsync(() => _ = aiVm.AskAiAboutErrorAsync(args.CrashReport));
};
```

---

## MainWindow — Nexora Login Flow

On load, `MainWindow` validates the stored token:

```
ValidateStoredTokenAsync()
  → Token valid:   account restored silently
  → Token missing/invalid:
       NexoraLoginWindow.ShowDialog()
         → LoginSuccessful = true  → OnNexoraLoginSuccessful()
         → ContinuedWithoutAccount → app opens without Nexora features
```

---

## NexoraLoginWindow

A dedicated WPF dialog for the full Nexora authentication flow:

- Email/username + password form
- 2FA code entry (shown when server returns `twoFactorRequired: true`)
- "Continue without account" option

Properties after close:
```csharp
public bool LoginSuccessful          { get; }
public bool ContinuedWithoutAccount  { get; }
```

---

## Friends Page

`FriendsPage.xaml` + `FriendsPageViewModel`:

- Current user's profile (username, linked Minecraft account)
- Incoming friend requests (accept / decline)
- Accepted friends list with Minecraft usernames and avatars
- "Add friend" input by Nexora username

All data loaded on page open via `INexoraApiService`.

---

## Account Page

- Nexora: display name, sign-in/sign-out, Minecraft link status
- Minecraft: logged-in username, UUID, "Link to Nexora" action

When the user links a Minecraft account:
```csharp
await _nexoraAccountService.LinkMinecraftAccountAsync(minecraftAccount);
// → POST /api/launcher/link.php → refresh profile
```

---

## Share Windows

### ShareInstanceWindow

Opened from an installation's context menu. Three modes:

1. **Share with friends** — select from friend list, export to `.mrpack`, POST to `share-instance.php`
2. **Generate share code** — exports, uploads to `create-instance-code.php`, shows 8-char code (7-day expiry)
3. **Import from code** — enter code, call `redeem-instance-code.php`, install modpack

### ShareTunnelWindow

Opened from an active tunnel. Share the tunnel's public address with friends by Nexora username via `share-tunnel.php`.

---

## Notification Badges

`MainWindow` subscribes to both notification managers. When `Changed` fires:

- Sidebar badge count is updated
- The relevant page list is refreshed

Polling runs every **30 seconds** in a background loop with cancellation token support. Starts on sign-in, stops on sign-out.

---

## Nexora Web Desktop

The Nexora web platform (`nexoragames.nl/desktop`) is a separate browser-based environment. The WPF launcher does not embed it. Both share the same Nexora account, friend list, and API infrastructure — but the web desktop (chat, Hazardous game, settings) is accessed through the user's browser.
