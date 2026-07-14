namespace MinecraftControlHub.Core.Models;

/// <summary>
/// A signed-in Minecraft (Microsoft) account. Persisted to disk so the user
/// stays logged in across restarts; the Microsoft refresh token is used to
/// silently obtain fresh access tokens.
/// </summary>
public class MinecraftAccount
{
    /// <summary>The in-game player name.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>The Minecraft profile UUID (no dashes, as Mojang returns it).</summary>
    public string Uuid { get; set; } = string.Empty;

    /// <summary>The Minecraft (Yggdrasil) access token used to launch the game online.</summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>UTC time the current <see cref="AccessToken"/> expires.</summary>
    public DateTime AccessTokenExpiresAt { get; set; }

    /// <summary>The Microsoft OAuth refresh token used to silently re-authenticate.</summary>
    public string MicrosoftRefreshToken { get; set; } = string.Empty;

    /// <summary>The Xbox user id (XUID), passed to the game as auth_xuid.</summary>
    public string Xuid { get; set; } = string.Empty;

    /// <summary>True when a usable Minecraft access token is present and not expired.</summary>
    public bool IsSignedIn =>
        !string.IsNullOrEmpty(AccessToken) &&
        !string.IsNullOrEmpty(Uuid) &&
        AccessTokenExpiresAt > DateTime.UtcNow.AddMinutes(1);
}
