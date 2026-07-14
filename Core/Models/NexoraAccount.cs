using System.Text.Json.Serialization;

namespace MinecraftControlHub.Core.Models;

/// <summary>
/// Represents a user's Nexora website account (stored locally after login).
/// </summary>
public class NexoraAccount
{
    public int    UserId   { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Token    { get; set; } = string.Empty;

    /// <summary>Minecraft account information (if linked).</summary>
    public MinecraftLink? MinecraftLink { get; set; }
}

/// <summary>Represents a linked Minecraft account.</summary>
public class MinecraftLink
{
    public string Uuid     { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
}

/// <summary>
/// A friend in the friends list.
/// Matches the snake_case fields returned by friends.php.
/// </summary>
public class Friend
{
    [JsonPropertyName("website_username")]
    public string WebsiteUsername { get; set; } = string.Empty;

    [JsonPropertyName("minecraft_username")]
    public string? MinecraftUsername { get; set; }

    [JsonPropertyName("minecraft_uuid")]
    public string? MinecraftUuid { get; set; }

    /// <summary>First letter of the website username for the avatar circle.</summary>
    [JsonIgnore]
    public string Initial => string.IsNullOrEmpty(WebsiteUsername) ? "?" : WebsiteUsername[0].ToString().ToUpper();
}

/// <summary>
/// An incoming friend request.
/// Matches the fields returned by friend-requests.php.
/// </summary>
public class FriendRequest
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    /// <summary>First letter of the username for the avatar circle.</summary>
    [JsonIgnore]
    public string Initial => string.IsNullOrEmpty(Username) ? "?" : Username[0].ToString().ToUpper();
}

/// <summary>Generic API response wrapper.</summary>
public class ApiResponse<T>
{
    public bool    Success { get; set; }
    public string? Error   { get; set; }
    public T?      Data    { get; set; }

    public static ApiResponse<T> Ok(T data)       => new() { Success = true,  Data  = data };
    public static ApiResponse<T> Fail(string? err) => new() { Success = false, Error = err  };
}

/// <summary>
/// Login response — fields come back flat at the JSON root next to "success".
/// Snake_case names match what login.php / verify-2fa.php return.
/// </summary>
public class LoginResponse
{
    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    // API may return "userId" or "user_id" — cover both via property-level name
    [JsonPropertyName("userId")]
    public int UserId { get; set; }

    [JsonPropertyName("twoFactorRequired")]
    public bool TwoFactorRequired { get; set; }

    // API returns snake_case: method
    [JsonPropertyName("method")]
    public string? Method { get; set; }

    // API returns snake_case: challenge
    [JsonPropertyName("challenge")]
    public string? Challenge { get; set; }
}

/// <summary>
/// Profile response from me.php — fields returned flat at the JSON root.
/// </summary>
public class MeResponse
{
    [JsonPropertyName("website_username")]
    public string WebsiteUsername { get; set; } = string.Empty;

    [JsonPropertyName("minecraft_username")]
    public string? MinecraftUsername { get; set; }

    [JsonPropertyName("minecraft_uuid")]
    public string? MinecraftUuid { get; set; }
}
