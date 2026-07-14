using System.ComponentModel;
using System.Runtime.CompilerServices;
using MinecraftControlHub.Core.Models;

namespace MinecraftControlHub.UI.ViewModels;

/// <summary>
/// Wraps a Nexora Friend with an IsWhitelisted toggle for the server Friends tab.
/// </summary>
public class FriendWhitelistRow : INotifyPropertyChanged
{
    private bool _isWhitelisted;

    public Friend Friend { get; }

    public string  WebsiteUsername  => Friend.WebsiteUsername  ?? string.Empty;
    public string? MinecraftUsername => Friend.MinecraftUsername;
    public string  Initial          => Friend.Initial;

    /// <summary>Minecraft username used for the whitelist command — falls back to website username.</summary>
    public string WhitelistName =>
        !string.IsNullOrWhiteSpace(Friend.MinecraftUsername)
            ? Friend.MinecraftUsername
            : Friend.WebsiteUsername ?? string.Empty;

    public bool IsWhitelisted
    {
        get => _isWhitelisted;
        set
        {
            if (_isWhitelisted == value) return;
            _isWhitelisted = value;
            OnPropertyChanged();
        }
    }

    public FriendWhitelistRow(Friend friend, bool isWhitelisted)
    {
        Friend         = friend;
        _isWhitelisted = isWhitelisted;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
