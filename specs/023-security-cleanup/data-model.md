# Data Model: Security Cleanup

## Schema impact

**None.** No entity, property, index or relationship changes. The new migration `NeutralizeSeededTestUser` is data-only: `dotnet ef migrations add` generates empty `Up`/`Down`, and `ApplicationDbContextModelSnapshot.cs` stays unchanged (`HasPendingModelChanges()` stays false).

## Data effect of the migration

| Table.Column | Rows affected | Change |
|---|---|---|
| `Users.PasswordHash` | the row `UserName = 'testuser'` whose hash equals the seeded hash | set to `/w==` |
| `Users.Role` | same row | set to `User` |
| `RefreshTokens.RevokedAtUtc`, `ConcurrencyStamp` | unrevoked tokens of that user | `SYSUTCDATETIME()`, `NEWID()` |
| anything else | none | none; no deletes |

Foreign keys (unchanged): `Orders.UserId` Restrict, `StockMovements.UserId` SetNull, `CartItems.UserId` and `RefreshTokens.UserId` Cascade. Updates do not trigger any of them.

## Data effect of the bootstrap

One `Users` insert at most per start: `UserName` = trimmed `Bootstrap:AdminUserName`, `PasswordHash` = `PasswordHasher<User>` hash, `Role` = `Admin`. Only when no `Role = 'Admin'` row and no same-name row exists. Unique index on `UserName` is the concurrency guard.

## Production code touched

| File | Change | Behavior impact |
|---|---|---|
| `CategoryProduct.API/Migrations/<ts>_NeutralizeSeededTestUser.cs` (+ `.Designer.cs`) | NEW | seeded admin neutralized |
| `CategoryProduct.API/Data/BootstrapAdmin.cs` | NEW static helper + `BootstrapAdminOutcome` | optional first-admin creation |
| `CategoryProduct.API/Program.cs` | one awaited call after `DatabaseMigrator` | runs once per start |
| `docker-compose.yml`, `.env.example` | optional `Bootstrap__*` passthrough, commented placeholders | none unless set |
| `CategoryProduct.MVC/Services/ApiRateLimit.cs` | NEW `const Message` | text only |
| `CategoryProduct.MVC/Services/ApiErrorHandler.cs` | NEW `TooManyRequestsFilter` | 429 reads -> friendly page |
| `CategoryProduct.MVC/Program.cs` | register filter | |
| `CategoryProduct.MVC/Views/Shared/TooManyRequests.cshtml` | NEW | |
| `CategoryProduct.MVC/Controllers/UsersController.cs`, `ProductsController.cs` | 429 branches | messages |

## Types

| Type | Where | Notes |
|---|---|---|
| `User` (`Id`, `UserName`, `PasswordHash`, `Role`) | `CategoryProduct.API/Models/User.cs` | unchanged |
| `BootstrapAdminOutcome` | new, `BootstrapAdmin.cs` | `NotConfigured, Created, SkippedAdminExists, SkippedUserNameTaken, Rejected, Failed` |
| `ApiResult.TooManyRequests` | `CategoryProduct.MVC/Models/ApiModels.cs` | existing flag, unchanged |

## Test infrastructure touched

| Type | File | Change |
|---|---|---|
| `ApiFactory` | `CategoryProduct.API.Tests/ApiFactory.cs` | `protected virtual bool SeedAdmin => true` (default keeps behavior) |
| `SqlServerEnv`, `SqlServerApiFactory` | `CategoryProduct.API.SqlServerTests/SqlServerApiFactory.cs` | reused unchanged |
| `FakeApi`, `FakeResponse.Status(429)`, `HtmlHelpers` | `CategoryProduct.MVC.Tests/Support` | reused unchanged |
