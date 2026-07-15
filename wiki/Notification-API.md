# Notification API

## Overview

The launcher polls two sets of notification endpoints every 30 seconds to check for incoming instance shares and tunnel shares from friends. Both are managed by their respective `NotificationManager` singletons.

---

## Instance Notifications

### GET instance-notifications.php

Returns all instance-share notifications for the authenticated user.

**Request:**
```
GET /api/launcher/instance-notifications.php?token=...
```

**Response:**
```json
{
    "success": true,
    "notifications": [
        {
            "id": 1,
            "fromUsername": "bob",
            "packName": "Bob's Fabric Pack",
            "sentAt": "2025-07-15T10:00:00Z",
            "read": false
        }
    ]
}
```

---

### POST instance-notification-read.php

Mark a single instance notification as read.

**Request:**
```json
{ "token": "...", "notification_id": 1 }
```

**Response:**
```json
{ "success": true }
```

---

### POST instance-notification-read-all.php

Mark all instance notifications as read.

**Request:**
```json
{ "token": "..." }
```

**Response:**
```json
{ "success": true }
```

---

## Tunnel Notifications

### GET tunnel-notifications.php

Returns all tunnel-share notifications for the authenticated user.

**Request:**
```
GET /api/launcher/tunnel-notifications.php?token=...
```

**Response:**
```json
{
    "success": true,
    "notifications": [
        {
            "id": 5,
            "fromUsername": "charlie",
            "serverName": "Charlie's SMP",
            "address": "something.joinmc.link:25565",
            "sentAt": "2025-07-15T11:30:00Z",
            "read": false
        }
    ]
}
```

---

### POST tunnel-notification-read.php

Mark a single tunnel notification as read.

**Request:**
```json
{ "token": "...", "notification_id": 5 }
```

---

### POST tunnel-notification-read-all.php

Mark all tunnel notifications as read.

**Request:**
```json
{ "token": "..." }
```

---

## Polling Behavior

Both `InstanceNotificationManager` and `TunnelNotificationManager`:

- Poll every **30 seconds** using `Task.Delay` in a background loop
- Start polling when a Nexora account signs in
- Stop polling (via `CancellationToken`) on sign-out
- Fire `Changed` event when new or updated notifications are detected
- `MainWindow` subscribes to `Changed` to update sidebar badge counts
