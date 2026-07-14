using System.Diagnostics;
using System.Windows;
using MinecraftControlHub.Core.Services;

namespace MinecraftControlHub.UI.ViewModels;

public class AccountPageViewModel : ViewModelBase
{
    private readonly IMinecraftAccountService _accountService;

    private bool _isSigningIn;
    private string _deviceCode = string.Empty;
    private string _deviceVerificationUri = string.Empty;
    private string _accountStatus = string.Empty;
    private List<AppearanceItem> _skins = new();
    private List<AppearanceItem> _capes = new();
    private string _skinVariant = "classic";
    private string _skinUrl = string.Empty;
    private string _appearanceStatus = string.Empty;
    private string _mchEmail = string.Empty;
    private string _mchStatus = string.Empty;

    public AccountPageViewModel(IMinecraftAccountService accountService)
    {
        _accountService = accountService;
        _accountService.AccountChanged += (_, _) => OnAccountChanged();

        if (IsSignedIn)
            _ = LoadAppearanceAsync();
    }

    public bool IsSignedIn => _accountService.Current?.IsSignedIn == true;

    public bool IsSignedOut => !IsSignedIn;

    public string AccountName => _accountService.Current?.Username ?? string.Empty;

    public string AvatarUrl => IsSignedIn && !string.IsNullOrEmpty(_accountService.Current?.Uuid)
        ? $"https://mc-heads.net/body/{_accountService.Current!.Uuid}"
        : string.Empty;

    public bool IsSigningIn
    {
        get => _isSigningIn;
        set => SetProperty(ref _isSigningIn, value);
    }

    public string DeviceCode
    {
        get => _deviceCode;
        set
        {
            if (SetProperty(ref _deviceCode, value))
                OnPropertyChanged(nameof(HasDeviceCode));
        }
    }

    public bool HasDeviceCode => !string.IsNullOrEmpty(DeviceCode);

    public string DeviceVerificationUri
    {
        get => _deviceVerificationUri;
        set => SetProperty(ref _deviceVerificationUri, value);
    }

    public string AccountStatus
    {
        get => _accountStatus;
        set
        {
            if (SetProperty(ref _accountStatus, value))
                OnPropertyChanged(nameof(HasAccountStatus));
        }
    }

    public bool HasAccountStatus => !string.IsNullOrEmpty(AccountStatus);

    public List<AppearanceItem> Skins
    {
        get => _skins;
        private set => SetProperty(ref _skins, value);
    }

    public List<AppearanceItem> Capes
    {
        get => _capes;
        private set => SetProperty(ref _capes, value);
    }

    public string SkinVariant
    {
        get => _skinVariant;
        set
        {
            var normalized = string.Equals(value, "slim", StringComparison.OrdinalIgnoreCase)
                ? "slim"
                : "classic";

            if (SetProperty(ref _skinVariant, normalized))
            {
                OnPropertyChanged(nameof(IsClassicVariant));
                OnPropertyChanged(nameof(IsSlimVariant));
            }
        }
    }

    public bool IsClassicVariant
    {
        get => string.Equals(SkinVariant, "classic", StringComparison.OrdinalIgnoreCase);
        set
        {
            if (value)
                SkinVariant = "classic";
        }
    }

    public bool IsSlimVariant
    {
        get => string.Equals(SkinVariant, "slim", StringComparison.OrdinalIgnoreCase);
        set
        {
            if (value)
                SkinVariant = "slim";
        }
    }

    public string SkinUrl
    {
        get => _skinUrl;
        set => SetProperty(ref _skinUrl, value);
    }

    public string AppearanceStatus
    {
        get => _appearanceStatus;
        set
        {
            if (SetProperty(ref _appearanceStatus, value))
                OnPropertyChanged(nameof(HasAppearanceStatus));
        }
    }

    public bool HasAppearanceStatus => !string.IsNullOrEmpty(AppearanceStatus);

    public string MchEmail
    {
        get => _mchEmail;
        set => SetProperty(ref _mchEmail, value);
    }

    public string MchStatus
    {
        get => _mchStatus;
        set
        {
            if (SetProperty(ref _mchStatus, value))
                OnPropertyChanged(nameof(HasMchStatus));
        }
    }

    public bool HasMchStatus => !string.IsNullOrEmpty(MchStatus);

    public async Task SignInAsync()
    {
        if (IsSigningIn) return;

        IsSigningIn = true;
        AccountStatus = string.Empty;
        AppearanceStatus = string.Empty;
        DeviceCode = string.Empty;
        DeviceVerificationUri = string.Empty;

        var dispatcher = System.Windows.Application.Current.Dispatcher;
        var progress = new Progress<DeviceCodeInfo>(info =>
        {
            dispatcher.Invoke(() =>
            {
                DeviceCode = info.UserCode;
                DeviceVerificationUri = info.VerificationUri;
                AccountStatus = $"Open {info.VerificationUri} and enter the code below to sign in.";
            });

            try
            {
                Process.Start(new ProcessStartInfo(info.VerificationUri) { UseShellExecute = true });
            }
            catch { /* user can open it manually */ }
        });

        var result = await _accountService.SignInAsync(progress);

        await dispatcher.InvokeAsync(() =>
        {
            IsSigningIn = false;
            DeviceCode = string.Empty;
            DeviceVerificationUri = string.Empty;

            AccountStatus = result.Success
                ? $"Signed in as {result.Account?.Username}."
                : (result.Error ?? "Sign-in failed.");

            RaiseAccountProps();
        });

        if (result.Success)
            await LoadAppearanceAsync();
    }

    public void SignOut()
    {
        _accountService.SignOut();
        DeviceCode = string.Empty;
        DeviceVerificationUri = string.Empty;
        AccountStatus = "Signed out.";
        AppearanceStatus = string.Empty;
        Skins = new();
        Capes = new();
        RaiseAccountProps();
    }

    public void CopyDeviceCode()
    {
        if (!string.IsNullOrEmpty(DeviceCode))
        {
            try { Clipboard.SetText(DeviceCode); } catch { /* ignore */ }
        }
    }

    public async Task LoadAppearanceAsync()
    {
        if (!IsSignedIn)
        {
            Skins = new();
            Capes = new();
            AppearanceStatus = string.Empty;
            return;
        }

        try
        {
            var appearance = await _accountService.GetAppearanceAsync();
            if (appearance == null)
            {
                Skins = new();
                Capes = new();
                AppearanceStatus = "Couldn't load your Minecraft skins and capes right now.";
                return;
            }

            Skins = appearance.Skins;
            Capes = appearance.Capes;
            AppearanceStatus = string.Empty;
        }
        catch (Exception ex)
        {
            Skins = new();
            Capes = new();
            AppearanceStatus = ex.Message;
        }
    }

    public async Task ApplyUrlSkinAsync()
    {
        AppearanceStatus = string.Empty;

        var result = await _accountService.ChangeSkinAsync(SkinUrl, SkinVariant);
        if (!result.Success)
        {
            AppearanceStatus = result.Error ?? "Skin change failed.";
            return;
        }

        await LoadAppearanceAsync();
        SkinUrl = string.Empty;
        AppearanceStatus = "Skin updated.";
    }

    public async Task ChangeSkinFromFileAsync(string path)
    {
        AppearanceStatus = string.Empty;

        var result = await _accountService.ChangeSkinFromFileAsync(path, SkinVariant);
        if (!result.Success)
        {
            AppearanceStatus = result.Error ?? "Skin upload failed.";
            return;
        }

        await LoadAppearanceAsync();
        AppearanceStatus = "Skin updated.";
    }

    public async Task UseCapeAsync(AppearanceItem cape)
    {
        AppearanceStatus = string.Empty;

        var result = await _accountService.SetActiveCapeAsync(cape.Id);
        if (!result.Success)
        {
            AppearanceStatus = result.Error ?? "Cape change failed.";
            return;
        }

        await LoadAppearanceAsync();
        AppearanceStatus = "Cape updated.";
    }

    public async Task HideCapeAsync()
    {
        AppearanceStatus = string.Empty;

        var result = await _accountService.SetActiveCapeAsync(null);
        if (!result.Success)
        {
            AppearanceStatus = result.Error ?? "Cape change failed.";
            return;
        }

        await LoadAppearanceAsync();
        AppearanceStatus = "Cape hidden.";
    }

    public void MchLogin()
    {
        MchStatus = "Coming soon — your Minecraft Control Hub account will later be linked to your Microsoft account.";
    }

    private void OnAccountChanged()
    {
        RaiseAccountProps();

        if (IsSignedIn)
        {
            _ = LoadAppearanceAsync();
            return;
        }

        Skins = new();
        Capes = new();
        AppearanceStatus = string.Empty;
    }

    private void RaiseAccountProps()
    {
        OnPropertyChanged(nameof(IsSignedIn));
        OnPropertyChanged(nameof(IsSignedOut));
        OnPropertyChanged(nameof(AccountName));
        OnPropertyChanged(nameof(AvatarUrl));
    }
}