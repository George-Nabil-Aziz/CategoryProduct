# Quickstart: Email Notifications

## Run everything (no Docker, SQL Server, SMTP or network)

```
dotnet build CategoryProduct.slnx
dotnet test CategoryProduct.slnx
```

The SQL Server project is not in the solution; build it to catch compile errors and run it only with a SQL Server:

```
dotnet build CategoryProduct.API.SqlServerTests
$env:SQLSERVER_TEST_CONNECTION = "Server=localhost,1433;User Id=sa;Password=<your local password>;TrustServerCertificate=True;"
dotnet test CategoryProduct.API.SqlServerTests --filter "FullyQualifiedName~UserEmailIndex"
```

## Targeted runs

```
dotnet test CategoryProduct.API.Tests --filter "FullyQualifiedName~Email|FullyQualifiedName~LowStockEmail|FullyQualifiedName~OrderEmail"
dotnet test CategoryProduct.MVC.Tests --filter "FullyQualifiedName~ProfileTests|FullyQualifiedName~AccountTests|FullyQualifiedName~UsersTests"
```

## See the emails locally (Pickup, the default)

1. Start the API in Development (`Email:Enabled` is true there, provider Pickup). Files go to `CategoryProduct.API/App_Data/email-outbox/` (ignored by git).
2. Register a user with an email (MVC `/Account/Register`, or `POST api/account/register` with `"email": "me@example.com"`), or set it later at `/Account/Profile`.
3. Add products to the cart and check out. A file `<timestamp>-<guid>.eml` appears; open it with a text editor or a mail client. Confirm or cancel the order as Admin and look for the next files.
4. For a low-stock mail: set an Admin's email, then post a stock movement that takes a product from at or above `Inventory:LowStockThreshold` to below it (Admin only, `POST api/product/{id}/stock-movements`).

## Compose

Email is off unless you enable it. In `.env` (never committed):

```
EMAIL_ENABLED=true
```

`docker compose up -d --build`. Mails are written to the `emailoutbox` volume (`/data/email-outbox` in the API container): `docker compose exec api ls /data/email-outbox`. To copy one out: `docker compose cp api:/data/email-outbox/<file>.eml .`.

## Optional: real SMTP

Only with your own server and credentials, supplied outside source control (user secrets in development or `.env`/environment in compose; never commit them):

```
EMAIL_ENABLED=true
EMAIL_PROVIDER=Smtp
EMAIL_FROM=<sender address your server allows>
EMAIL_SMTP_HOST=<host>
EMAIL_SMTP_PORT=587
EMAIL_SMTP_USERNAME=<user>
EMAIL_SMTP_PASSWORD=<password>
EMAIL_SMTP_ENABLESSL=true
```

Avoid `$`, quotes and spaces in values (compose interpolation). With a missing host or a misspelled provider the API logs an Error at startup and keeps writing pickup files.

Development secrets example (placeholders):

```
dotnet user-secrets set "Email:Provider" "Smtp" --project CategoryProduct.API
dotnet user-secrets set "Email:Smtp:Host" "<host>" --project CategoryProduct.API
dotnet user-secrets set "Email:Smtp:Password" "<password>" --project CategoryProduct.API
```

## Manual checks

- `GET api/account/me` with a user's token returns `email`; `PUT api/account/email` with `{ "email": "" }` clears it.
- Sending fails safely: point `Email:PickupDirectory` at a read-only or invalid path; checkout still returns 201 and the API log has an Error from the worker (recipient masked).
- Restart the API right after a checkout: queued but unsent mails are lost (Known Gap); with Pickup a send takes milliseconds, so the window is tiny.
- Cleanup: the pickup folder is never pruned automatically; delete old files by hand.
