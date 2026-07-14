using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using MinecraftControlHub.Core.Models;
using MinecraftControlHub.Core.Services;

namespace MinecraftControlHub.UI.ViewModels;

public class FriendsPageViewModel : INotifyPropertyChanged
{
    private readonly INexoraAccountService _nexoraService;
    private readonly IMinecraftAccountService _minecraftService;
    private MeResponse? _currentProfile;
    private bool _isLoading;
    private string _statusMessage = string.Empty;
    private string _newFriendUsername = string.Empty;
    private ObservableCollection<Friend> _friends = new();
    private ObservableCollection<FriendRequest> _friendRequests = new();

    public MeResponse? CurrentProfile
    {
        get => _currentProfile;
        set
        {
            _currentProfile = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsMinecraftLinked));
            OnPropertyChanged(nameof(IsNoMicrosoftAccount));
            OnPropertyChanged(nameof(ProfileInitial));
            // Tell WPF to re-check CanExecute on link/unlink buttons
            RaiseAllCommandsCanExecuteChanged();
        }
    }

    public bool IsMinecraftLinked    => CurrentProfile?.MinecraftUuid != null;
    public bool IsNoMicrosoftAccount => !IsNotLoggedIn && !IsMinecraftLinked && _minecraftService.Current == null;
    public bool IsNotLoggedIn        => _nexoraService.Current == null;

    public string ProfileInitial
    {
        get
        {
            var name = CurrentProfile?.WebsiteUsername;
            return string.IsNullOrEmpty(name) ? "?" : name[0].ToString().ToUpper();
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            _isLoading = value;
            OnPropertyChanged();
            // Whenever loading state changes, re-evaluate all button states
            RaiseAllCommandsCanExecuteChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            _statusMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasStatusMessage));
        }
    }

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

    public string NewFriendUsername
    {
        get => _newFriendUsername;
        set
        {
            _newFriendUsername = value;
            OnPropertyChanged();
            // Re-evaluate Add button when username field changes
            (AddFriendCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public ObservableCollection<Friend> Friends
    {
        get => _friends;
        set { _friends = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasNoFriends)); }
    }

    public bool HasNoFriends => Friends.Count == 0;

    public ObservableCollection<FriendRequest> FriendRequests
    {
        get => _friendRequests;
        set { _friendRequests = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasFriendRequests)); }
    }

    public bool HasFriendRequests => FriendRequests.Count > 0;

    public ICommand LinkMinecraftCommand   { get; }
    public ICommand UnlinkMinecraftCommand { get; }
    public ICommand AddFriendCommand       { get; }
    public ICommand AcceptFriendCommand    { get; }
    public ICommand DeclineFriendCommand   { get; }
    public ICommand RefreshCommand         { get; }

    public FriendsPageViewModel(INexoraAccountService nexoraService, IMinecraftAccountService minecraftService)
    {
        _nexoraService    = nexoraService;
        _minecraftService = minecraftService;

        LinkMinecraftCommand   = new RelayCommand(async () => await LinkMinecraftAsync(),   CanLinkMinecraft);
        UnlinkMinecraftCommand = new RelayCommand(async () => await UnlinkMinecraftAsync(), CanUnlinkMinecraft);
        AddFriendCommand       = new RelayCommand(async () => await AddFriendAsync(),       CanAddFriend);
        AcceptFriendCommand    = new RelayCommand(async (id) => await AcceptFriendAsync((int)id!));
        DeclineFriendCommand   = new RelayCommand(async (id) => await DeclineFriendAsync((int)id!));
        RefreshCommand         = new RelayCommand(async () => await LoadDataAsync());

        if (_nexoraService.Current != null)
            LoadDataAsync().ConfigureAwait(false);
    }

    private bool CanLinkMinecraft()   => !IsLoading && !IsMinecraftLinked;
    private bool CanUnlinkMinecraft() => !IsLoading && IsMinecraftLinked;
    private bool CanAddFriend()       => !IsLoading && !string.IsNullOrWhiteSpace(NewFriendUsername);

    public string LinkButtonTooltip => _minecraftService.Current == null
        ? "Clicking this will open the Microsoft sign-in flow automatically."
        : "Link your Minecraft account to your Nexora account.";

    /// <summary>Call this after a successful Nexora login from the overlay or sidebar.</summary>
    public void NotifyLoginStateChanged()
    {
        OnPropertyChanged(nameof(IsNotLoggedIn));
        OnPropertyChanged(nameof(IsNoMicrosoftAccount));
        RaiseAllCommandsCanExecuteChanged();

        if (_nexoraService.Current != null)
            LoadDataAsync().ConfigureAwait(false);
    }

    /// <summary>Notifies all commands to re-evaluate their CanExecute state.</summary>
    private void RaiseAllCommandsCanExecuteChanged()
    {
        (LinkMinecraftCommand   as RelayCommand)?.RaiseCanExecuteChanged();
        (UnlinkMinecraftCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (AddFriendCommand       as RelayCommand)?.RaiseCanExecuteChanged();
        (RefreshCommand         as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private async Task LoadDataAsync()
    {
        if (_nexoraService.Current == null)
        {
            StatusMessage = string.Empty;
            return;
        }

        IsLoading     = true;
        StatusMessage = string.Empty;

        try
        {
            CurrentProfile = await _nexoraService.GetProfileAsync();

            var api   = _nexoraService.GetApiService();
            var token = _nexoraService.Current.Token;

            var friendsResp = await api.GetFriendsAsync(token);
            Friends = friendsResp.Success && friendsResp.Data != null
                ? new ObservableCollection<Friend>(friendsResp.Data)
                : new ObservableCollection<Friend>();

            var requestsResp = await api.GetFriendRequestsAsync(token);
            FriendRequests = requestsResp.Success && requestsResp.Data != null
                ? new ObservableCollection<FriendRequest>(requestsResp.Data)
                : new ObservableCollection<FriendRequest>();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading data: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LinkMinecraftAsync()
    {
        // If no Microsoft account is signed in, start the sign-in flow automatically
        if (_minecraftService.Current == null)
        {
            StatusMessage = "Opening Microsoft sign-in… Please complete the browser step.";
            IsLoading = true;

            var dispatcher = System.Windows.Application.Current.Dispatcher;
            var progress = new System.Progress<MinecraftControlHub.Core.Services.DeviceCodeInfo>(info =>
            {
                dispatcher.Invoke(() =>
                {
                    StatusMessage = $"Open {info.VerificationUri} and enter: {info.UserCode}";
                });
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(info.VerificationUri) { UseShellExecute = true }); }
                catch { }
            });

            var msResult = await _minecraftService.SignInAsync(progress);
            IsLoading = false;

            if (!msResult.Success)
            {
                StatusMessage = $"Microsoft sign-in failed: {msResult.Error}";
                return;
            }

            StatusMessage = $"Signed in as {msResult.Account?.Username}. Linking to Nexora…";
        }

        IsLoading     = true;
        StatusMessage = "Linking Minecraft account…";

        try
        {
            var response = await _nexoraService.LinkMinecraftAccountAsync(_minecraftService.Current!);
            if (response.Success)
            {
                // Immediately update local state so the UI reflects the link
                if (_nexoraService.Current != null && _minecraftService.Current != null)
                {
                    _nexoraService.Current.MinecraftLink = new MinecraftControlHub.Core.Models.MinecraftLink
                    {
                        Uuid     = _minecraftService.Current.Uuid,
                        Username = _minecraftService.Current.Username
                    };
                }
                StatusMessage = "✓ Minecraft account linked successfully!";
                await LoadDataAsync();
            }
            else
            {
                StatusMessage = $"Failed to link: {response.Error}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error linking account: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task UnlinkMinecraftAsync()
    {
        IsLoading     = true;
        StatusMessage = "Unlinking Minecraft account…";

        try
        {
            var api      = _nexoraService.GetApiService();
            var token    = _nexoraService.Current!.Token;
            var response = await api.UnlinkMinecraftAccountAsync(token);

            if (response.Success)
            {
                if (_nexoraService.Current != null)
                    _nexoraService.Current.MinecraftLink = null;

                StatusMessage = "✓ Minecraft account unlinked.";
                await LoadDataAsync();
            }
            else
            {
                StatusMessage = $"Could not unlink: {response.Error}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task AddFriendAsync()
    {
        if (string.IsNullOrWhiteSpace(NewFriendUsername)) return;

        IsLoading     = true;
        StatusMessage = "Sending friend request…";

        try
        {
            var response = await _nexoraService.GetApiService()
                .SendFriendRequestAsync(_nexoraService.Current!.Token, NewFriendUsername.Trim());

            if (response.Success)
            {
                StatusMessage     = $"✓ Friend request sent to {NewFriendUsername}!";
                NewFriendUsername = string.Empty;
            }
            else
            {
                StatusMessage = $"Failed: {response.Error}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task AcceptFriendAsync(int requestId)
    {
        IsLoading = true;
        try
        {
            var response = await _nexoraService.GetApiService()
                .AcceptFriendRequestAsync(_nexoraService.Current!.Token, requestId);

            StatusMessage = response.Success ? "✓ Friend request accepted!" : $"Failed: {response.Error}";
            if (response.Success) await LoadDataAsync();
        }
        catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    private async Task DeclineFriendAsync(int requestId)
    {
        IsLoading = true;
        try
        {
            var response = await _nexoraService.GetApiService()
                .DeclineFriendRequestAsync(_nexoraService.Current!.Token, requestId);

            StatusMessage = response.Success ? "Friend request declined." : $"Failed: {response.Error}";
            if (response.Success) await LoadDataAsync();
        }
        catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
