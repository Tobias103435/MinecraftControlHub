using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MinecraftControlHub.Core.Models;

/// <summary>
/// A notification that someone shared a Minecraft instance (modpack) with the current user.
/// </summary>
public class InstanceShareNotification : INotifyPropertyChanged
{
    private bool _isRead;

    public int    Id              { get; set; }
    public string SenderUsername  { get; set; } = string.Empty;
    public string InstanceName    { get; set; } = string.Empty;
    public string ShareCode       { get; set; } = string.Empty;
    public DateTime CreatedAt     { get; set; }

    public bool IsRead
    {
        get => _isRead;
        set
        {
            if (_isRead == value) return;
            _isRead = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
