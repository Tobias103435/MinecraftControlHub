# Socket Server Architecture

## Overview

The Nexora Socket.IO server is a Node.js application that handles all real-time communication for the web platform. It authenticates connections using the PHP session stored in Redis, and routes events between connected clients.

---

## Authentication

When a browser connects to the Socket.IO server, it sends the PHP session cookie. The Node.js server reads the session from Redis to verify the user's identity — this is the same session created by the PHP login system.

```
Browser connects via WebSocket
    → handshake with session cookie
    → Node.js reads session from Redis
    → If valid session: socket.userId = session.userId
    → If invalid: socket.disconnect()
```

This means the Socket.IO server never has its own user database — it delegates entirely to the PHP authentication system.

---

## Event Types

| Event | Direction | Description |
|---|---|---|
| `chat:message` | Client → Server | Send a message to a DM, group, or world channel |
| `chat:message` | Server → Client | Receive a message |
| `chat:history` | Client → Server | Request recent message history for a channel |
| `presence:online` | Server → Client | A friend came online |
| `presence:offline` | Server → Client | A friend went offline |
| `notification:friend_request` | Server → Client | Received a new friend request |
| `notification:friend_accepted` | Server → Client | Friend request was accepted |

---

## Presence

When a user connects, the server:
1. Marks them as online in Redis
2. Broadcasts `presence:online` to all of their friends who are currently connected

When they disconnect:
1. Removes their online status from Redis
2. Broadcasts `presence:offline` to their connected friends

---

## Message Routing

Direct messages and group chats use Socket.IO rooms:

```javascript
// Each DM has a room named "dm:${smallerId}:${largerId}"
// Each group chat has a room named "group:${groupId}"
// World channel: "world"

socket.on('chat:message', ({ roomId, content }) => {
    io.to(roomId).emit('chat:message', {
        from: socket.userId,
        content,
        timestamp: Date.now()
    });
    saveToDatabase(roomId, socket.userId, content);
});
```

---

## WPF Launcher vs Web Platform

The WPF launcher does not connect to the Socket.IO server. The launcher uses HTTP polling for its notifications. The Socket.IO server is exclusively for the web platform features.
