# Quickstart: Verifying API Quick Fixes

## Automated

```bash
dotnet test CategoryProduct.API.Tests
```

Relevant tests: `DeleteCategory_WithProducts_Returns409` (`ApiTests.cs`) and
`Login_ExceedingLimit_Returns429` (`RateLimitTests`, same file). The test factory sets
`RateLimiting:LoginPermitLimit` to 1000 by default so other tests are not throttled.

## Manual (API)

Prerequisites: a `Jwt:Key` of at least 32 characters set through user secrets or the `Jwt__Key`
environment variable (never commit it), a reachable SQL Server database, an existing admin account.

1. Run `dotnet run --project CategoryProduct.API` (Development) and open Swagger UI.
2. **Authorize button**: click Authorize, paste the token from `POST /api/Account/login`, run a
   protected call; expect it to succeed.
3. **409**: create a category, create a product in it, `DELETE /api/Category/{id}` with the token;
   expect `409` with title "Category has products". Delete the product first, then the category;
   expect success.
4. **429**: send more than 5 `POST /api/Account/login` calls within a minute from the same machine;
   the sixth returns `429`.
5. **Dependency**: `dotnet list CategoryProduct.API package --include-transitive` shows
   `System.Security.Cryptography.Pkcs` 10.0.14.

## Manual (MVC) - not exercised when this spec was written

1. Run the API and `dotnet run --project CategoryProduct.MVC`.
2. Sign in as an admin and try deleting a category that has products: the Delete page shows
   "Delete or move its products first.".
3. Submit the login form repeatedly with a wrong password: after the limit the form shows "Too many
   login attempts. Try again in a minute.".
