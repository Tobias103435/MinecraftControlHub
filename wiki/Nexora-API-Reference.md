# Nexora API Reference

## Base URL

```
https://nexoragames.nl/api/launcher/
```

## Request Format

All requests require these headers:

```
Accept: application/json
X-Launcher-Secret: mch-launcher-2026
User-Agent: MinecraftControlHub/1.0
```

POST requests use `Content-Type: application/json`.

## Response Format

```json
{ "success": true, ...payload }
{ "success": false, "error": "..." }
```

---

## Authentication

| Method | Endpoint | Description |
|---|---|---|
| POST | `login.php` | Initiate login; returns token or 2FA challenge |
| POST | `verify-2fa.php` | Complete 2FA; returns token |
| POST | `validate.php` | Validate a stored token |
| POST | `logout.php` | Invalidate a token |

### POST login.php

```json
// Request
{ "email": "alice@example.com", "password": "..." }
// or
{ "username": "alice", "password": "..." }

// Success
{ "success": true, "token": "...", "username": "alice", "userId": 42 }

// 2FA required
{ "success": true, "twoFactorRequired": true, "method": "email", "challenge": "..." }
```

### POST verify-2fa.php

```json
// Request
{ "challenge": "...", "code": "123456" }

// Response
{ "success": true, "token": "...", "username": "alice", "userId": 42 }
```

### POST validate.php

```json
// Request
{ "token": "..." }
// Response
{ "success": true, "userId": 42, "username": "alice" }
```

---

## User Management

| Method | Endpoint | Description |
|---|---|---|
| GET | `me.php` | Get profile with Minecraft link |
| POST | `link.php` | Link a Minecraft account |
| POST | `unlink.php` | Remove the Minecraft link |

---

## Friend System

| Method | Endpoint | Description |
|---|---|---|
| POST | `friend-request.php` | Send a friend request |
| GET | `friend-requests.php` | List incoming pending requests |
| POST | `accept-friend.php` | Accept a request |
| POST | `decline-friend.php` | Decline a request |
| GET | `friends.php` | List accepted friends |

---

## Instance Sharing

| Method | Endpoint | Description |
|---|---|---|
| POST | `share-instance.php` | Share `.mrpack` with friends |
| POST | `create-instance-code.php` | Upload `.mrpack`, get 8-char share code |
| GET | `redeem-instance-code.php` | Download `.mrpack` by share code |
| GET | `instance-notifications.php` | List received notifications |
| POST | `instance-notification-read.php` | Mark one notification as read |
| POST | `instance-notification-read-all.php` | Mark all as read |

---

## Tunnel Sharing

| Method | Endpoint | Description |
|---|---|---|
| POST | `share-tunnel.php` | Share tunnel address with friends |
| GET | `tunnel-notifications.php` | List received tunnel notifications |
| POST | `tunnel-notification-read.php` | Mark one as read |
| POST | `tunnel-notification-read-all.php` | Mark all as read |

---

## Error Reference

| HTTP Status | Meaning |
|---|---|
| 400 | Missing field, invalid format, too many recipients |
| 401 | Invalid/expired token, invalid credentials, invalid 2FA code |
| 404 | User not found, resource not found |
| 409 | Already friends, duplicate request, UUID already linked |
| 410 | Share code expired |
| 429 | Rate limit exceeded |
