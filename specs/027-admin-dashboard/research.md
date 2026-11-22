# Research: Admin dashboard

## R1. What exists today (verified at HEAD, with 025/026 treated as landed)

- `Order`: `Id, UserId, Status (OrderStatus: Pending, Confirmed, Cancelled; stored as string), Total (decimal(18,2)), CreatedAtUtc, UpdatedAtUtc, ConcurrencyStamp, Items`; index `(UserId, CreatedAtUtc)`. 026 adds `PaymentStatus` (`Unpaid`, `Paid`, `Refunded`; string, default `Unpaid`) and the `Payments` table.
- `OrderItem`: `OrderId, ProductId (int?, FK SetNull), ProductName, UnitPrice, Quantity, LineTotal`.
- `Product`: `Quantity`, `Price`; low stock = `Quantity < InventoryOptions.Effective` (`ProductController.GetLowStock`, `ToResponse`); option section `Inventory:LowStockThreshold` (default 5).
- `User`: `Id, UserName, PasswordHash, Role, Email`; no creation time. Created in `AccountController.Register` (`new Models.User { UserName, PasswordHash = "", Role = "User", Email }`), `BootstrapAdmin` (`new User { UserName, Role = "Admin", PasswordHash = "" }`), the seeded test user via migration `SeedTestUserForAuth` (neutralized by `NeutralizeSeededTestUser`), and tests (`ApiFactory` seeds `tester` admin and `plainuser`).
- Admin-only pattern: `[Authorize(Roles = "Admin")]` on `UsersController` and on `ProductController.GetLowStock`; MVC `InventoryController` has `[Authorize(Roles = "Admin")]` and `SessionExpiredAsync`; the nav shows admin links inside `User.IsInRole("Admin")` in `_Layout.cshtml`.
- No `IMemoryCache` registration and no `AddMemoryCache` exist in the API (grep at HEAD); `AddMemoryCache()` is safe to add (also registered implicitly by some framework features; double registration is harmless).
- Tests: `ApiFactory` (InMemory), `FakeApi`/`MvcTestBase` (`SignInAdminAsync`, `ApiJson`), SQL Server tests (`SqlServerEnv`, `WithDatabaseAsync` pattern from `UserEmailIndexSqlServerTests`; some migration tests migrate to an older migration and must not read full entities whose columns do not exist there).

## R2. Aggregation strategy

Options: (a) load all orders in range and aggregate in C#; (b) several focused aggregate queries; (c) one huge multi-purpose query. Chosen (b): each query returns an already-aggregated tiny result set.

| # | Query | Result rows |
|---|---|---|
| 1 | `orders.GroupBy(o => o.Status).Select(g => new { g.Key, Count = g.Count() })` | <= 3 |
| 2 | `orders.Where(PaymentStatus is Paid or Refunded).GroupBy(o => o.PaymentStatus).Select(Key, Count, Sum(Total))` | <= 2 |
| 3 | daily group (R3) | <= 366 |
| 4 | top products (R4) | <= `MaxTopProducts` |
| 5 | `Users.CountAsync(u => u.CreatedAtUtc >= fromUtc && u.CreatedAtUtc < toUtc)` (NULL never matches) | 1 |
| 6 | `Products.CountAsync(p => p.Quantity < threshold)` | 1 |

Six queries regardless of data size: no N+1. `totalOrders` is the sum of query 1 (consistent with the daily sum because all use the same filter). `Sum` over a possibly empty group of non-nullable decimal is safe in a `GroupBy` (a group always has rows); for any ungrouped sum, `SumAsync` on an empty `IQueryable<decimal>` returns 0 on SQL Server (EF emits `COALESCE(SUM(...), 0.0)`) and on InMemory. Decimal sums are exact on SQL Server `decimal(18,2)`; on InMemory they are CLR decimals.

## R3. Daily series translation (SQL Server and InMemory)

`GroupBy(o => o.CreatedAtUtc.Date)`: EF Core SQL Server translates `DateTime.Date` to `CONVERT(date, [o].[CreatedAtUtc])` and groups by it; the key comes back as `DateTime` at midnight; InMemory evaluates it in LINQ-to-Objects. Conditional sum `g.Sum(o => o.PaymentStatus == OrderPaymentStatus.Paid ? o.Total : 0m)` translates to `SUM(CASE WHEN [PaymentStatus] = N'Paid' THEN [Total] ELSE 0.0 END)` (the enum string conversion is applied to the constant). This is verified for real by the SQL Server test (T034); the fallback, if EF ever rejects it, is grouping by an anonymous `{ Year, Month, Day }` key (each part translates to `DATEPART`), then building `DateOnly` in memory. Never `AsEnumerable()` before the group (that would pull every order row). Provider-specific functions (`EF.Functions.DateDiffDay`) are avoided so the same code runs on both providers.

## R4. Top products

Query shape: `db.OrderItems.AsNoTracking().Where(i => i.ProductId != null && i.Order!.CreatedAtUtc >= fromUtc && i.Order.CreatedAtUtc < toUtc && i.Order.Status != OrderStatus.Cancelled).GroupBy(i => i.ProductId).Select(g => new { ProductId = g.Key, Name = g.Max(i => i.ProductName), Quantity = g.Sum(i => i.Quantity), Revenue = g.Sum(i => i.LineTotal) }).OrderByDescending(x => x.Quantity).ThenBy(x => x.ProductId).Take(top)`.

- The join to `Orders` is generated from the navigation inside the predicate (no `Include`, no per-row query).
- `Max(ProductName)` picks one deterministic name per product (the snapshot names could differ after a rename; `Max` is translatable on both providers, `First()` inside a group projection is less portable).
- Ties: `ThenBy(ProductId)` makes ordering total, so tests are deterministic and SQL Server paging/top has no arbitrary choice.
- Revenue here is line totals of non-cancelled orders (placed value), not paid revenue; the table header says "Sales".
- Items with NULL `ProductId` (deleted product) are excluded; including them would require grouping by name and mixing rows.

## R5. Caching

Options: none; `IMemoryCache` 60 s; response caching/ETag; distributed. Chosen `IMemoryCache`, key `dashboard:{from:yyyy-MM-dd}:{to:yyyy-MM-dd}:{top}`, 60 s absolute expiry, bypass at 0.
Why: the dashboard is polled by refresh and re-submits of the range form, the queries scan a date range, and a minute of staleness is irrelevant for analytics. Key excludes the user because only Admins reach it and the data is not user-specific. Only successful values are stored; invalid input never reaches the service. Memory bound: keys are bounded by (range, top) combinations an Admin types; entries expire in 60 s, so growth is negligible; `SizeLimit` is not configured (not needed). Tests: the shared factory sets `Dashboard:CacheSeconds=0` so no test sees stale data; `DashboardCacheTests` use a factory with a positive value and assert hit (data changed, response not) and miss (different key).
Rejected: HTTP response caching (per-client and bypassed by auth headers); distributed cache (infrastructure not in the stack).

## R6. New users: add `User.CreatedAtUtc`, nullable, no backfill

Options: (a) drop the metric; (b) add non-null `CreatedAtUtc` with SQL default `GETUTCDATE()` (existing rows get the migration time); (c) add nullable `CreatedAtUtc`, NULL for legacy rows. Chosen (c).
Reasons: the metric was requested and is cheap; (b) makes every legacy user look "new" on the day the migration runs (a false spike that corrupts the first ranges) and a `GETUTCDATE()` default also hides forgotten assignments in future code; (a) loses a KPI. (c) is honest: unknown stays unknown, legacy users are excluded, and only users created after deployment count. It is a metadata-only column add on SQL Server (fast, no table rewrite), InMemory needs no configuration, `UserResponseDto` and every existing test are untouched. Writes: `AccountController.Register` and `BootstrapAdmin` set `DateTime.UtcNow`; test seeds in `ApiFactory` may stay NULL (they are not "new users" by definition) - tests that need new users create them with explicit dates.
The migration is generated AFTER the 025 and 026 migrations exist, so ordering and snapshot are consistent (T012). Migration tests that migrate to an older migration must not read full `User` entities (the column would not exist): use projections (see 026 FR-021 for the same rule on `Order`); T013 checks `SeededTestUserNeutralizationSqlServerTests` and `MigrationTests` still pass.

## R7. Chart: inline SVG vs Chart.js

Options: (a) Chart.js from jsDelivr/cdnjs with SRI and a pinned version; (b) server-rendered inline SVG plus a table. Chosen (b).
Reasons: no external request, no SRI maintenance, no CSP change, no JavaScript to test (the MVC test suite has no browser; HTML assertions on `<svg>`/`<rect>`/`<title>` are exact), accessible by default with `<title>`, `role="img"`, `aria-label` and the table fallback; the required chart is a simple daily bar series, which SVG covers in ~30 lines of Razor. (a) would be justified only for interactivity (zoom, legends), which is not requested. Feature 028 (browser tests) can still assert the SVG and the table; if a richer chart is wanted later, switching is local to the partial. If (a) is ever chosen: cdnjs, exact version (e.g. `chart.js@4.4.1`, any version at least two weeks old), `integrity` + `crossorigin="anonymous"`, and the page keeps the table as fallback.
SVG details: `viewBox="0 0 720 160"`, `preserveAspectRatio="none"` off; bar width = 720 / days (min 1); height = value / max * 140 (max 0 -> height 0); text outside `<title>` is limited to numbers and fixed labels; bar `<title>` content is built from numbers, dates and the currency code only (no user text), and Razor encodes everything.

## R8. Range and date semantics

`DateOnly?` model binding: `yyyy-MM-dd` binds; date-times and junk fail to bind -> 400 with the key `from`/`to`. A request `from=2026-11-01&to=2026-11-07` -> `fromUtc = 2026-11-01T00:00:00Z`, `toExclusiveUtc = 2026-11-08T00:00:00Z`, `days = to - from + 1`. `DateTime` values with `Kind` Unspecified are built with `DateTime.SpecifyKind(..., Utc)` for clarity (SQL Server `datetime2` ignores kind). Today = `DateTime.UtcNow` date (via `TimeProvider` is not used anywhere in the codebase today; tests therefore seed relative to `DateTime.UtcNow` and avoid midnight flakiness by using explicit ranges for exact assertions and a `from`/`to` chosen around the current UTC date with margin). Default range: `to = today`, `from = to - (DefaultRangeDays - 1)`.

## R9. Authorization and exposure

Class-level `[Authorize(Roles = "Admin")]`. Response contains only aggregates and product names/ids; no user data. Product names are public data. 401/403 come from the existing JWT setup (no new policy). No rate limiter (A11): Admin-only, cached, bounded.

## R10. MVC form validation

Browser `<input type="date">` gives format help; the server re-validates strings: both dates parse with `DateOnly.TryParseExact("yyyy-MM-dd")`, `from <= to`, `(to - from).Days + 1 <= 366`, `top` in 1..20 (the MVC constants mirror the API defaults; if the API changes them the API 400 is still mapped to field messages). Empty form -> defaults (API applies them; the page shows the returned `from`/`to`). GET form so the URL is shareable and no anti-forgery is needed (read-only, no state change).

## R11. SQL Server test design

`DashboardSqlServerTests` (pattern: `WithDatabaseAsync`, full `MigrateAsync`): seed users, products, orders with items and every `PaymentStatus`/`Status` combination at chosen UTC dates; call `DashboardService` directly with a real context; assert sums, counts, daily series (including a day boundary and empty days), top products ordering with a tie, `newUsers` ignoring NULL `CreatedAtUtc`; register a `DbCommandInterceptor` that counts executed reader commands and assert <= 8 for one `GetSummaryAsync` with `CacheSeconds = 0`. Also a test that `AddUserCreatedAt` leaves existing rows NULL (insert a user with raw SQL without the column value after migrate, or read a seeded row through a projection). Runs in CI (`sqlserver-tests`) and on LocalDB.
