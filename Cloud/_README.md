# Cloud

Backend integration with the Nexora platform.

## Implemented (via Core/Services)

These services live in `/Core/Services` but constitute the cloud layer:

| Service | Description |
|---------|-------------|
| `NexoraApiService.cs` | Base HTTP client for the Nexora backend (`https://nexoragames.nl/api/launcher/`). Handles authentication headers and JSON parsing. |
| `NexoraAccountService.cs` | Login/logout for Nexora accounts. Persists the token to `AppSettings`. |
| `TunnelShareService.cs` | Shares an active tunnel address with friends by Nexora username. POST to `share-tunnel.php`. |
| `TunnelNotificationManager.cs` | Polls `tunnel-notifications.php` for incoming tunnel invites. Marks notifications as read. |

## Planned
- Modpack marketplace — browse and install community modpacks from the Nexora backend
- Server sync — back up server configs and worlds to the cloud
- Friends list sync — cloud-persisted friends list instead of local-only
- Real-time notifications via WebSocket instead of polling
