// Partial extension of SettingsPageViewModel — Nexora account section.
// Add this file next to SettingsPageViewModel.cs and add the Nexora
// dependency to the constructor (see comment below).

using MinecraftControlHub.Core.Services;

namespace MinecraftControlHub.UI.ViewModels;

/// <summary>
/// Nexora-specific methods for the Settings page.
/// Wire up: add INexoraAccountService as a constructor parameter in
/// SettingsPageViewModel.cs and store it as _nexoraAccountService.
/// </summary>
public partial class SettingsPageViewModel
{
    // Add this field + constructor param to the main SettingsPageViewModel:
    //   private readonly INexoraAccountService _nexoraAccountService;

    // ── Properties ───────────────────────────────────────────────────────

    public bool IsNexoraLoggedIn    => _nexoraAccountService?.Current != null;
    public bool IsNexoraLoggedOut   => !IsNexoraLoggedIn;
    public string NexoraUsername    => _nexoraAccountService?.Current?.Username ?? string.Empty;
    public string NexoraUsernameInitial => 
        _nexoraAccountService?.Current?.Username is { Length: > 0 } u 
            ? u[0].ToString().ToUpper() 
            : "?";
    public bool IsMinecraftLinked   => _nexoraAccountService?.Current?.MinecraftLink != null;
    public string MinecraftUsername => _nexoraAccountService?.Current?.MinecraftLink?.Username ?? string.Empty;

    // ── Actions ──────────────────────────────────────────────────────────

    public void NotifyNexoraStateChanged()
    {
        OnPropertyChanged(nameof(IsNexoraLoggedIn));
        OnPropertyChanged(nameof(IsNexoraLoggedOut));
        OnPropertyChanged(nameof(NexoraUsername));
        OnPropertyChanged(nameof(NexoraUsernameInitial));
        OnPropertyChanged(nameof(IsMinecraftLinked));
        OnPropertyChanged(nameof(MinecraftUsername));
    }

    public async Task NexoraUnlinkMinecraftAsync()
    {
        if (_nexoraAccountService?.Current == null) return;

        IsBusy = true;
        StatusMessage = "Unlinking Minecraft account…";
        try
        {
            var api = _nexoraAccountService.GetApiService();
            var response = await api.UnlinkMinecraftAccountAsync(_nexoraAccountService.Current.Token);

            if (response.Success)
            {
                // Clear local link
                if (_nexoraAccountService.Current != null)
                    _nexoraAccountService.Current.MinecraftLink = null;

                StatusMessage = "Minecraft account unlinked.";
                NotifyNexoraStateChanged();
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
            IsBusy = false;
        }
    }

    public void NexoraLogout()
    {
        _nexoraAccountService?.SignOut();
        StatusMessage = "Logged out of Nexora.";
        NotifyNexoraStateChanged();
    }
}
