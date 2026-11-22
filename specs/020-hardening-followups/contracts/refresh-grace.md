# Contract: POST api/account/refresh (grace window)

Request and success response are unchanged (`RefreshRequestDto { refreshToken }` -> `200 LoginResponseDto`).

## Decision table for a presented token

| Token state | Condition | Result | Side effects |
|---|---|---|---|
| Not found | no row with the hash | 401 | none |
| Revoked by rotation | `RevokedAtUtc != null`, `ReplacedByTokenHash != null`, `grace > 0`, `now - RevokedAtUtc < grace` | **401 (new)** | none: no revoke, no save, no token issued |
| Revoked, other | everything else with `RevokedAtUtc != null` (logout-revoked, reuse-revoked, outside grace, grace = 0) | 401 | revoke all unrevoked tokens of the same user and family (unchanged) |
| Active but expired | `ExpiresAtUtc <= now` | 401 | none |
| Active, user missing | | 401 | none |
| Active | | 200 | rotate (unchanged); lost concurrency race -> 401 (unchanged) |

`grace` = `Jwt:RefreshReuseGraceSeconds` (default 10; `<= 0` means 0, i.e. disabled).

The 401 body is identical in all rows (`Unauthorized()`, empty body; status-code pages middleware may add its default body as today).
