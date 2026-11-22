# Contract: Account and Users API changes (015-auth-hardening)

Request/response bodies are unchanged; only behavior and status codes below change.

## POST /api/account/login
No contract change. Internally starts a new token family (`FamilyId`).

## POST /api/account/refresh
**Rate limit**: policy `refresh` (unchanged). Body: `RefreshRequestDto { RefreshToken }`.

| Status | When | Change |
|--------|------|--------|
| `200 OK` | Valid token; rotated, successor inherits `FamilyId` | none |
| `401` | Unknown token | none |
| `401` | Revoked token replayed | sweep now revokes only active tokens with the same `UserId` and `FamilyId` (was: all of the user's) |
| `401` | Expired token / user missing | none |
| `401` | Lost a concurrent update (`DbUpdateConcurrencyException`) | NEW; no sweep |
| `429` | Rate limit exceeded | none |

## POST /api/account/logout
**Rate limit**: NEW policy `logout`, per client IP, fixed window 1 minute, default 30
(`RateLimiting:LogoutPermitLimit`). Body: `RefreshRequestDto`.

| Status | When |
|--------|------|
| `204 No Content` | Token revoked, already revoked, unknown, or lost a concurrent update (idempotent) |
| `429 Too Many Requests` | Limit exceeded (empty body from the rate limiter, same as the other policies) |

## PUT /api/users/{id}/role
**Auth**: Admin (unchanged). Statuses unchanged: `200`, `400` invalid role, `404`, `409`
`Cannot remove the last admin.`. Behavior change: the last-admin check and the update run in one
serializable transaction on SQL Server, with one retry on deadlock, so concurrent demotions yield
one `200` and one `409`.

## Background job (no HTTP surface)
`RefreshTokenCleanupWorker` purges tokens expired or revoked longer ago than `RetentionDays`, every
`IntervalMinutes`.
