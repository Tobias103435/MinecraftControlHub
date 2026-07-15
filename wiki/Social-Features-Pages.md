# Social Features Pages

## Friends Page

`FriendsPage.xaml` + `FriendsPageViewModel`

Shows the user's Nexora friend network and handles friend request management.

**Requires:** Nexora account signed in. If not signed in, the page shows a sign-in prompt.

### Sections

**Incoming friend requests**
- List of pending requests with accept / decline buttons
- Badge on the sidebar icon when unread requests exist
- Accepting a request moves the user to the friends list immediately

**Friends list**
- Shows Nexora username and linked Minecraft username (if any)
- Player head avatars loaded asynchronously from `crafatar.com` using the Minecraft UUID
- "Share Modpack" button per friend (opens ShareInstanceWindow pre-selected for that friend)

**Add friend**
- Text field to enter a Nexora username
- "Send Request" button → calls `POST friend-request.php`
- Inline error for "user not found" or "already friends"

---

## Account Page

`AccountPage.xaml` + `AccountPageViewModel`

Manages both the Nexora account and the linked Minecraft account.

### Nexora Account Section

- Displays username and sign-in status
- "Sign Out" button → calls `NexoraAccountService.SignOutAsync()`
- "Sign In" button (shown when not signed in) → opens `NexoraLoginWindow`

### Minecraft Account Section

- Displays Minecraft username, UUID, and skin head
- "Link to Nexora" button (if not linked) → calls `LinkMinecraftAccountAsync()`
- "Unlink" button (if linked) → calls `POST unlink.php`
- "Switch Account" button → starts a new device code flow

### Offline Mode

If the user is not signed in with Microsoft, an "Offline mode" badge is shown. Offline mode uses the username from `Settings.OfflineUsername`. The user can set it from the Account page.
