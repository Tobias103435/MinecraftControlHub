namespace MinecraftControlHub.Core.Models;

/// <summary>
/// Represents a received tunnel share notification.
/// Sent by another Nexora user who shared a tunnel address with the current user.
/// </summary>
public class TunnelNotification
{
    /// <summary>Unique notification ID from the server.</summary>
    public int Id { get; set; }

    /// <summary>Nexora username of the person who shared the tunnel.</summary>
    public string SenderUsername { get; set; } = string.Empty;

    /// <summary>The public IP address of the shared tunnel.</summary>
    public string Ip { get; set; } = string.Empty;

    /// <summary>The port number of the shared tunnel.</summary>
    public int Port { get; set; }

    /// <summary>Combined "ip:port" string for convenience.</summary>
    public string IpPort => $"{Ip}:{Port}";

    /// <summary>Optional server/tunnel name provided by the sender.</summary>
    public string? ServerName { get; set; }

    /// <summary>UTC timestamp when the share was created.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Whether the notification has been read by the current user.</summary>
    public bool IsRead { get; set; }

    /// <summary>First letter of the sender's username for the avatar circle.</summary>
    public string SenderInitial =>
        string.IsNullOrEmpty(SenderUsername) ? "?" : SenderUsername[0].ToString().ToUpper();

    /// <summary>Human-friendly timestamp label, e.g. "Just now" or "2h ago".</summary>
    public string TimeLabel
    {
        get
        {
            var diff = DateTime.UtcNow - CreatedAt;
            if (diff.TotalSeconds < 60)  return "Just now";
            if (diff.TotalMinutes < 60)  return $"{(int)diff.TotalMinutes}m ago";
            if (diff.TotalHours   < 24)  return $"{(int)diff.TotalHours}h ago";
            return CreatedAt.ToLocalTime().ToString("MMM d");
        }
    }
}
