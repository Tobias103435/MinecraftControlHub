# User Management API

## GET me.php

Returns the authenticated user's profile including optional Minecraft link.

**Request:**
```
GET /api/launcher/me.php?token=...
```

**Response:**
```json
{
    "success": true,
    "userId": 42,
    "username": "alice",
    "minecraft": {
        "uuid": "550e8400e29b41d4a716446655440000",
        "username": "Alice_MC"
    }
}
```

`minecraft` is `null` if no Minecraft account is linked.

---

## POST validate.php

Validates a stored launcher token. Used on startup to confirm the token is still active.

**Request:**
```json
{ "token": "..." }
```

**Response:**
```json
{ "success": true, "userId": 42, "username": "alice" }
```

**Errors:** 401 invalid/expired · 404 user not found

---

## POST link.php

Links a Minecraft account to the authenticated Nexora account.

**Request:**
```json
{
    "token": "...",
    "uuid": "550e8400e29b41d4a716446655440000",
    "username": "Alice_MC"
}
```

**Response:**
```json
{ "success": true }
```

**Validation rules:**
- UUID: 32 hex characters after normalizing (lowercase, no dashes)
- Username: 3–16 alphanumeric + underscore characters
- UUID must not already be linked to a different Nexora account (409 if conflict)

---

## POST unlink.php

Removes the Minecraft account link from the authenticated Nexora account.

**Request:**
```json
{ "token": "..." }
```

**Response:**
```json
{ "success": true }
```

**Errors:** 404 if no link exists
