# Dialog Windows and Specialized Interfaces

## NexoraLoginWindow

The Nexora authentication dialog shown on startup (or when re-authenticating).

**Features:**
- Email/username + password form
- 2FA code entry (shown conditionally after server returns `twoFactorRequired: true`)
- "Continue without account" option
- Error messages for failed login attempts

**Properties after close:**
```csharp
public bool LoginSuccessful          { get; }
public bool ContinuedWithoutAccount  { get; }
```

---

## ShareInstanceWindow

Opened from an installation's context menu or detail panel.

**Three modes (tabs):**

1. **Share with friends**
   - Loads friend list from `INexoraApiService.GetFriendsAsync()`
   - Select one or more friends from the list
   - Exports installation to `.mrpack` and sends via `IInstanceShareService.ShareWithFriendsAsync()`
   - Shows result: who received it, who wasn't found, who isn't a friend

2. **Generate share code**
   - Exports installation and calls `CreateShareCodeAsync()`
   - Displays the 8-character code with a copy button
   - Notes the 7-day expiry

3. **Import from code**
   - Text field for entering a share code
   - Calls `RedeemShareCodeAsync()` on submit
   - Creates and shows the new installation on success

---

## ShareTunnelWindow

Opened from an active tunnel's share button.

**Features:**
- Friend list with multi-select
- Active tunnel address displayed (read-only)
- Server name field
- "Share" button → calls `ITunnelShareService.ShareTunnelAsync()`
- Shows result similar to ShareInstanceWindow

---

## InstallationSettingsWindow

Opens when the user edits an installation's settings. Contains multiple tabs:

| Tab | Contents |
|---|---|
| General | Name, Minecraft version, loader, loader version |
| Java | Custom Java path, JVM arguments, min/max RAM |
| Mods | Installed mods list (same as ModsPage but scoped to this installation) |
| Content | Resource packs, shader packs |
| Health | Health report card, crash history |

---

## CreateInstallationDialog

Step-by-step wizard for creating a new installation:

1. Enter name
2. Select Minecraft version (fetched from Mojang manifest)
3. Select loader (Vanilla / Fabric / Forge / NeoForge / Quilt)
4. Select loader version (fetched from loader API)
5. Configure RAM (shows RamCalculatorService recommendation)
6. Confirm and create

---

## CreateServerDialog

Similar wizard for creating a new server:

1. Enter name
2. Select server type (Vanilla, Paper, Purpur, Fabric, Forge, NeoForge, Quilt)
3. Select Minecraft version
4. Configure RAM and port (auto-detected by `PortScanService`)
5. Confirm and provision
