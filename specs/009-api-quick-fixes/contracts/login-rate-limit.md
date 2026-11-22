# Contract: POST /api/account/login (rate limiting)

Request and success/401 responses are unchanged from `003-jwt-authentication` (and later role work).

**Policy `login`**: fixed window of 1 minute, partitioned by client IP (`"unknown"` if none),
`PermitLimit = RateLimiting:LoginPermitLimit` (default 5), `QueueLimit = 0`.

| Status | When |
|---|---|
| `200`/`401`/`400` | Within the permit limit (normal processing; every request, successful or not, uses a permit) |
| `429 Too Many Requests` | Permit limit exhausted in the current window; the request never reaches the controller |

No custom 429 body or `Retry-After` header is configured in code.

## MVC consumer

- Route: `POST /Account/Login`.
- API call: `CategoryProductApiClient.LoginAsync` -> `POST api/account/login`.
- `401` -> `null` -> "Invalid username or password."; `429` -> `LoginResult.TooManyRequests` ->
  "Too many login attempts. Try again in a minute."; other non-success statuses throw.
