# Quickstart: Validating Auth Hardening

## Prerequisites

- `Jwt:Key` set via user secrets (see `specs/003-jwt-authentication/quickstart.md`).
- After implementation, apply the migration yourself (this feature's tasks never do):
  `dotnet ef database update --project CategoryProduct.API`

## Automated

```
dotnet test
```

New/changed tests: `RefreshTokenTests` (family scoping, legacy family, concurrency conflict via
interceptor), `RefreshTokenCleanupTests` (`PurgeAsync`), `RateLimitTests` (logout `429`),
`LastAdminTests` (demote one of two admins succeeds; last admin `409`).

## Manual (API, SQL Server)

1. Log in twice as the same user (R-A1, R-B1). Refresh R-A1 to get R-A2. Replay R-A1 -> `401`;
   R-A2 -> `401`; R-B1 -> `200` (other session survives).
2. Fire two parallel refreshes with one token (two `curl` jobs): at most one `200`, the other `401`;
   the winner's new token still refreshes.
3. Call `POST /api/account/logout` more than 30 times in a minute -> `429`.
4. Create two admins; send both demotions at once (parallel `curl`): one `200`, one `409`; one admin
   remains.
5. Purge: set `RefreshTokenCleanup:IntervalMinutes` to `1` and `RetentionDays` to `1`, age some rows
   via SQL (`UPDATE RefreshTokens SET ExpiresAtUtc = DATEADD(day,-3,SYSUTCDATETIME())`), wait one
   interval, confirm those rows disappear and the Serilog log shows the purged count. Values of zero
   or less fall back to the defaults.
