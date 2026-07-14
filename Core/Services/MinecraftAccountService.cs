using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MinecraftControlHub.Core.Models;

namespace MinecraftControlHub.Core.Services;

/// <summary>
/// Data shown to the user to complete the Microsoft device-code login: they open
/// <see cref="VerificationUri"/> and type <see cref="UserCode"/>.
/// </summary>
public class DeviceCodeInfo
{
    public string UserCode { get; set; } = string.Empty;
    public string VerificationUri { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class AccountResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public MinecraftAccount? Account { get; set; }
}

/// <summary>A single skin or cape entry from the Minecraft profile.</summary>
public class AppearanceItem
{
    public string Id { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public bool Active { get; set; }
    /// <summary>Skins only: "CLASSIC" or "SLIM".</summary>
    public string? Variant { get; set; }
    /// <summary>Capes only: the human-readable cape name.</summary>
    public string? Alias { get; set; }
}

/// <summary>The skins and capes attached to the signed-in Minecraft profile.</summary>
public class ProfileAppearance
{
    public List<AppearanceItem> Skins { get; set; } = new();
    public List<AppearanceItem> Capes { get; set; } = new();
}

public interface IMinecraftAccountService
{
    /// <summary>The currently signed-in account, or null when signed out.</summary>
    MinecraftAccount? Current { get; }

    /// <summary>Raised whenever <see cref="Current"/> changes (sign-in / sign-out).</summary>
    event EventHandler? AccountChanged;

    /// <summary>
    /// Runs the Microsoft device-code login flow. <paramref name="onDeviceCode"/>
    /// is invoked once the user code + verification URL are known so the UI can
    /// display them; the task completes when login finishes, fails, or is cancelled.
    /// </summary>
    Task<AccountResult> SignInAsync(IProgress<DeviceCodeInfo> onDeviceCode, CancellationToken cancellationToken = default);

    /// <summary>Signs out and clears the persisted account.</summary>
    void SignOut();

    /// <summary>
    /// Ensures the current account has a valid (non-expired) Minecraft access token,
    /// silently refreshing via the Microsoft refresh token when needed. Returns the
    /// ready-to-use account, or null when not signed in / refresh failed.
    /// </summary>
    Task<MinecraftAccount?> GetValidAccountAsync(CancellationToken cancellationToken = default);

    /// <summary>Fetches the signed-in account's skins and capes from Minecraft services.</summary>
    Task<ProfileAppearance?> GetAppearanceAsync(CancellationToken cancellationToken = default);

    /// <summary>Uploads/sets the active skin from a public image URL. <paramref name="variant"/> is "classic" or "slim".</summary>
    Task<AccountResult> ChangeSkinAsync(string url, string variant, CancellationToken cancellationToken = default);

    /// <summary>Uploads/sets the active skin from a local PNG file. <paramref name="variant"/> is "classic" or "slim".</summary>
    Task<AccountResult> ChangeSkinFromFileAsync(string filePath, string variant, CancellationToken cancellationToken = default);

    /// <summary>Sets the active cape by its id, or hides the cape when <paramref name="capeId"/> is null/empty.</summary>
    Task<AccountResult> SetActiveCapeAsync(string? capeId, CancellationToken cancellationToken = default);
}

public class MinecraftAccountService : IMinecraftAccountService
{
    // Public client ID used by Prism/MultiMC launchers — pre-configured for Xbox Live
    // OAuth scopes. Custom Azure apps must expose the same scopes or sign-in fails.
    private const string ClientId = "c36a9fb6-4f2a-41ff-90bd-ae7cc92031eb";
    // Must match Prism Launcher's MSAStep / MSADeviceCodeStep exactly (case-sensitive).
    // BUGFIX: this was "XboxLive.SignIn" (capital S, capital I). Microsoft's OAuth scopes
    // are case-sensitive against what's actually registered/consented for this client id;
    // the real, working value (confirmed against Prism/PolyMC and multiple independent
    // third-party Minecraft-auth implementations) is lowercase "signin". The capitalised
    // version is not a recognised scope for this client, which is exactly why sign-in was
    // failing with "Microsoft sign-in failed: invalid_scope".
    private const string Scope = "XboxLive.signin XboxLive.offline_access";

    private const string DeviceCodeUrl = "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode";
    private const string TokenUrl = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";
    private const string XblAuthUrl = "https://user.auth.xboxlive.com/user/authenticate";
    private const string XstsAuthUrl = "https://xsts.auth.xboxlive.com/xsts/authorize";
    private const string McLoginUrl = "https://api.minecraftservices.com/authentication/login_with_xbox";
    private const string McProfileUrl = "https://api.minecraftservices.com/minecraft/profile";
    private const string McSkinsUrl = "https://api.minecraftservices.com/minecraft/profile/skins";
    private const string McActiveCapeUrl = "https://api.minecraftservices.com/minecraft/profile/capes/active";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly HttpClient _http;

    public MinecraftAccount? Current { get; private set; }

    public event EventHandler? AccountChanged;

    public MinecraftAccountService(HttpClient http)
    {
        _http = http;
        Current = Load();
    }

    // ---------------------------------------------------------------- Sign in

    public async Task<AccountResult> SignInAsync(IProgress<DeviceCodeInfo> onDeviceCode, CancellationToken cancellationToken = default)
    {
        try
        {
            // 1) Request a device code.
            var deviceResp = await _http.PostAsync(DeviceCodeUrl, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = ClientId,
                ["scope"] = Scope
            }), cancellationToken);

            var deviceBody = await deviceResp.Content.ReadAsStringAsync(cancellationToken);
            if (!deviceResp.IsSuccessStatusCode)
                return Fail($"Microsoft device-code request failed: {deviceBody}");

            using var deviceDoc = JsonDocument.Parse(deviceBody);
            var deviceRoot = deviceDoc.RootElement;
            var deviceCode = deviceRoot.GetProperty("device_code").GetString()!;
            var userCode = deviceRoot.GetProperty("user_code").GetString()!;
            var verificationUri = deviceRoot.GetProperty("verification_uri").GetString()!;
            var message = deviceRoot.TryGetProperty("message", out var msgEl) ? msgEl.GetString() ?? string.Empty : string.Empty;
            var interval = deviceRoot.TryGetProperty("interval", out var intEl) ? intEl.GetInt32() : 5;
            var expiresIn = deviceRoot.TryGetProperty("expires_in", out var expEl) ? expEl.GetInt32() : 900;

            onDeviceCode.Report(new DeviceCodeInfo
            {
                UserCode = userCode,
                VerificationUri = verificationUri,
                Message = message
            });

            // 2) Poll for the token until the user finishes (or it times out).
            var deadline = DateTime.UtcNow.AddSeconds(expiresIn);
            string? msAccessToken = null;
            string? msRefreshToken = null;

            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(TimeSpan.FromSeconds(interval), cancellationToken);

                var tokenResp = await _http.PostAsync(TokenUrl, new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                    ["client_id"] = ClientId,
                    ["device_code"] = deviceCode
                }), cancellationToken);

                var tokenBody = await tokenResp.Content.ReadAsStringAsync(cancellationToken);
                using var tokenDoc = JsonDocument.Parse(tokenBody);
                var tokenRoot = tokenDoc.RootElement;

                if (tokenResp.IsSuccessStatusCode)
                {
                    msAccessToken = tokenRoot.GetProperty("access_token").GetString();
                    msRefreshToken = tokenRoot.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
                    break;
                }

                var error = tokenRoot.TryGetProperty("error", out var errEl) ? errEl.GetString() : null;
                if (error is "authorization_pending" or "slow_down")
                {
                    if (error == "slow_down") interval += 5;
                    continue;
                }

                // authorization_declined, expired_token, bad_verification_code, etc.
                return Fail($"Microsoft sign-in failed: {error ?? tokenBody}");
            }

            if (string.IsNullOrEmpty(msAccessToken))
                return Fail("Microsoft sign-in timed out. Please try again.");

            MinecraftAccount account;
            try
            {
                account = await BuildAccountAsync(msAccessToken, msRefreshToken, cancellationToken);
            }
            catch (AccountException aex)
            {
                return Fail(aex.Message);
            }

            SetCurrent(account);
            return new AccountResult { Success = true, Account = account };
        }
        catch (OperationCanceledException)
        {
            return Fail("Sign-in cancelled.");
        }
        catch (Exception ex)
        {
            return Fail($"Sign-in error: {ex.Message}");
        }
    }

    // ------------------------------------------------------------ Silent refresh

    public async Task<MinecraftAccount?> GetValidAccountAsync(CancellationToken cancellationToken = default)
    {
        if (Current == null)
            return null;

        if (Current.IsSignedIn)
            return Current;

        if (string.IsNullOrEmpty(Current.MicrosoftRefreshToken))
            return null;

        try
        {
            var resp = await _http.PostAsync(TokenUrl, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = ClientId,
                ["scope"] = Scope,
                ["refresh_token"] = Current.MicrosoftRefreshToken
            }), cancellationToken);

            var body = await resp.Content.ReadAsStringAsync(cancellationToken);
            if (!resp.IsSuccessStatusCode)
                return null;

            using var doc = JsonDocument.Parse(body);
            var msAccessToken = doc.RootElement.GetProperty("access_token").GetString();
            var msRefreshToken = doc.RootElement.TryGetProperty("refresh_token", out var rt)
                ? rt.GetString()
                : Current.MicrosoftRefreshToken;

            MinecraftAccount account;
            try
            {
                account = await BuildAccountAsync(msAccessToken!, msRefreshToken, cancellationToken);
            }
            catch (AccountException)
            {
                return null;
            }

            SetCurrent(account);
            return account;
        }
        catch
        {
            return null;
        }
    }

    // -------------------------------------------------------- Xbox → Minecraft

    private async Task<MinecraftAccount> BuildAccountAsync(string msAccessToken, string? msRefreshToken, CancellationToken ct)
    {
        // Xbox Live authenticate.
        var xblPayload = new
        {
            Properties = new
            {
                AuthMethod = "RPS",
                SiteName = "user.auth.xboxlive.com",
                RpsTicket = "d=" + msAccessToken
            },
            RelyingParty = "http://auth.xboxlive.com",
            TokenType = "JWT"
        };
        using var xblDoc = await PostJsonAsync(XblAuthUrl, xblPayload, ct)
            ?? throw new AccountException("Xbox Live authentication failed. Please try signing in again.");
        var xblToken = xblDoc.RootElement.GetProperty("Token").GetString()!;
        var userHash = xblDoc.RootElement.GetProperty("DisplayClaims")
            .GetProperty("xui")[0].GetProperty("uhs").GetString()!;

        // XSTS authorize.
        var xstsPayload = new
        {
            Properties = new
            {
                SandboxId = "RETAIL",
                UserTokens = new[] { xblToken }
            },
            RelyingParty = "rp://api.minecraftservices.com/",
            TokenType = "JWT"
        };
        var (xstsDoc, xstsStatus, xstsBody) = await PostJsonRawAsync(XstsAuthUrl, xstsPayload, ct);
        if (xstsDoc == null)
            throw new AccountException(DescribeXstsError(xstsStatus, xstsBody));
        using var _xsts = xstsDoc;
        var xstsToken = xstsDoc.RootElement.GetProperty("Token").GetString()!;
        var xuid = xstsDoc.RootElement.TryGetProperty("DisplayClaims", out var dc) &&
                   dc.TryGetProperty("xui", out var xui) && xui.GetArrayLength() > 0 &&
                   xui[0].TryGetProperty("xid", out var xidEl)
            ? xidEl.GetString() ?? string.Empty
            : string.Empty;

        // Minecraft login with Xbox.
        var mcPayload = new { identityToken = $"XBL3.0 x={userHash};{xstsToken}" };
        using var mcDoc = await PostJsonAsync(McLoginUrl, mcPayload, ct)
            ?? throw new AccountException("Minecraft services rejected the Xbox login. Please try again.");
        var mcAccessToken = mcDoc.RootElement.GetProperty("access_token").GetString()!;
        var expiresIn = mcDoc.RootElement.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 86400;

        // Minecraft profile (id + name). Requires the account to own the game.
        using var profileReq = new HttpRequestMessage(HttpMethod.Get, McProfileUrl);
        profileReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", mcAccessToken);
        var profileResp = await _http.SendAsync(profileReq, ct);
        var profileBody = await profileResp.Content.ReadAsStringAsync(ct);

        if (profileResp.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new AccountException(
                "This Microsoft account is signed in, but it does not own Minecraft: Java Edition " +
                "(only a Java profile can be used here). If you own Java via Xbox Game Pass, launch it " +
                "once in the official launcher to create a profile, then try again.");

        if (!profileResp.IsSuccessStatusCode)
            throw new AccountException($"Could not fetch your Minecraft profile ({(int)profileResp.StatusCode}). {Trim(profileBody)}");

        using var profileDoc = JsonDocument.Parse(profileBody);
        var profileRoot = profileDoc.RootElement;
        if (!profileRoot.TryGetProperty("id", out var idEl) || !profileRoot.TryGetProperty("name", out var nameEl))
            throw new AccountException("Your Minecraft profile response was incomplete. Please try again.");

        return new MinecraftAccount
        {
            Uuid = idEl.GetString() ?? string.Empty,
            Username = nameEl.GetString() ?? string.Empty,
            AccessToken = mcAccessToken,
            AccessTokenExpiresAt = DateTime.UtcNow.AddSeconds(expiresIn),
            MicrosoftRefreshToken = msRefreshToken ?? string.Empty,
            Xuid = xuid
        };
    }

    private async Task<JsonDocument?> PostJsonAsync(string url, object payload, CancellationToken ct)
    {
        var (doc, _, _) = await PostJsonRawAsync(url, payload, ct);
        return doc;
    }

    private async Task<(JsonDocument? Doc, int Status, string Body)> PostJsonRawAsync(string url, object payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var resp = await _http.SendAsync(request, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            System.Diagnostics.Debug.WriteLine($"POST {url} failed ({(int)resp.StatusCode}): {body}");
            return (null, (int)resp.StatusCode, body);
        }
        return (JsonDocument.Parse(body), (int)resp.StatusCode, body);
    }

    /// <summary>
    /// Turns an XSTS failure into a human-readable reason. XSTS uses the numeric
    /// <c>XErr</c> code to explain why an Xbox account can't be used.
    /// </summary>
    private static string DescribeXstsError(int status, string body)
    {
        long xErr = 0;
        try
        {
            if (!string.IsNullOrWhiteSpace(body))
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("XErr", out var xErrEl))
                    xErr = xErrEl.GetInt64();
            }
        }
        catch { /* not JSON */ }

        return xErr switch
        {
            2148916233 => "This Microsoft account has no Xbox account yet. Create one at xbox.com (sign in once) and try again.",
            2148916235 => "Xbox Live is not available in your account's country/region.",
            2148916236 or 2148916237 => "This account needs adult verification (South Korea) on xbox.com before it can be used.",
            2148916238 => "This is a child account and must be added to a Family by an adult before it can sign in.",
            _ => $"Xbox sign-in was rejected (XSTS {(xErr != 0 ? xErr.ToString() : status.ToString())}). Please try again."
        };
    }

    private static string Trim(string s) =>
        string.IsNullOrEmpty(s) ? string.Empty : (s.Length > 300 ? s[..300] : s);

    // ---------------------------------------------------------- Skins & capes

    public async Task<ProfileAppearance?> GetAppearanceAsync(CancellationToken cancellationToken = default)
    {
        var account = await GetValidAccountAsync(cancellationToken);
        if (account == null) return null;

        using var req = new HttpRequestMessage(HttpMethod.Get, McProfileUrl);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);
        var resp = await _http.SendAsync(req, cancellationToken);
        if (!resp.IsSuccessStatusCode) return null;

        var body = await resp.Content.ReadAsStringAsync(cancellationToken);
        return ParseAppearance(body);
    }

    public async Task<AccountResult> ChangeSkinAsync(string url, string variant, CancellationToken cancellationToken = default)
    {
        var account = await GetValidAccountAsync(cancellationToken);
        if (account == null) return Fail("You are not signed in with a Microsoft account.");
        if (string.IsNullOrWhiteSpace(url)) return Fail("Please provide a skin image URL.");

        try
        {
            var payload = new { variant = NormalizeVariant(variant), url };
            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var req = new HttpRequestMessage(HttpMethod.Post, McSkinsUrl) { Content = content };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);
            var resp = await _http.SendAsync(req, cancellationToken);
            var respBody = await resp.Content.ReadAsStringAsync(cancellationToken);
            return resp.IsSuccessStatusCode
                ? new AccountResult { Success = true, Account = account }
                : Fail($"Minecraft rejected the skin change ({(int)resp.StatusCode}). {Trim(respBody)}");
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    public async Task<AccountResult> ChangeSkinFromFileAsync(string filePath, string variant, CancellationToken cancellationToken = default)
    {
        var account = await GetValidAccountAsync(cancellationToken);
        if (account == null) return Fail("You are not signed in with a Microsoft account.");
        if (!File.Exists(filePath)) return Fail("The selected skin file no longer exists.");

        try
        {
            using var form = new MultipartFormDataContent
            {
                { new StringContent(NormalizeVariant(variant)), "variant" }
            };
            var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
            var fileContent = new ByteArrayContent(bytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(fileContent, "file", Path.GetFileName(filePath));

            using var req = new HttpRequestMessage(HttpMethod.Post, McSkinsUrl) { Content = form };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);
            var resp = await _http.SendAsync(req, cancellationToken);
            var respBody = await resp.Content.ReadAsStringAsync(cancellationToken);
            return resp.IsSuccessStatusCode
                ? new AccountResult { Success = true, Account = account }
                : Fail($"Minecraft rejected the skin upload ({(int)resp.StatusCode}). {Trim(respBody)}");
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    public async Task<AccountResult> SetActiveCapeAsync(string? capeId, CancellationToken cancellationToken = default)
    {
        var account = await GetValidAccountAsync(cancellationToken);
        if (account == null) return Fail("You are not signed in with a Microsoft account.");

        try
        {
            HttpRequestMessage req;
            if (string.IsNullOrWhiteSpace(capeId))
            {
                // Hide the cape.
                req = new HttpRequestMessage(HttpMethod.Delete, McActiveCapeUrl);
            }
            else
            {
                var payload = new { capeId };
                req = new HttpRequestMessage(HttpMethod.Put, McActiveCapeUrl)
                {
                    Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
                };
            }
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);
            var resp = await _http.SendAsync(req, cancellationToken);
            var respBody = await resp.Content.ReadAsStringAsync(cancellationToken);
            req.Dispose();
            return resp.IsSuccessStatusCode
                ? new AccountResult { Success = true, Account = account }
                : Fail($"Minecraft rejected the cape change ({(int)resp.StatusCode}). {Trim(respBody)}");
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    private static ProfileAppearance ParseAppearance(string profileJson)
    {
        var appearance = new ProfileAppearance();
        using var doc = JsonDocument.Parse(profileJson);
        var root = doc.RootElement;

        if (root.TryGetProperty("skins", out var skins) && skins.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in skins.EnumerateArray())
            {
                appearance.Skins.Add(new AppearanceItem
                {
                    Id = s.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty,
                    Url = s.TryGetProperty("url", out var u) ? u.GetString() ?? string.Empty : string.Empty,
                    Active = s.TryGetProperty("state", out var st) &&
                             string.Equals(st.GetString(), "ACTIVE", StringComparison.OrdinalIgnoreCase),
                    Variant = s.TryGetProperty("variant", out var v) ? v.GetString() : null
                });
            }
        }

        if (root.TryGetProperty("capes", out var capes) && capes.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in capes.EnumerateArray())
            {
                appearance.Capes.Add(new AppearanceItem
                {
                    Id = c.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty,
                    Url = c.TryGetProperty("url", out var u) ? u.GetString() ?? string.Empty : string.Empty,
                    Active = c.TryGetProperty("state", out var st) &&
                             string.Equals(st.GetString(), "ACTIVE", StringComparison.OrdinalIgnoreCase),
                    Alias = c.TryGetProperty("alias", out var a) ? a.GetString() : null
                });
            }
        }

        return appearance;
    }

    private static string NormalizeVariant(string variant) =>
        string.Equals(variant, "slim", StringComparison.OrdinalIgnoreCase) ? "slim" : "classic";

    // ---------------------------------------------------------- Sign out / state

    public void SignOut()
    {
        Current = null;
        try
        {
            if (File.Exists(AppPaths.AccountFile))
                File.Delete(AppPaths.AccountFile);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Account delete failed: {ex.Message}");
        }
        AccountChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetCurrent(MinecraftAccount account)
    {
        Current = account;
        Persist();
        AccountChanged?.Invoke(this, EventArgs.Empty);
    }

    private static MinecraftAccount? Load()
    {
        try
        {
            if (File.Exists(AppPaths.AccountFile))
            {
                var json = File.ReadAllText(AppPaths.AccountFile);
                return JsonSerializer.Deserialize<MinecraftAccount>(json, JsonOptions);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Account load failed: {ex.Message}");
        }
        return null;
    }

    private void Persist()
    {
        try
        {
            if (Current == null) return;
            var json = JsonSerializer.Serialize(Current, JsonOptions);
            File.WriteAllText(AppPaths.AccountFile, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Account save failed: {ex.Message}");
        }
    }

    private static AccountResult Fail(string error) => new() { Success = false, Error = error };
}

/// <summary>
/// Raised inside the sign-in pipeline to carry a specific, user-facing reason why
/// building the Minecraft account failed (instead of a generic "you don't own it").
/// </summary>
public class AccountException : Exception
{
    public AccountException(string message) : base(message) { }
}
