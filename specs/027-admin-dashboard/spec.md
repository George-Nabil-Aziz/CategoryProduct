# Feature Specification: Admin dashboard (read-only analytics)

**Feature Branch**: `027-admin-dashboard`

**Created**: 2026-11-22

**Status**: Draft

**Input**: User description: "Add a read-only admin dashboard. API `GET api/dashboard/summary?from=&to=` (Admin only; default last 30 days, bounded range, UTC dates) returning order counts by status, revenue (paid orders minus refunded, using the 026 payment status), average order value, new users, low-stock product count, top products by quantity sold and a gap-free daily series of orders and revenue. All aggregation is server-side in SQL-translatable queries, no N+1, short cache. An MVC Dashboard page (Admin only, nav link) with KPI cards, a simple chart, a top products table, a low-stock link to Inventory and a validated date range form."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Summary API for admins (Priority: P1)

An Admin calls `GET api/dashboard/summary` and gets every KPI for a date range in one response, computed in the database.

**Why this priority**: it is the data behind everything else. Verified at HEAD: there is no dashboard or report endpoint; `Order` has `Status`, `Total`, `CreatedAtUtc`; `OrderItem` snapshots `ProductId?`, `ProductName`, `Quantity`, `LineTotal`; `User` has `Id, UserName, PasswordHash, Role, Email` and NO creation timestamp (`CategoryProduct.API/Models/User.cs`); `InventoryOptions.LowStockThreshold` (default 5, `Effective`) and `ProductController.GetLowStock` use `Quantity < threshold`. Feature 026 (payments) adds `Order.PaymentStatus` (`Unpaid`/`Paid`/`Refunded`) and the `Payment` entity and is implemented before this feature.

**Independent Test**: new API tests `DashboardSummaryTests` (`CategoryProduct.API.Tests`, in-memory `ApiFactory`, rows seeded directly through the `DbContext` with chosen `CreatedAtUtc` values).

**Acceptance Scenarios**:

1. **Given** an Admin and no query string, **Then** 200 with `from` = today-29 and `to` = today (UTC dates, 30 days inclusive), `days: 30`, and all fields of `contracts/dashboard-endpoints.md`.
2. **Given** an empty database (no orders, only the seeded users and products), **Then** 200 with every count and money value `0`, `averageOrderValue` `0`, `topProducts: []`, and `daily` holding one zero entry per day of the range.
3. **Given** orders created inside the range with statuses Pending/Confirmed/Cancelled, **Then** `ordersByStatus` has `pending`, `confirmed`, `cancelled` (always all three keys) and `totalOrders` is their sum; orders outside the range are not counted.
4. **Given** orders with `PaymentStatus` Paid, Unpaid and Refunded, **Then** `revenue` is the sum of `Total` of the Paid orders only, `paidOrders` their count, `refundedTotal` the sum of `Total` of the Refunded orders; Unpaid orders add nothing to either; a Refunded order is never in `revenue` (see Assumptions A2: refunds are full, so "paid minus refunded" equals "orders that are still Paid").
5. **Given** `paidOrders > 0`, **Then** `averageOrderValue` = `revenue / paidOrders` rounded to 2 decimals (away from zero); `0` when `paidOrders = 0` (no division by zero).
6. **Given** users created inside the range, **Then** `newUsers` counts them; users created before the feature (NULL creation time) are never counted (A4).
7. **Given** products with `Quantity < LowStockThreshold`, **Then** `lowStockCount` is their count and `lowStockThreshold` the effective threshold; it is a point-in-time value, independent of the range, and equals `GET api/product/low-stock` `totalCount`.
8. **Given** `currency`, **Then** it is the configured `Payments:Currency` (026), a label only.

---

### User Story 2 - Range validation and defaults (Priority: P1)

**Independent Test**: `DashboardRangeTests` (API).

**Acceptance Scenarios**:

1. **Given** `from=2026-11-01&to=2026-11-07`, **Then** 200, `days: 7`, both dates inclusive, interpreted as UTC calendar days (an order at `2026-11-07T23:59:59Z` is in, one at `2026-11-08T00:00:00Z` is out).
2. **Given** only `from`, **Then** `to` defaults to today (UTC); given only `to`, `from` defaults to `to` minus 29 days.
3. **Given** `from` later than `to`, **Then** 400 `ValidationProblem` with a `from` entry.
4. **Given** a range longer than 366 days inclusive (`DashboardOptions.MaxRangeDays`), **Then** 400 with a `to` entry; exactly 366 days is accepted.
5. **Given** unparseable dates (`from=abc`, `from=2026-13-40`, a date-time such as `2026-11-01T10:00:00`), **Then** 400 `ValidationProblem` (model binding to `DateOnly?` fails; format `yyyy-MM-dd` only).
6. **Given** `top` outside `1..MaxTopProducts` (0, -1, 21 with the default max 20) or not an integer, **Then** 400 with a `top` entry; omitted `top` uses `DefaultTopProducts` (5).
7. **Given** dates in the future (`to` after today), **Then** accepted; the future days are zero entries.

---

### User Story 3 - Daily series without gaps (Priority: P1)

**Independent Test**: `DashboardSeriesTests` (API).

**Acceptance Scenarios**:

1. **Given** orders on day 1 and day 4 of a 5-day range, **Then** `daily` has exactly 5 entries, ascending by `date` (`yyyy-MM-dd`), days 2, 3 and 5 are `{ orders: 0, revenue: 0 }`.
2. **Given** several orders on one day, **Then** that entry's `orders` counts all orders created that day (any status) and `revenue` is the sum of that day's Paid orders' `Total`; the sum of `daily[].orders` equals `totalOrders` and the sum of `daily[].revenue` equals `revenue`.
3. **Given** orders at `00:00:00Z` and `23:59:59Z` of the same day, **Then** both fall on that date (day boundary is UTC).
4. **Given** the maximum range (366 days), **Then** exactly 366 entries and the response stays small (a few tens of KB).

---

### User Story 4 - Top products (Priority: P1)

**Independent Test**: `DashboardTopProductsTests` (API).

**Acceptance Scenarios**:

1. **Given** items across orders in the range, **Then** `topProducts` lists at most `top` products ordered by `quantitySold` descending, then `productId` ascending (deterministic ties); each entry `{ productId, name, quantitySold, revenue }` where `revenue` is the sum of `LineTotal`.
2. **Given** items of Cancelled orders, **Then** they are excluded; items of Pending, Confirmed orders (paid or not) count (A3: "sold" = placed and not cancelled).
3. **Given** order items whose `ProductId` is NULL (product deleted since), **Then** they are excluded from this list (A3).
4. **Given** a product renamed after the sale, **Then** the entry shows one row per product (grouped by `ProductId`) with a name taken deterministically from the order items (the snapshot `ProductName`, maximum value) - no duplicate rows.
5. **Given** fewer than `top` distinct products, **Then** only those are returned; none: `[]`.

---

### User Story 5 - Authorization and caching (Priority: P1)

**Independent Test**: `DashboardAuthTests`, `DashboardCacheTests` (API).

**Acceptance Scenarios**:

1. **Given** no token, **Then** 401; **Given** a signed-in non-Admin, **Then** 403 (the same `[Authorize(Roles = "Admin")]` pattern as `UsersController` and `ProductController.GetLowStock`).
2. **Given** a cache window (`Dashboard:CacheSeconds`, default 60), **When** the same `(from, to, top)` is requested twice, **Then** the second answer is identical and served from `IMemoryCache` without querying again (proved by changing the data between calls: the second response is unchanged; a different range or `top` is a miss); `generatedAtUtc` is the time the cached value was computed.
3. **Given** `Dashboard:CacheSeconds = 0`, **Then** nothing is cached (every call recomputes). The shared test factory sets 0 so other tests never see stale data.
4. **Given** invalid input (400), **Then** nothing is cached and no query runs.
5. **Given** the query count, **Then** the summary needs a fixed small number of queries (at most 8) independent of the number of orders, products and days (proved against SQL Server in the SQL Server tests with a command counter).

---

### User Story 6 - New-user timestamp (Priority: P2)

The `newUsers` KPI needs a creation time that `User` does not have today.

**Independent Test**: `UserCreatedAtTests` (API), migration check in the SQL Server tests.

**Acceptance Scenarios**:

1. **Given** a successful `POST api/account/register`, **Then** the new user has `CreatedAtUtc` set to the current UTC time; the bootstrap admin created by `BootstrapAdmin` also gets it.
2. **Given** existing users when the migration runs, **Then** their `CreatedAtUtc` is NULL (unknown), they are excluded from `newUsers`, nothing else about them changes; no API response of users changes shape (`UserResponseDto` is not touched).
3. **Given** the migration `AddUserCreatedAt` is generated with `dotnet ef migrations add AddUserCreatedAt --project CategoryProduct.API`, **Then** it only adds one nullable column (no backfill value, no index).

---

### User Story 7 - MVC Dashboard page (Priority: P2)

**Independent Test**: `CategoryProduct.MVC.Tests/DashboardTests.cs` with `FakeApi` JSON and `MvcTestBase`.

**Acceptance Scenarios**:

1. **Given** a signed-in Admin, **When** `GET /Dashboard`, **Then** the page calls `api/dashboard/summary` (with `from`/`to`/`top` when given) and renders KPI cards (Orders, Revenue with currency, Average order value, New users, Low stock), an orders-by-status block, the daily chart, the top products table, and a "Low stock" card that links to `Inventory/Index`.
2. **Given** the daily series, **Then** the page renders a server-side inline SVG bar chart for orders and one for revenue (no JavaScript, no CDN), each bar with a `<title>` tooltip (`2026-11-03: 4 orders, 120.00 USD`), plus an HTML data table of the same numbers in a `<details>` element for accessibility; an all-zero series renders flat bars and a "No orders in this range" note.
3. **Given** the range form (`GET`, fields `from`, `to` as `<input type="date">`, optional `top` number 1..20, a Show button, a "Last 30 days" reset link), **Then** submitting valid values reloads with them prefilled; the form shows the effective range returned by the API.
4. **Given** invalid input (missing/invalid date format, `from` after `to`, more than 366 days, `top` out of 1..20), **Then** the page re-renders the form with field validation messages and NO API call is made for client-detectable errors; if the API still answers 400, its `ValidationProblem` errors are shown next to the fields (existing `ApiResult.Errors` pattern).
5. **Given** a non-Admin or anonymous user, **Then** the existing redirect to `/Account/AccessDenied` / login happens (`[Authorize(Roles = "Admin")]`, as `InventoryController`), and no API call is made; the nav link "Dashboard" appears only inside the existing `User.IsInRole("Admin")` block of `_Layout.cshtml`.
6. **Given** API 401, **Then** the session-expired flow (existing `SessionExpiredAsync`); API 403 -> AccessDenied; 429/5xx/unavailable -> the existing error handling (`ApiErrorHandler`), no crash.
7. **Given** HTML-significant characters in a product name (`<script>`), **Then** the page encodes them (Razor default) and the SVG `<title>` text contains no raw markup.

---

### User Story 8 - Final verification and Known Gaps (Priority: P3)

Build, three full test runs, SQL Server tests on LocalDB, Known Gaps written; CI verification stays open until the user pushes (see tasks Phase 10).

---

### Edge Cases

- Range start/end are computed as `[from 00:00:00Z, to + 1 day 00:00:00Z)`; `CreatedAtUtc` is stored as UTC by the existing code (`DateTime.UtcNow`), never local.
- A day with an order whose `PaymentStatus` is not in {Paid, Refunded} (Unpaid) counts in `orders` but not in `revenue`.
- An Admin-confirmed unpaid order (offline payment, allowed by 026 A1) adds no revenue (Known Gap).
- Refund attribution is by the ORDER creation date, not the refund date (A5); a refund of an order created before the range is invisible in a later range.
- Orders cancelled before payment are `Cancelled` + `Unpaid`: counted in `cancelled`, no revenue.
- `top` and `from`/`to` are the only inputs; no free-text input reaches a query except as parameters.
- Two concurrent first requests compute twice (no stampede lock; Known Gap, harmless: read-only).
- Time zones: all dates are UTC; the MVC says "UTC" next to the range.
- Admin role changes take effect through the existing token flow; nothing new.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: `GET api/dashboard/summary` with optional `from`, `to` (`yyyy-MM-dd`, UTC, inclusive) and `top`; `[Authorize(Roles = "Admin")]`; response per `contracts/dashboard-endpoints.md`; routes and shapes of every existing endpoint unchanged.
- **FR-002**: Defaults and limits from `DashboardOptions` (`DefaultRangeDays` 30, `MaxRangeDays` 366, `DefaultTopProducts` 5, `MaxTopProducts` 20, `CacheSeconds` 60), section `Dashboard`, invalid values fall back to the defaults (pattern of `InventoryOptions.Effective`); see `contracts/dashboard-config.md`.
- **FR-003**: KPI definitions exactly as in US1 (revenue = sum of `Total` of orders with `PaymentStatus == Paid`; refunded total = `PaymentStatus == Refunded`; both by order `CreatedAtUtc` in range).
- **FR-004**: All aggregation runs in the database through LINQ `Where/GroupBy/Sum/Count/OrderBy/Take` that translate on both providers; no `AsEnumerable()`/`ToList()` before grouping; only the already-aggregated rows (at most 366 day rows, at most `MaxTopProducts` product rows, a few status rows) are materialized; empty days are filled in memory from those rows. Detailed in `research.md` R2-R4.
- **FR-005**: No N+1: a fixed number of queries per request (<= 8), no per-day or per-product query, no lazy loading, `AsNoTracking`, projections only (no `Include`).
- **FR-006**: `DashboardService` (scoped) holds the queries and the cache; the controller validates input and maps to DTOs; the service is usable without HTTP (SQL Server tests call it directly).
- **FR-007**: Caching with `IMemoryCache`, key `dashboard:{from}:{to}:{top}`, absolute expiration `CacheSeconds`, disabled at 0; only successful results are cached.
- **FR-008**: `User.CreatedAtUtc` (`DateTime?`, NULL for legacy rows) set on registration and bootstrap admin; one generated migration `AddUserCreatedAt`; `UserResponseDto` unchanged (research R6).
- **FR-009**: MVC `DashboardController` (`[Authorize(Roles = "Admin")]`, GET `Dashboard`), `DashboardModels` (MVC's own records tolerant of missing fields), `CategoryProductApiClient.GetDashboardSummaryAsync`, views `Dashboard/Index.cshtml` and partial `_DailyBarChart.cshtml`, nav link; MVC references no API type (Principle V).
- **FR-010**: The chart is server-rendered inline SVG with an accessible table; no script tag, no CDN, no new static asset (research R7).
- **FR-011**: MVC date-range validation mirrors the API (format, order, 366 days, `top` 1..20) with messages next to the fields, and maps API 400 errors.
- **FR-012**: Read-only: the feature adds no write endpoint and changes no order, product or payment data.
- **FR-013**: Existing behavior unchanged: every existing test passes without edits except the additive `ApiFactory` setting `Dashboard:CacheSeconds=0` and additive helpers.
- **FR-014**: No secrets, no new package, no new JavaScript library.
- **FR-015**: Tests per story (API, MVC, SQL Server) as listed in `tasks.md`; the SQL Server test proves translation/execution of every dashboard query on a real SQL Server and the query count.
- **FR-016**: Logging: Information with range, `top`, duration and cache hit/miss; no data values.

### Key Entities

- **DashboardSummary** (not persisted): response aggregate described in `data-model.md`.
- **User** (changed): gains nullable `CreatedAtUtc`.
- No other table changes. Read models: `Order` (026 `PaymentStatus`), `OrderItem`, `Product`, `User`.

## Success Criteria *(mandatory)*

- **SC-001**: An Admin gets every KPI for any allowed range in one request with a fixed, small number of queries, verified on SQL Server.
- **SC-002**: Revenue, refunded total, status counts, daily sums and top products are exact in tests that seed paid, unpaid, refunded and cancelled orders.
- **SC-003**: Non-admins and anonymous callers never receive data (403/401, AccessDenied redirect).
- **SC-004**: Invalid ranges are rejected with field-level errors in both API and MVC.
- **SC-005**: `dotnet test CategoryProduct.slnx` is green three runs in a row and the SQL Server tests pass on LocalDB.
- **SC-006**: Existing features behave exactly as before.

## Out of Scope

- Exports (CSV/PDF), saved reports, comparison with a previous period, per-user or per-category breakdowns, real-time updates, drill-down pages.
- Browser/E2E tests of the page (feature 028); a JavaScript chart library.
- Revenue by payment date, partial refunds, taxes, shipping, multi-currency conversion.
- Auditing of dashboard access, per-admin rate limiting.
- Backfilling `User.CreatedAtUtc` for existing users.

## Assumptions

- **A1 (025/026 timing)**: feature 025 (password management, being implemented concurrently) and 026 (payments, documented, implemented before 027) change shared files (`Program.cs`, `ApiFactory.cs`, `ApplicationDbContext.cs`, `OrdersController.cs`, `Order.cs`, `NotificationService.cs`, `appsettings.json`, migrations). This spec treats 026's `Order.PaymentStatus` (`OrderPaymentStatus.Unpaid/Paid/Refunded`) and `Payment` as present; the implementer re-reads those files first (tasks T002).
- **A2 (revenue)**: revenue = `Sum(Order.Total) where PaymentStatus = Paid`. Because 026 refunds fully and sets the order to `Refunded` when a paid order is cancelled, "paid minus refunded" is exactly the set of orders still `Paid`; `refundedTotal` is reported separately for visibility. The `Payment` table is not queried (the order column is the denormalized truth maintained in the same save as the payment, 026 FR-017), which keeps the query a single-table aggregate.
- **A3 (top products)**: "sold" = order items of orders with `Status != Cancelled` in the range, regardless of payment status (stock is reserved at checkout); items with NULL `ProductId` are excluded; grouped by `ProductId`.
- **A4 (new users)**: add a nullable `User.CreatedAtUtc` with migration `AddUserCreatedAt`, no backfill, instead of dropping the metric or defaulting old rows to the migration time (which would show all legacy users as "new" on deploy day) - research R6.
- **A5 (date attribution)**: every date-based figure uses `Order.CreatedAtUtc` (orders, revenue, daily series, top products), not payment or refund time; simple, single table, consistent totals between cards and chart (sum of daily equals the card).
- **A6 (range)**: dates are UTC calendar days, inclusive, `yyyy-MM-dd`; default last 30 days ending today; max 366 days; future days allowed; `top` default 5, max 20.
- **A7 (cache)**: 60 s `IMemoryCache` keyed by `(from, to, top)`; staleness up to 60 s is accepted for an analytics page; per-process (no distributed cache); disabled in tests by config.
- **A8 (chart)**: inline SVG, no JavaScript - research R7; feature 028's browser tests can assert the SVG/table.
- **A9 (low stock)**: uses `InventoryOptions.Effective` and `Quantity < threshold` exactly like `ProductController.GetLowStock`.
- **A10 (response currency)**: `currency` echoes `Payments:Currency` from 026 (default `USD`); no conversion.
- **A11 (rate limiting)**: no new limiter; the endpoint is Admin-only and cached.
- **A12 (indexes)**: no new index (YAGNI at the current scale); `Orders` has `(UserId, CreatedAtUtc)` only, so a range scan is a table scan; recorded as a Known Gap with the remedy (`IX_Orders_CreatedAtUtc` in a later migration).

## Known Gaps

(To be completed in Phase 10: final test counts, whether the SQL Server tests ran on LocalDB or only CI, migration name, deviations, and the CI run for this feature, which stays open until the user pushes.)

- No index supports date-range scans on `Orders` (A12).
- Legacy users have no creation date and are never counted as new (A4).
- Offline (Admin-confirmed, unpaid) orders add no revenue (A2).
- Refunds are attributed to the order date (A5).
- The per-process cache is not shared between API instances and has no stampede protection (A7).
- CI verification: open.
