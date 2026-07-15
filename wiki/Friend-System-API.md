# Friend System API

## POST friend-request.php

Send a friend request to another Nexora user.

**Request:**
```json
{ "token": "...", "to_username": "bob" }
```

**Response:**
```json
{ "success": true }
```

**Errors:** 404 user not found · 409 already friends or request already sent

---

## GET friend-requests.php

List incoming pending friend requests.

**Request:**
```
GET /api/launcher/friend-requests.php?token=...
```

**Response:**
```json
{
    "success": true,
    "requests": [
        { "fromUserId": 7, "fromUsername": "charlie", "sentAt": "2025-07-15T10:00:00Z" }
    ]
}
```

---

## POST accept-friend.php

Accept a pending friend request.

**Request:**
```json
{ "token": "...", "from_user_id": 7 }
```

**Response:**
```json
{ "success": true }
```

---

## POST decline-friend.php

Decline a pending friend request.

**Request:**
```json
{ "token": "...", "from_user_id": 7 }
```

**Response:**
```json
{ "success": true }
```

---

## GET friends.php

List all accepted friends.

**Request:**
```
GET /api/launcher/friends.php?token=...
```

**Response:**
```json
{
    "success": true,
    "friends": [
        {
            "userId": 7,
            "username": "charlie",
            "minecraft": { "uuid": "...", "username": "Charlie_MC" }
        }
    ]
}
```

`minecraft` is `null` if the friend has not linked a Minecraft account.
