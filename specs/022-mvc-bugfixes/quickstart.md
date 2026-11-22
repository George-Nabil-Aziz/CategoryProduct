# Quickstart: MVC Bug Fixes

## Run everything (no Docker, SQL Server or network)

```
dotnet build CategoryProduct.slnx
dotnet test CategoryProduct.slnx
```

## Run only the tests of this feature

```
dotnet test CategoryProduct.MVC.Tests --filter "FullyQualifiedName~ErrorPageTests|FullyQualifiedName~UsersTests|FullyQualifiedName~ProductsTests|FullyQualifiedName~ProductImageTests|FullyQualifiedName~AccountTests|FullyQualifiedName~TokenRefreshHandlerTests|FullyQualifiedName~SessionExpiryTests|FullyQualifiedName~ApiUnavailableTests"
```

## Manual check of the error page (optional)

Provoking a real exception by hand is awkward, so the simplest manual check is to run the MVC app as Production and visit `/Home/Error` directly:

```
$env:ASPNETCORE_ENVIRONMENT = "Production"
$env:Api__BaseUrl = "http://localhost:5028/"
dotnet run --project CategoryProduct.MVC
# open http://localhost:<port>/Home/Error  -> friendly page, HTTP 500, link "Back to Categories"
# open http://localhost:<port>/Home        -> redirect to /Categories
```

The behavior behind a real exception (middleware re-execution) is covered by `ErrorPageTests`; the manual step only shows how the page looks.

## Manual check of logout (optional, needs the API running)

Sign in, then sign out; in the API log (Serilog) the `POST /api/account/logout` request now has an `Authorization` header and `POST /api/account/refresh` is not called around it. The refresh token is revoked server side (a later refresh with it returns 401).
