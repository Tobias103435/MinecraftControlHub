# Content Sharing API

## Instance Sharing

### POST share-instance.php

Share a `.mrpack` modpack directly with specific friends.

**Request:**
```json
{
    "token": "...",
    "recipients": ["bob", "charlie"],
    "mrpack": "<base64-encoded .mrpack contents>",
    "name": "My Modpack"
}
```

**Response:**
```json
{
    "success": true,
    "sent": ["bob"],
    "not_found": ["charlie"],
    "not_friends": []
}
```

The server verifies that each recipient is a friend of the sender before creating a notification.

---

### POST create-instance-code.php

Upload a modpack and generate a public share code.

**Request:**
```json
{
    "token": "...",
    "mrpack": "<base64-encoded .mrpack contents>",
    "name": "My Modpack"
}
```

**Response:**
```json
{ "success": true, "code": "AB12CD34" }
```

- Code is 8 characters, alphanumeric
- Valid for **7 days**
- Anyone with the code can redeem it

---

### GET redeem-instance-code.php

Download a modpack by share code.

**Request:**
```
GET /api/launcher/redeem-instance-code.php?code=AB12CD34
```

**Response:**
```json
{
    "success": true,
    "name": "My Modpack",
    "mrpack": "<base64-encoded .mrpack contents>"
}
```

**Errors:** 404 code not found · 410 code expired

---

## Tunnel Sharing

### POST share-tunnel.php

Share an active tunnel address with friends.

**Request:**
```json
{
    "token": "...",
    "recipients": ["bob"],
    "address": "whacking-unbroken.gl.joinmc.link:25565",
    "serverName": "Survival SMP"
}
```

**Response:**
```json
{
    "success": true,
    "sent": ["bob"],
    "not_found": [],
    "not_friends": []
}
```
