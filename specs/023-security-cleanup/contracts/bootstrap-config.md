# Contract: bootstrap configuration

## Settings

| Key | Env var form | Meaning |
|---|---|---|
| `Bootstrap:AdminUserName` | `Bootstrap__AdminUserName` | name of the Admin to create |
| `Bootstrap:AdminPassword` | `Bootstrap__AdminPassword` | its password (never logged) |

Neither is in `appsettings.json` or `appsettings.Development.json` (Principle IV). Local development: `dotnet user-secrets set "Bootstrap:AdminUserName" ...` in `CategoryProduct.API` (it has a `UserSecretsId`).

## docker-compose.yml (`api.environment`, two added lines)

```yaml
      Bootstrap__AdminUserName: ${BOOTSTRAP_ADMIN_USERNAME:-}
      Bootstrap__AdminPassword: ${BOOTSTRAP_ADMIN_PASSWORD:-}
```

Optional: empty default, no `:?`. Empty string is "not set".

## .env.example (commented placeholders)

```
# Optional: create the first Admin on API start when no Admin exists.
# Set both, start, then remove them. Never commit .env.
# BOOTSTRAP_ADMIN_USERNAME=CHANGE_ME_admin_name
# BOOTSTRAP_ADMIN_PASSWORD=CHANGE_ME_strong_password_min_8_chars
```

Commented out so copying the file creates no account with a placeholder password. Avoid `$`, quotes and spaces in the password (compose interpolation), as the file already says for `SA_PASSWORD`.

## CI

`compose-smoke` writes its own `.env` without these keys; no workflow change.
