using System.ComponentModel;
using System.Runtime.CompilerServices;
using MinecraftControlHub.Core.Models;

namespace MinecraftControlHub.UI.ViewModels;

/// <summary>
/// A row in the "share with friends" picker — wraps a Friend and adds an IsSelected toggle.
/// </summary>
public class FriendShareRow : INotifyPropertyChanged
{
    private bool _isSelected;

    public Friend Friend { get; }

    public string DisplayName =>
        !string.IsNullOrWhiteSpace(Friend.MinecraftUsername)
            ? $"{Friend.MinecraftUsername} ({Friend.WebsiteUsername})"
            : Friend.WebsiteUsername ?? string.Empty;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public FriendShareRow(Friend friend)
    {
        Friend = friend;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
