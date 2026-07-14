using System.Text.Json;
using MinecraftControlHub.Core.Models;

namespace MinecraftControlHub.Core.Services;

/// <summary>
/// Service for managing the Nexora website account (token storage, validation, etc.)
/// </summary>
public interface INexoraAccountService
{
    /// <summary>The currently signed-in Nexora account, or null when signed out.</summary>
    NexoraAccount? Current { get; }
    
    /// <summary>Raised whenever the account changes (sign-in / sign-out).</summary>
    event EventHandler? AccountChanged;
    
    /// <summary>Signs in with website credentials and stores the token locally.</summary>
    Task<ApiResponse<LoginResponse>> SignInAsync(string emailOrUsername, string password);
    
    /// <summary>Completes a login that required a two-factor code and stores the token locally.</summary>
    Task<ApiResponse<LoginResponse>> Verify2FAAsync(string challenge, string code);
    
    /// <summary>Validates the stored token and refreshes account info.</summary>
    Task<NexoraAccount?> ValidateStoredTokenAsync();
    
    /// <summary>Signs out and clears the stored token.</summary>
    void SignOut();
    
    /// <summary>Links the current Minecraft account to the Nexora account.</summary>
    Task<ApiResponse<object>> LinkMinecraftAccountAsync(MinecraftAccount minecraftAccount);
    
    /// <summary>Gets the current user's profile with Minecraft link info.</summary>
    Task<MeResponse?> GetProfileAsync();
    
    /// <summary>Gets the underlying API service for direct API calls.</summary>
    INexoraApiService GetApiService();
}

public class NexoraAccountService : INexoraAccountService
{
    private readonly INexoraApiService _api;
    private readonly IMinecraftAccountService _minecraftService;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    
    
    public NexoraAccount? Current { get; private set; }
    public event EventHandler? AccountChanged;
    
    public NexoraAccountService(INexoraApiService api, IMinecraftAccountService minecraftService)
    {
        _api = api;
        _minecraftService = minecraftService;
        Current = Load();
    }
    
    public async Task<ApiResponse<LoginResponse>> SignInAsync(string emailOrUsername, string password)
    {
        var response = await _api.LoginAsync(emailOrUsername, password);
        StoreAccountIfAuthenticated(response);
        return response;
    }
    
    public async Task<ApiResponse<LoginResponse>> Verify2FAAsync(string challenge, string code)
    {
        var response = await _api.Verify2FAAsync(challenge, code);
        StoreAccountIfAuthenticated(response);
        return response;
    }
    
    /// <summary>
    /// Persists the account only when the response is a completed login (a token is
    /// present). Responses that still require a second factor are left unstored.
    /// </summary>
    private void StoreAccountIfAuthenticated(ApiResponse<LoginResponse> response)
    {
        if (response.Success
            && response.Data != null
            && !response.Data.TwoFactorRequired
            && !string.IsNullOrEmpty(response.Data.Token))
        {
            Current = new NexoraAccount
            {
                UserId = response.Data.UserId,
                Username = response.Data.Username,
                Token = response.Data.Token
            };
            
            Persist();
            AccountChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    
    public async Task<NexoraAccount?> ValidateStoredTokenAsync()
    {
        if (Current == null || string.IsNullOrEmpty(Current.Token))
            return null;
        
        var response = await _api.ValidateTokenAsync(Current.Token);
        
        if (response.Success && response.Data != null)
        {
            Current = response.Data;
            Current.Token = response.Data.Token;
            Persist();
            return Current;
        }
        
        // Token is invalid, sign out
        SignOut();
        return null;
    }
    
    public void SignOut()
    {
        if (Current != null && !string.IsNullOrEmpty(Current.Token))
        {
            // Try to invalidate token on server (fire and forget)
            _ = _api.LogoutAsync(Current.Token);
        }
        
        Current = null;
        
        try
        {
            var path = AppPaths.NexoraAccountFile;
            if (System.IO.File.Exists(path))
                System.IO.File.Delete(path);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Nexora account delete failed: {ex.Message}");
        }
        
        AccountChanged?.Invoke(this, EventArgs.Empty);
    }
    
    public async Task<ApiResponse<object>> LinkMinecraftAccountAsync(MinecraftAccount minecraftAccount)
    {
        if (Current == null || string.IsNullOrEmpty(Current.Token))
        {
            return new ApiResponse<object> { Success = false, Error = "Not logged in to Nexora account" };
        }
        
        if (minecraftAccount == null || string.IsNullOrEmpty(minecraftAccount.Uuid))
        {
            return new ApiResponse<object> { Success = false, Error = "No Minecraft account available" };
        }
        
        var response = await _api.LinkMinecraftAccountAsync(
            Current.Token,
            minecraftAccount.Uuid,
            minecraftAccount.Username
        );
        
        return response;
    }
    
    public async Task<MeResponse?> GetProfileAsync()
    {
        if (Current == null || string.IsNullOrEmpty(Current.Token))
            return null;
        
        var response = await _api.GetMeAsync(Current.Token);
        
        if (response.Success && response.Data != null)
        {
            // Update local account with Minecraft link info (or clear it if not linked)
            if (response.Data.MinecraftUuid != null)
            {
                Current.MinecraftLink = new MinecraftLink
                {
                    Uuid     = response.Data.MinecraftUuid,
                    Username = response.Data.MinecraftUsername ?? string.Empty
                };
            }
            else
            {
                Current.MinecraftLink = null;
            }
            Persist();
            return response.Data;
        }
        
        return null;
    }
    
    public INexoraApiService GetApiService()
    {
        return _api;
    }
    
    private static NexoraAccount? Load()
    {
        try
        {
            var path = AppPaths.NexoraAccountFile;
            if (System.IO.File.Exists(path))
            {
                var json = System.IO.File.ReadAllText(path);
                return JsonSerializer.Deserialize<NexoraAccount>(json, JsonOptions);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Nexora account load failed: {ex.Message}");
        }
        return null;
    }
    
    private void Persist()
    {
        try
        {
            if (Current == null) return;
            
            var path = AppPaths.NexoraAccountFile;
            var json = JsonSerializer.Serialize(Current, JsonOptions);
            System.IO.File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Nexora account save failed: {ex.Message}");
        }
    }
}
