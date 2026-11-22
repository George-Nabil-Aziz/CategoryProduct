# Contract: configuration

No key here is a secret. Defaults are in code; `appsettings.json` lists them for discoverability. Empty strings in compose-style passthrough must not break startup: integers are read with `GetValue<int>(key, default)` after a `string.IsNullOrWhiteSpace` check, or compose uses non-empty defaults (the 024 lesson, research R4 of 024: the generic binder throws on an empty string for an int).

| Key | Default | Meaning |
|---|---|---|
| `App:PublicMvcBaseUrl` | `http://localhost:5256` | Base URL users open from the reset mail. Absolute `http` or `https`; trailing `/` trimmed; unset, empty or invalid falls back to the default and logs a Warning. Never derived from the request. |
| `PasswordReset:TokenMinutes` | `30` | Reset token lifetime. Values <= 0 or unparsable use 30. |
| `PasswordReset:MinIntervalSeconds` | `60` | Minimum time between two reset mails for one user (an unused token created inside this window suppresses a new one, silently). `0` disables the throttle. Unparsable uses 60. |
| `RateLimiting:ForgotPasswordPermitLimit` | `5` | Requests per minute per IP for `forgot-password` (policy `forgot-password`). |
| `RateLimiting:PasswordPermitLimit` | `5` | Requests per minute per IP for `change-password` and `reset-password` (policy `password`). |
| `RefreshTokenCleanup:RetentionDays` / `IntervalMinutes` | `7` / `60` (existing) | Also govern the purge of reset tokens. |
| `Email:Enabled` (existing, 024) | `false` in code; `true` in `appsettings.Development.json` | Reset and notice mails (and reset tokens) are produced only when true. |

## appsettings.json additions

```json
"App": { "PublicMvcBaseUrl": "http://localhost:5256" },
"PasswordReset": { "TokenMinutes": 30, "MinIntervalSeconds": 60 }
```

## docker-compose.yml (api environment)

```yaml
      App__PublicMvcBaseUrl: ${APP_PUBLIC_MVC_BASE_URL:-http://localhost:${MVC_HOST_PORT:-5256}}
      PasswordReset__TokenMinutes: ${PASSWORD_RESET_TOKEN_MINUTES:-30}
```

(Nested `${...:-...}` interpolation is supported by Compose; if `docker compose config` is unavailable locally, review by eye and rely on CI `compose-smoke`.)

## .env.example additions (commented)

```
# Optional: password reset emails (need EMAIL_ENABLED=true). Address users use to open the MVC app.
# APP_PUBLIC_MVC_BASE_URL=http://localhost:5256
# PASSWORD_RESET_TOKEN_MINUTES=30
```

## Test configuration (`ApiFactory`)

`RateLimiting:ForgotPasswordPermitLimit` and `RateLimiting:PasswordPermitLimit` from new virtual properties (default 1000); `App:PublicMvcBaseUrl` set to `https://mvc.test` so tests can assert the link prefix; `PasswordReset:TokenMinutes` and `MinIntervalSeconds` left at defaults (time is advanced with the fake clock).
