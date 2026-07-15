# Main Window and Navigation System

## Overview

`MainWindow` is the application shell. It owns the page cache, the sidebar navigation, and the Nexora authentication flow on startup.

---

## Page Cache

```csharp
private readonly Dictionary<AppPage, UserControl> _pageCache = new();
private readonly IServiceProvider _provider;

private UserControl GetOrCreatePage(AppPage page)
{
    if (_pageCache.TryGetValue(page, out var cached)) return cached;

    var ctrl = page switch
    {
        AppPage.Home     => _provider.GetRequiredService<HomePage>(),
        AppPage.Servers  => _provider.GetRequiredService<ServersPage>(),
        AppPage.Mods     => _provider.GetRequiredService<ModsPage>(),
        AppPage.Content  => _provider.GetRequiredService<ContentPage>(),
        AppPage.Friends  => _provider.GetRequiredService<FriendsPage>(),
        AppPage.Account  => _provider.GetRequiredService<AccountPage>(),
        AppPage.Tunnel   => _provider.GetRequiredService<TunnelPage>(),
        AppPage.AI       => _provider.GetRequiredService<AiPage>(),
        AppPage.Settings => _provider.GetRequiredService<SettingsPage>(),
        _ => throw new ArgumentOutOfRangeException()
    };

    _pageCache[page] = ctrl;
    return ctrl;
}
```

Pages preserve their state between navigations because they are cached — switching away and back does not reset the ViewModel.

---

## Startup Nexora Flow

```csharp
protected override async void OnContentRendered(EventArgs e)
{
    base.OnContentRendered(e);
    await _nexoraAccountService.ValidateStoredTokenAsync();

    if (_nexoraAccountService.Current == null)
    {
        var login = new NexoraLoginWindow();
        login.ShowDialog();

        if (login.LoginSuccessful)
            OnNexoraLoginSuccessful();
        else if (login.ContinuedWithoutAccount)
            OnContinuedWithoutAccount();
    }
}
```

---

## Notification Manager Wiring

After a successful Nexora login, both notification managers start polling:

```csharp
private void OnNexoraLoginSuccessful()
{
    _instanceNotifManager.Changed += (_, _) => UpdateInstanceBadge();
    _tunnelNotifManager.Changed   += (_, _) => UpdateTunnelBadge();
    _instanceNotifManager.Start();
    _tunnelNotifManager.Start();
}
```

---

## Tunnel → Settings Navigation

`TunnelPage` can request navigation to the Settings page (e.g. when the user needs to configure a tunnel provider). It does this via an event that `MainWindow` handles:

```csharp
tunnelPage.NavigateToSettings += (_, _) => ShowPage(AppPage.Settings);
```

---

## AccountChanged Handler

`MainWindow` subscribes to `INexoraAccountService.AccountChanged` to update the sidebar's account indicator:

```csharp
_nexoraAccountService.AccountChanged += (_, _) =>
    Dispatcher.InvokeAsync(UpdateAccountIndicator);
```
