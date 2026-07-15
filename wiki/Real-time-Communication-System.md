# Real-time Communication System

## Overview

Nexora's real-time communication features are handled by a Node.js Socket.IO server that runs separately from the PHP Launcher API. The WPF launcher itself does **not** connect to this Socket.IO server — the real-time features (chat, presence) are available through the Nexora web platform at `nexoragames.nl/desktop`.

---

## Architecture

```
Browser (nexoragames.nl)           WPF Launcher
────────────────────               ────────────────────
Socket.IO client                   Polls PHP API (HTTP)
    │                              every 30 seconds
    │ WebSocket
    ▼
Node.js Socket.IO server
    │
    │ Redis session bridge
    ▼
PHP Sessions (Redis)
    │
    ▼
MySQL database
```

The PHP sessions created during login are also accessible by the Node.js server via Redis, so a user who logs into the website is automatically authenticated for the Socket.IO connection too.

---

## Features (Web Platform Only)

### Chat
- **Private DMs** — one-to-one messages between friends
- **Group chats** — multi-user rooms with invite support
- **World channel** — public channel visible to all logged-in users
- Messages are stored in MySQL and delivered in real time via Socket.IO

### Presence / Online Status
- Users who are connected to the Socket.IO server appear as "online" to friends
- Status updates (online, away, offline) are broadcast to all friends of the user

### Real-time Notifications
- Friend requests and friend request acceptances are pushed to connected clients immediately
- Chat message notifications are pushed to the recipient even if they are not in the chat window

---

## WPF Launcher Relationship

The WPF launcher uses **HTTP polling** (not Socket.IO) for its notifications:

| Feature | Implementation |
|---|---|
| Instance share notifications | `GET instance-notifications.php` every 30s |
| Tunnel share notifications | `GET tunnel-notifications.php` every 30s |
| Friend list | `GET friends.php` on page load |

The launcher does not maintain a persistent WebSocket connection. This is a deliberate simplicity tradeoff — the polling approach avoids the complexity of managing a Socket.IO connection in a WPF app.

---

## Related Page

- [Socket Server Architecture](Socket-Server-Architecture)
