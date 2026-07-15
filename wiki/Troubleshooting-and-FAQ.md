# Troubleshooting and FAQ

## Diagnostics Log

The first place to look for any issue:

```
%LocalAppData%\MinecraftControlHub\logs\diagnostics.log
```

This file contains timestamped entries for all background service errors, failed API calls, and provisioning events.

---

## Installation Problems

### Java not detected

**Symptom:** "No compatible Java installation found" on the health card or at launch.

**Fix:**
1. Install the correct Java version for your Minecraft version (Java 8 for ≤1.17, Java 17 for 1.18–1.20, Java 21 for 1.21+)
2. Or set a custom Java path in the installation settings panel

### Game crashes immediately on launch

**Symptom:** Minecraft opens and closes within seconds with no error window.

**Fix:**
1. Check the crash report in `%LocalAppData%\MinecraftControlHub\instances\<name>\logs\latest.log`
2. Open the AI terminal and ask it to "show the latest crash report" — it will diagnose the cause automatically
3. Common causes: insufficient RAM, conflicting mods, wrong Fabric API version

### Forge / NeoForge installation fails

**Symptom:** Loader installation step hangs or reports a download error.

**Fix:**
1. Verify your internet connection
2. Try again — Maven sometimes has temporary outages
3. Check `diagnostics.log` for the specific HTTP error code

### Assets not downloading

**Symptom:** Missing sounds, textures, or language files in-game.

**Fix:**
1. The Mojang asset CDN (`resources.download.minecraft.net`) may be slow — wait and retry
2. Check that Windows Firewall is not blocking the launcher

---

## Server Problems

### Server fails to start

**Symptom:** Server terminal shows `Error: Unable to access jarfile` or nothing at all.

**Fix:**
1. Check that the server jar was fully downloaded — look for provisioning errors in `diagnostics.log`
2. Delete the server folder and recreate the server (provisioning will re-download the jar)
3. Verify Java is available at the configured path

### Server crashes on startup

**Symptom:** Server starts and immediately exits with a non-zero exit code.

**Fix:**
1. The AI terminal will automatically receive the crash report and start diagnosis
2. Common causes: port already in use, insufficient RAM, corrupted world

### Port already in use

**Symptom:** `FAILED TO BIND TO PORT` in the server terminal.

**Fix:**
1. Change the server port in the `server.properties` editor
2. Or stop the other process using that port

---

## Mod Problems

### Mod fails to install

**Symptom:** "Installation failed" error when clicking Install.

**Fix:**
1. Check that the mod is compatible with your loader and Minecraft version
2. Modrinth and CurseForge rate-limit heavily — wait 30 seconds and retry
3. Check `diagnostics.log` for the API response

### Dependency conflicts

**Symptom:** Game crashes with `net.fabricmc.loader` or Forge error mentioning version constraints.

**Fix:**
1. Use the conflict scanner on the Mods page
2. Check if two mods require different versions of the same dependency
3. Ask the AI terminal to "scan my mods for conflicts" — it will list the affected pairs

---

## Tunnel Problems

### Tunnel address not appearing

**Symptom:** Tunnel created but no public address shows up.

**Fix:**
1. For playit.gg free tier: the address is shown in the official playit wizard window — paste it manually into the launcher
2. For ngrok: check that your authtoken is correctly configured in Settings → Tunnels
3. Check `diagnostics.log` for process launch errors

### Friends can't connect through tunnel

**Symptom:** Tunnel address is shared but connection times out.

**Fix:**
1. Confirm the server is actually running before the tunnel
2. Verify Windows Firewall allows the server process
3. Check that the port in the tunnel matches the port in `server.properties`

---

## Authentication Problems

### Microsoft login loop

**Symptom:** Device code appears but after entering it, the launcher asks again.

**Fix:**
1. Make sure you are signing in with a Microsoft account that owns Minecraft Java Edition
2. Check that `minecraft_account.json` is not corrupted — delete it and re-authenticate

### Nexora login fails with "Invalid credentials"

**Symptom:** Correct password entered but login is rejected.

**Fix:**
1. Try logging in on the website at nexoragames.nl to confirm the credentials work
2. If you have 2FA enabled, check that your authenticator clock is synchronized

---

## FAQ

**Q: Can I use the launcher without a Nexora account?**
Yes. All features except friends, modpack sharing, and tunnel sharing work without a Nexora account.

**Q: Can I use the launcher without a Microsoft account?**
Yes — you can launch in offline mode using the offline username configured in Settings.

**Q: Where are my Minecraft worlds stored?**
In `%LocalAppData%\MinecraftControlHub\instances\<installation-name>\saves\`.

**Q: How do I completely uninstall the launcher?**
Delete the executable and the folder `%LocalAppData%\MinecraftControlHub\`. There are no registry entries.

**Q: Can I import from the official Minecraft launcher?**
Yes — the launcher can import profiles from the official launcher's installation directory.
