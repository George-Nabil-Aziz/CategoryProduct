# Quickstart: Password Management

## Run everything (no Docker, SQL Server, SMTP or network)

```
dotnet build CategoryProduct.slnx
dotnet test CategoryProduct.slnx
```

The SQL Server project is not in the solution; build it to catch compile errors and run it only with a SQL Server:

```
dotnet build CategoryProduct.API.SqlServerTests
$env:SQLSERVER_TEST_CONNECTION = "Server=localhost,1433;User Id=sa;Password=<your local password>;TrustServerCertificate=True;"
dotnet test CategoryProduct.API.SqlServerTests --filter "FullyQualifiedName~PasswordResetTokenIndex"
```

## Targeted runs

```
dotnet test CategoryProduct.API.Tests --filter "FullyQualifiedName~ChangePasswordTests|FullyQualifiedName~ForgotPasswordTests|FullyQualifiedName~ResetPasswordTests|FullyQualifiedName~PasswordRateLimitTests|FullyQualifiedName~RefreshTokenCleanup"
dotnet test CategoryProduct.MVC.Tests --filter "FullyQualifiedName~ForgotPasswordTests|FullyQualifiedName~ResetPasswordPageTests|FullyQualifiedName~ChangePasswordPageTests|FullyQualifiedName~AccountTests|FullyQualifiedName~ProfileTests"
```

## Try the reset flow locally (Pickup emails, the Development default)

1. Create the table: `dotnet ef database update --project CategoryProduct.API` (or start the API with `Database:MigrateOnStartup=true`).
2. Start API and MVC. In Development `Email:Enabled` is true with the Pickup provider; `App:PublicMvcBaseUrl` defaults to `http://localhost:5256`. If your MVC runs on another port, set `App:PublicMvcBaseUrl` (user secrets or environment variable `App__PublicMvcBaseUrl`) so the link points at it.
3. Register a user with an email (`/Account/Register`) or set one at `/Account/Profile`.
4. Sign out, open `/Account/Login`, click "Forgot password?", enter the user name or email. The page says the same thing for any input.
5. Open the newest file in `CategoryProduct.API/App_Data/email-outbox/` (git-ignored). It holds a live reset link: treat the folder as sensitive, development only. Open the link, choose a new password, sign in.
6. A second file `Your password was changed` appears. Old refresh tokens are revoked: another browser signed in as the same user must log in again once its access token expires (up to `Jwt:ExpiryMinutes`).
7. Change password while signed in: `/Account/Profile`, "Change password".

## API by hand (Swagger or curl)

```
POST api/account/forgot-password   { "userNameOrEmail": "alice" }                  -> 202 (always)
POST api/account/reset-password    { "token": "<from the mail>", "newPassword": "..." }   -> 204 or 400
POST api/account/change-password   (Bearer) { "currentPassword": "...", "newPassword": "..." }  -> 200 login tokens or 400
```

## Compose

Reset mails need `EMAIL_ENABLED=true` in `.env` (never committed). Set the address users open the link on:

```
EMAIL_ENABLED=true
APP_PUBLIC_MVC_BASE_URL=http://localhost:5256
# PASSWORD_RESET_TOKEN_MINUTES=30
```

`docker compose up -d --build`; read the mail with `docker compose exec api ls /data/email-outbox` and `docker compose cp api:/data/email-outbox/<file>.eml .`. The default `APP_PUBLIC_MVC_BASE_URL` is `http://localhost:${MVC_HOST_PORT:-5256}`.
