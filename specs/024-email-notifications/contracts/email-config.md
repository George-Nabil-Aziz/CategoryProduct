# Contract: email configuration

## Settings

| Key | Env var form | Default | Meaning |
|---|---|---|---|
| `Email:Enabled` | `Email__Enabled` | `false` (code and `appsettings.json`); `true` in `appsettings.Development.json` | master switch; false means nothing is queued or written |
| `Email:Provider` | `Email__Provider` | `Pickup` | `Pickup` or `Smtp` (case-insensitive) |
| `Email:From` | `Email__From` | `noreply@categoryproduct.local` | sender address on every mail |
| `Email:PickupDirectory` | `Email__PickupDirectory` | `App_Data/email-outbox` | folder for `.eml` files; relative to `ContentRootPath`, or absolute |
| `Email:Smtp:Host` | `Email__Smtp__Host` | empty | SMTP server; required for Smtp |
| `Email:Smtp:Port` | `Email__Smtp__Port` | `587` | |
| `Email:Smtp:UserName` | `Email__Smtp__UserName` | empty | optional; credentials only used when set |
| `Email:Smtp:Password` | `Email__Smtp__Password` | empty | secret; user secrets or environment only |
| `Email:Smtp:EnableSsl` | `Email__Smtp__EnableSsl` | `true` | |

Empty string for `Enabled`, `Port` or `EnableSsl` is treated as unset (compose passes empty defaults).

## appsettings.json (tracked; no secrets)

```json
"Email": {
  "Enabled": false,
  "Provider": "Pickup",
  "From": "noreply@categoryproduct.local",
  "PickupDirectory": "App_Data/email-outbox",
  "Smtp": { "Host": "", "Port": 587, "UserName": "", "Password": "", "EnableSsl": true }
}
```

## appsettings.Development.json

```json
"Email": { "Enabled": true }
```

## docker-compose.yml (`api.environment`, added lines) and volume

```yaml
      Email__Enabled: ${EMAIL_ENABLED:-}
      Email__Provider: ${EMAIL_PROVIDER:-}
      Email__From: ${EMAIL_FROM:-}
      Email__PickupDirectory: /data/email-outbox
      Email__Smtp__Host: ${EMAIL_SMTP_HOST:-}
      Email__Smtp__Port: ${EMAIL_SMTP_PORT:-}
      Email__Smtp__UserName: ${EMAIL_SMTP_USERNAME:-}
      Email__Smtp__Password: ${EMAIL_SMTP_PASSWORD:-}
      Email__Smtp__EnableSsl: ${EMAIL_SMTP_ENABLESSL:-}
    volumes:
      - productimages:/data/product-images
      - emailoutbox:/data/email-outbox
# volumes:
  emailoutbox:
```

All optional (no `:?`). An empty `Email__Provider`/`Email__From` reads as an empty string: the code treats empty as "use the default".

## CategoryProduct.API/Dockerfile (added line next to the product-images one)

```dockerfile
RUN mkdir -p /data/email-outbox && chown $APP_UID /data/email-outbox
```

## .env.example (commented placeholders only)

```
# Optional: email notifications. Off unless EMAIL_ENABLED=true.
# Default provider writes .eml files to the emailoutbox volume (/data/email-outbox).
# EMAIL_ENABLED=true
# Optional real SMTP (own server; never commit real values):
# EMAIL_PROVIDER=Smtp
# EMAIL_FROM=CHANGE_ME_sender@example.com
# EMAIL_SMTP_HOST=CHANGE_ME_smtp_host
# EMAIL_SMTP_PORT=587
# EMAIL_SMTP_USERNAME=CHANGE_ME_smtp_user
# EMAIL_SMTP_PASSWORD=CHANGE_ME_smtp_password
# EMAIL_SMTP_ENABLESSL=true
```

Avoid `$`, quotes and spaces in values (compose interpolation), as the file already says for `SA_PASSWORD`.

## Rules

- No tracked file contains an SMTP host, user or password value (review task T033, grep).
- Development secrets: `dotnet user-secrets set "Email:Smtp:Password" "<value>" --project CategoryProduct.API` (the project has a `UserSecretsId`).
- Startup log (Information, once): provider chosen and whether enabled; never the password.
