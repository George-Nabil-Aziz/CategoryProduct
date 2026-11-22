# Data Model: Hardening Follow-ups

## Schema impact

**None.** No entity, property, index or relationship is added or changed. Confirmed: `CategoryProduct.API/Data/ApplicationDbContext.cs` is untouched by this feature and no migration is created (the newest migration stays `20261122120100_AddCartAndOrders`). `dotnet ef migrations has-pending-model-changes --project CategoryProduct.API` must still report no changes, and the SQL Server tests assert it.

## Existing schema the new tests exercise

| Behavior under test | Defined in | Expected effect |
|---|---|---|
| Unique cart line per user and product | `CartItem` index `(UserId, ProductId)` unique | second insert throws `DbUpdateException` (SqlException 2601/2627) |
| Product delete cascades to cart lines | `CartItem -> Product` `OnDelete(Cascade)` | cart rows deleted |
| Product delete cascades to movements | `StockMovement -> Product` `OnDelete(Cascade)` | movement rows deleted |
| Product delete nulls order item reference | `OrderItem` `HasOne<Product>()...OnDelete(SetNull)` | `ProductId` NULL, order and item kept |
| Order owner delete restricted | `Order -> User` `Restrict` | not tested here |
| Optimistic concurrency | `Product.ConcurrencyStamp`, `Order.ConcurrencyStamp`, `RefreshToken.ConcurrencyStamp` | losing save throws `DbUpdateConcurrencyException` |
| Seeded admin | migrations `SeedTestUserForAuth`, `AddUserRole` | `testuser` exists as Admin on every migrated DB |

## Configuration keys

| Key | Project | Default | Meaning | Env var form |
|---|---|---|---|---|
| `Jwt:RefreshReuseGraceSeconds` | API | `10` (code default; also in `appsettings.json`); `<= 0` disables | Seconds after rotation during which presenting the old refresh token yields 401 without revoking the family | `Jwt__RefreshReuseGraceSeconds` |
| `DataProtection:KeysPath` | MVC | unset (framework default key storage) | Directory for persisted Data Protection keys; app name `CategoryProduct.MVC` | `DataProtection__KeysPath` |
| `SQLSERVER_TEST_CONNECTION` | SQL tests only (env var, not config file) | none; tests fail if missing | SQL Server connection string to a server where the login may create and drop databases | n/a |

Compose additions: service `mvc` env `DataProtection__KeysPath: /data/dp-keys`; volume `mvckeys` mounted at `/data/dp-keys`; top-level `volumes: mvckeys:`.

Workflow additions: workflow-level env `SQLSERVER_CI_SA_PASSWORD` (test-only literal).

## Runtime state read by the grace window

`RefreshToken` fields: `RevokedAtUtc` (set by `Revoke`), `ReplacedByTokenHash` (set only by rotation in `IssueAsync`, null for logout and reuse-revocation), `FamilyId`. The grace path reads them and writes nothing.
