# Quickstart: Hardening Follow-ups

## Automated checks (no database needed)

```
dotnet build CategoryProduct.slnx
dotnet test CategoryProduct.slnx
dotnet ef migrations has-pending-model-changes --project CategoryProduct.API
```

New InMemory tests: `CategoryProduct.API.Tests/RefreshGraceTests.cs` and `ProductImageConcurrencyTests.cs`. All existing tests must still pass (the test factory defaults the grace to 0).

## SQL Server tests (needs a SQL Server)

Start a throwaway server (Docker host required):

```
docker run -d --name sqltest -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD=Ci-Only_Sa-Pass-1234 -p 1433:1433 mcr.microsoft.com/mssql/server:2022-latest
```

Bash:

```
export SQLSERVER_TEST_CONNECTION="Server=localhost,1433;User Id=sa;Password=Ci-Only_Sa-Pass-1234;TrustServerCertificate=True;"
dotnet test CategoryProduct.API.SqlServerTests
```

PowerShell: `$env:SQLSERVER_TEST_CONNECTION = "Server=localhost,1433;User Id=sa;Password=Ci-Only_Sa-Pass-1234;TrustServerCertificate=True;"`.

Without the variable the tests fail with a message naming it. Databases named `CategoryProductTest_*` are dropped on dispose; if a run was killed, drop leftovers manually. Remove the container with `docker rm -f sqltest`.

In CI the same tests run in the `sqlserver-tests` job; check that job on the pushed commit.

## Refresh grace window (manual, optional)

1. Login (`POST api/account/login`), call `POST api/account/refresh` with the refresh token -> 200 (new token B).
2. Immediately call refresh again with the old token -> 401.
3. Call refresh with B -> 200 (family survived).
4. Wait over 10 seconds after a rotation, replay the old token -> 401, then B -> 401 (family revoked).
5. Set `Jwt__RefreshReuseGraceSeconds=0` and repeat steps 1-3: step 3 returns 401.

## Image endpoint conflict (automated only)

The 409 path needs a race between read and save; it is covered by the interceptor tests, not by a manual walkthrough.

## MVC Data Protection (Docker host)

1. `docker compose up --build -d`, sign in at MVC.
2. `docker compose up -d --force-recreate mvc`, reload the page: still signed in.
3. `docker compose exec mvc ls /data/dp-keys` lists a `key-*.xml` file. `docker volume ls` shows `mvckeys`.
4. Without compose: `dotnet run --project CategoryProduct.MVC` with `DataProtection__KeysPath` unset behaves as before.
