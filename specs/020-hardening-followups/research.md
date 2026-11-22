# Research: Hardening Follow-ups

## R1. Why a separate project, outside the solution

**Decision**: `CategoryProduct.API.SqlServerTests`, not in `CategoryProduct.slnx`.
**Rationale**: `dotnet test CategoryProduct.slnx` (CI `build-test`, local) must keep working with no database. Tests that need SQL Server cannot be "skipped" cleanly inside the existing project without silent passes.
**Alternatives**: trait/category filter in the existing project (rejected: easy to run by accident, and the existing factory is InMemory-bound); `Skip` when env var missing (rejected: hides a misconfigured CI job).

## R2. Migrating the test database

`Program.cs` only migrates when `Database:MigrateOnStartup` is true (`Data/DatabaseMigrator.cs`, with a 10-attempt retry loop). The factory instead calls `Database.MigrateAsync()` itself in `ConfigureWebHost` before the host is built. Reason: it fails fast (one attempt) and guarantees the schema exists before `RefreshTokenCleanupWorker` (hosted service) starts. The health check inside CI relies on the `sqlcmd` service health, so the server is accepting logins first.

`HasPendingModelChanges()` (EF Core 9+, relational extension on `DatabaseFacade`) is used for "model has no pending changes". If it throws a design-time-only error in this setup, the fallback is to run `dotnet ef migrations has-pending-model-changes` as a CI step (SC-004). Not verifiable locally.

## R3. Seeded data after migrations

Migrations are not empty: `SeedTestUserForAuth` inserts `testuser`, and `AddUserRole` promotes it to Admin. Tests that count admins must delete that row first. Tests identify their own data by unique names (`Guid`) so they do not depend on row counts elsewhere.

## R4. Real concurrency approach

Requests go through `WebApplicationFactory` `HttpClient` (in-process TestServer), each parallel call started with `Task.Run` and released together via a `ManualResetEventSlim`/`TaskCompletionSource` gate. TestServer handles requests on thread-pool threads, each with its own scoped `DbContext` and real SQL Server connection, so contention is real.

Expected status sets (from code):
- Checkout (`OrdersController.Checkout`): winner 201; loser 400 (cart empty after retry), 409 (`InsufficientStock` or retries exhausted after 3 attempts).
- Stock movement (`ProductController` POST `{id}/stock-movements`, `InventoryService.ApplyMovementAsync`): 201, or 409 after 3 optimistic attempts, or 409 InsufficientStock if the change would go below zero (tests use only increases or a large initial quantity).
- Role update (`UsersController.UpdateRole`): Serializable transaction, one deadlock retry; 200, 409 "Cannot remove the last admin.", or 500 after a second deadlock.
- Refresh: winner 200, others 401.

Thread count: N = 8 for checkout/refresh, 10 for movements, 2 for demotions. Small enough for a 2-vCPU runner, large enough to interleave.

## R5. Refresh grace window semantics

Current code (`AccountController.Refresh`): token found and `RevokedAtUtc != null` -> revoke every unrevoked token of the same `UserId`+`FamilyId`, 401. A rotated token has `ReplacedByTokenHash` set by `IssueAsync`; a logout-revoked token does not. The concurrency token (`ConcurrencyStamp`) only protects requests that both read the active row before either commits; a late reader sees the revoked row and used to kill the family.

**Decision**: if rotation-revoked and younger than the grace, return 401 and do nothing else. **Rationale**: the common benign cause is two tabs/requests with the same cookie; the winner's token must survive. **Alternative rejected**: returning the winner's tokens to the loser (needs storing raw tokens or re-issuing; never issue tokens in a reuse path). **Security**: an attacker replaying a stolen old token within 10 s only gets 401; outside the window the family is revoked as today.

Existing tests: several reuse a rotated token immediately and expect revocation. Making the test factory default the grace to 0 (virtual property, like `LoginPermitLimit`) leaves them untouched; the grace tests opt in with 10.

## R6. Image endpoint concurrency

`Product.ConcurrencyStamp` is a concurrency token, but `UploadImage`/`DeleteImage` do not change it themselves, they only fail when another writer changed the row between read and save. `UploadImage` already has `catch { _storage.Delete(newFileName); throw; }`; the new specific catch runs first and returns 409. Interceptor reuse: `InventoryTests.RaceInterceptor` fires on a `Product` in Modified/Deleted state in `SavingChangesAsync` and bumps `Quantity` + `ConcurrencyStamp` through another context, which is enough because an image change marks `ImageFileName` modified. `InventoryTests.RacingFactory` is public and exposes `ImageStoragePath` via `ApiFactory`.

## R7. MVC Data Protection

`PersistKeysToFileSystem` ships in `Microsoft.AspNetCore.DataProtection`, part of the ASP.NET Core shared framework (the MVC project uses `Microsoft.NET.Sdk.Web`), so no package is added. `SetApplicationName` pins the isolation so cookie payloads are valid across container rebuilds. The runtime image runs as `$APP_UID`; a Docker named volume copies the image directory's ownership on first mount, so the Dockerfile must create and chown `/data/dp-keys` (same pattern as `/data/product-images` in the API Dockerfile). Keys are plaintext on the volume (no X.509 or DPAPI in Linux containers); declared Known Gap.

## R8. CI service container

Compose already uses `mcr.microsoft.com/mssql/server:2022-latest` and `/opt/mssql-tools18/bin/sqlcmd -C` for its health check, so the same image and flags are known to be the intended pair. GitHub does not expose the `env` context in `jobs.<id>.services.<id>.env`, so the SA password literal cannot reference a workflow-level variable there. In the `--health-cmd` option string, `$MSSQL_SA_PASSWORD` is expanded by the container's shell (the runner does not interpolate plain `$`). Both points are unverified until the first run.

Hosted-runner Docker images pull on every run (about 1.5 GB); acceptable, no caching.
