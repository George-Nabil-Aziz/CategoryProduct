# Quickstart: Security Cleanup

## Run everything (no Docker, SQL Server or network)

```
dotnet build CategoryProduct.slnx
dotnet test CategoryProduct.slnx
```

The SQL Server project is not in the solution; build it to catch compile errors and run it only with a SQL Server:

```
dotnet build CategoryProduct.API.SqlServerTests
$env:SQLSERVER_TEST_CONNECTION = "Server=localhost,1433;User Id=sa;Password=<your local password>;TrustServerCertificate=True;"
dotnet test CategoryProduct.API.SqlServerTests --filter "FullyQualifiedName~SeededTestUserNeutralization"
```

CI runs it in the `sqlserver-tests` job.

## Targeted runs

```
dotnet test CategoryProduct.API.Tests --filter "FullyQualifiedName~BootstrapAdminTests|FullyQualifiedName~SeededTestUserMarkerTests"
dotnet test CategoryProduct.MVC.Tests --filter "FullyQualifiedName~UsersTests|FullyQualifiedName~ProductsTests|FullyQualifiedName~ProductImageTests|FullyQualifiedName~TooManyRequestsTests"
```

## Get the first Admin (compose)

1. In `.env` (never committed) add, with your own values:
   ```
   BOOTSTRAP_ADMIN_USERNAME=<admin name, 3-50 chars: letters digits . _ ->
   BOOTSTRAP_ADMIN_PASSWORD=<strong password, at least 8 chars>
   ```
2. `docker compose up -d --build`. The API applies migrations (`testuser` is neutralized), then creates the Admin if none exists. API log: `Bootstrap admin '<name>' created.` (no password in the log).
3. Sign in at the MVC with those credentials; the Users page is available.
4. Remove the two lines from `.env` afterwards (harmless to keep: ignored while an Admin exists).

Without these variables a fresh database has no Admin and the API logs a Warning at startup.

## Recovery

If every Admin was removed, set the two variables again and restart the API: it creates the Admin because none exists. A user that already has the name is never promoted; choose another name.

## Manual check of the neutralization (optional, needs SQL Server)

Before: `SELECT UserName, Role, LEFT(PasswordHash,10) FROM Users WHERE UserName='testuser'` shows `Admin` and `AQAAAAIAAY`. Apply migrations (`dotnet ef database update --project CategoryProduct.API` or start with `Database__MigrateOnStartup=true`). After: `User` and `/w==`; `POST api/account/login` for `testuser` returns 401; its `RefreshTokens` have `RevokedAtUtc` set.

## Manual check of the 429 pages (optional)

Automated tests fake the API. By hand, put a proxy that answers 429 in front of `Api:BaseUrl`, open `/Users` and `/Products`: both show "Too many requests. Try again in a minute."
