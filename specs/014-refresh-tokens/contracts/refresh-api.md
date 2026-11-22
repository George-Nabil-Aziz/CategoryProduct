# Contract: Refresh / Logout API (addition to Account API)

Base route: `api/Account`.

## Changed: POST /api/account/login

Unchanged request and failures (see `003-jwt-authentication`). Success `200 OK` body is now:

```json
{ "token": "...", "expiresAtUtc": "...", "role": "User",
  "refreshToken": "...", "refreshTokenExpiresAtUtc": "..." }
```

## POST /api/account/refresh

**Auth**: none (the refresh token is the credential). **Rate limit**: policy `refresh` (per client
IP, 1-minute window, default 30).

**Request body**: `RefreshRequestDto { RefreshToken: string }`

| Status | When | Body |
|--------|------|------|
| `200 OK` | Token valid, unrevoked, unexpired, user exists | New `LoginResponseDto` (new access token + new refresh token); old token revoked and linked |
| `401 Unauthorized` | Unknown token | empty |
| `401 Unauthorized` | Token already revoked (reuse); all the user's unrevoked refresh tokens are revoked | empty |
| `401 Unauthorized` | Token expired, or its user no longer exists | empty |
| `429 Too Many Requests` | Rate limit exceeded | rate limiter default |

## POST /api/account/logout

**Auth**: none. **Rate limit**: none.

**Request body**: `RefreshRequestDto { RefreshToken: string }`

| Status | When |
|--------|------|
| `204 No Content` | Always; the token is revoked if found and not already revoked |

Access tokens already issued remain valid until their own expiry.

## Internal contract: MVC to API

`TokenRefreshHandler` calls `POST api/account/refresh` with `{ "refreshToken": ... }` through the
named client `api-refresh`; `CategoryProductApiClient.LogoutAsync` calls `POST api/account/logout`
(best effort, exceptions swallowed).
