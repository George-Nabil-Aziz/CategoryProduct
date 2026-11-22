---
description: "Task list for 027-admin-dashboard"
---

# Tasks: Admin dashboard

**Input**: Design documents from `/specs/027-admin-dashboard/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md. Features 025 and 026 are implemented before this one.

**Tests**: Requested. Each story writes its failing tests first, then the change. No fixed sleeps; time-dependent tests seed explicit dates and use explicit `from`/`to`.

**Organization**: Primitives (options, DTOs) and the model/migration are the foundation; US1-US4 (summary API) share one service and endpoint; US5 (auth, cache) finishes the API; US7 (MVC) builds on the contract; US6 (timestamp) is part of the foundation; US8 (final) last.

## File ownership (for parallel work)

| Area | Production files | Test files |
|---|---|---|
| Primitives | `API/Services/DashboardOptions.cs`, `API/Dtos/DashboardDtos.cs`, `API/appsettings.json`, `API/Program.cs` (options, cache) | `APIT/DashboardOptionsTests.cs` |
| Model + migration (US6) | `API/Models/User.cs`, `API/Controllers/AccountController.cs`, `API/Data/BootstrapAdmin.cs`, `API/Data/ApplicationDbContext.cs` (only if needed), `API/Migrations/*AddUserCreatedAt*` | `APIT/UserCreatedAtTests.cs` |
| Summary API (US1-US4) | `API/Services/DashboardService.cs`, `API/Controllers/DashboardController.cs`, `API/Program.cs` (service) | `APIT/{DashboardTestSupport,DashboardSummaryTests,DashboardRangeTests,DashboardSeriesTests,DashboardTopProductsTests}.cs`, `APIT/ApiFactory.cs` |
| Auth + cache (US5) | `API/Services/DashboardService.cs` (cache part) | `APIT/{DashboardAuthTests,DashboardCacheTests}.cs` |
| MVC (US7) | `MVC/Models/DashboardModels.cs`, `MVC/Services/CategoryProductApiClient.cs`, `MVC/Controllers/DashboardController.cs`, `MVC/Views/Dashboard/*`, `MVC/Views/Shared/_Layout.cshtml` | `MVCT/DashboardTests.cs`, `MVCT/Support/ApiJson.cs` |
| SQL Server | none | `SQLT/DashboardSqlServerTests.cs` |

Shared files (`Program.cs`, `ApiFactory.cs`, `AccountController.cs`, `ApplicationDbContext.cs`, `appsettings.json`, `CategoryProductApiClient.cs`, `_Layout.cshtml`) are edited sequentially by one agent at a time.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency)
- **[Story]**: US1..US8 from spec.md
- Paths relative to the repository root. `API` = `CategoryProduct.API`, `APIT` = `CategoryProduct.API.Tests`, `SQLT` = `CategoryProduct.API.SqlServerTests`, `MVC` = `CategoryProduct.MVC`, `MVCT` = `CategoryProduct.MVC.Tests`.

---

## Phase 1: Setup

- [ ] T001 Record the baseline: `dotnet build CategoryProduct.slnx` and `dotnet test CategoryProduct.slnx` (note the test counts); `git status`; confirm 025 and 026 are implemented (the `AddPasswordResetTokens` and `AddPayments` migrations exist; `Order.PaymentStatus` exists); note the branch.
- [ ] T002 [P] Re-read, after 025/026 landed, the files this feature edits and re-verify the spec's code claims: `API/Program.cs`, `APIT/ApiFactory.cs`, `API/Controllers/AccountController.cs` (Register), `API/Data/BootstrapAdmin.cs`, `API/Data/ApplicationDbContext.cs`, `API/Models/{User,Order,OrderItem,Product}.cs`, `API/Models/OrderPaymentStatus.cs` (exact enum and `TryParseName` names), `API/Controllers/OrdersController.cs` (how other controllers validate query parameters and produce `ValidationProblem`), `API/Services/{InventoryOptions,PaymentOptions}.cs`, `appsettings.json`, `MVC/Services/CategoryProductApiClient.cs` (`SendAsync<T>`, `ApiResult`), `MVC/Controllers/InventoryController.cs`, `MVC/Views/Shared/_Layout.cshtml`, `MVCT/Support/{ApiJson,FakeApi,MvcTestBase}.cs`, SQL Server tests (`SqlServerEnv`, `MigrationTests`, `SeededTestUserNeutralizationSqlServerTests`). Adjust names in this task list if they changed (see the final note).

---

## Phase 2: Primitives - options and DTOs (foundation)

**Independent Test**: `dotnet test CategoryProduct.API.Tests --filter "FullyQualifiedName~DashboardOptionsTests"`

- [ ] T003 [P] [US5] Write `APIT/DashboardOptionsTests.cs` FIRST: defaults when the section is missing (30, 366, 5, 20, 60); invalid values fall back (`DefaultRangeDays` 0 or above max, `MaxRangeDays` 0, `DefaultTopProducts` above max, `MaxTopProducts` 0, `CacheSeconds` negative -> 60, 0 stays 0); hard ceilings (3660, 100); per `contracts/dashboard-config.md`.
- [ ] T004 [US5] Create `API/Services/DashboardOptions.cs` (`SectionName = "Dashboard"`, raw properties, `Effective*` accessors). (FR-002)
- [ ] T005 [P] [US1] Create `API/Dtos/DashboardDtos.cs`: `DashboardSummaryDto`, `OrdersByStatusDto`, `TopProductDto`, `DailyPointDto` per `data-model.md`. (FR-001)
- [ ] T006 [US5] Edit `API/Program.cs` (re-read first): `builder.Services.Configure<DashboardOptions>(builder.Configuration.GetSection("Dashboard"))` next to `InventoryOptions`, and `builder.Services.AddMemoryCache()`. (FR-002, FR-007)
- [ ] T007 [P] [US5] Edit `API/appsettings.json`: add the `Dashboard` section with the documented defaults (numbers only). (FR-002)

---

## Phase 3: New-user timestamp and migration (US6)

**Independent Test**: `dotnet test CategoryProduct.API.Tests --filter "FullyQualifiedName~UserCreatedAtTests"`

- [ ] T008 [US6] Write `APIT/UserCreatedAtTests.cs` FIRST: register sets `CreatedAtUtc` within a few seconds of `DateTime.UtcNow` (read through a scope `DbContext`); the bootstrap-admin path (`BootstrapAdminTests` helpers) sets it; the seeded users of `ApiFactory` may be NULL; `UserResponseDto` JSON (register response, `GET api/users`) has no `createdAtUtc` member.
- [ ] T009 [US6] Edit `API/Models/User.cs`: `public DateTime? CreatedAtUtc { get; set; }`. (FR-008)
- [ ] T010 [US6] Edit `API/Controllers/AccountController.cs` (Register: `CreatedAtUtc = DateTime.UtcNow` in the object initializer) and `API/Data/BootstrapAdmin.cs` (same). Re-read both first (025 touched `AccountController`). (FR-008)
- [ ] T011 [US6] Check `API/Data/ApplicationDbContext.cs`: a plain `DateTime?` needs no configuration; add none unless the model snapshot demands (YAGNI). Verify no `HasData` user seed exists that would now need the column.
- [ ] T012 [US6] Generate the migration AFTER the 025 and 026 migrations: `dotnet ef migrations add AddUserCreatedAt --project CategoryProduct.API`. Open it: it must only `AddColumn<DateTime>(name: "CreatedAtUtc", table: "Users", type: "datetime2", nullable: true)` (and the Down drop); nothing hand-edited; snapshot regenerated by the tool. (FR-008, Principle II)
- [ ] T013 [US6] Confirm migration-based tests still hold: `dotnet build CategoryProduct.API.SqlServerTests`; read `MigrationTests` and `SeededTestUserNeutralizationSqlServerTests` for any full `User` entity read at an OLDER migration (the column would not exist there) and switch such reads to projections if found; run `MigrationStartupTests` (API tests, InMemory).
- [ ] T014 [US6] Run `UserCreatedAtTests` and `dotnet test CategoryProduct.API.Tests` (nothing else changed).

---

## Phase 4: Summary API (US1, US2, US3, US4)

**Independent Test**: `dotnet test CategoryProduct.API.Tests --filter "FullyQualifiedName~DashboardSummaryTests|FullyQualifiedName~DashboardRangeTests|FullyQualifiedName~DashboardSeriesTests|FullyQualifiedName~DashboardTopProductsTests"`

- [ ] T015 [US1] Edit `APIT/ApiFactory.cs` (re-read first; 025/026 changed it): add setting `Dashboard:CacheSeconds` = `0`. Create `APIT/DashboardTestSupport.cs`: helpers to sign in as the seeded admin and a plain user (reuse the existing helpers of `CartOrderTestHelpers`/`ApiFactory`), `SeedOrderAsync(userId, createdAtUtc, OrderStatus, OrderPaymentStatus, total, params (productId, name, qty, unitPrice)[])` via a scope `DbContext` (sets `Total` = sum of line totals, `UpdatedAtUtc`), `SeedUserAsync(createdAtUtc?)`, `GetSummaryAsync(client, query)` returning the parsed JSON, and a database reset between tests if `ApiFactory` shares state (check how other tests isolate; do not delete the seeded users/products).
- [ ] T016 [P] [US1] Write `APIT/DashboardSummaryTests.cs` FIRST (US1 scenarios 1-8): defaults and shape; empty database zeros with 30 daily zero entries and `topProducts: []`; status counts incl. orders outside the range excluded; revenue with Paid, Unpaid and Refunded orders (exact decimals, e.g. 100.10 + 50.25), `paidOrders`, `refundedTotal`; average order value rounding (e.g. 100.00 / 3 = 33.33) and 0 with no paid orders; `newUsers` with in-range, out-of-range and NULL users; `lowStockCount` equals `GET api/product/low-stock` `totalCount` (seed products at/under/over the threshold, boundary `Quantity == threshold` is NOT low); `currency` equals the configured value.
- [ ] T017 [P] [US2] Write `APIT/DashboardRangeTests.cs` FIRST (US2): inclusive UTC boundaries (23:59:59Z in, next day 00:00:00Z out); defaults when only `from` or only `to`; `from > to` -> 400 key `from`; 367 days -> 400 key `to`, 366 accepted; invalid formats (`abc`, `2026-13-40`, `2026-11-01T10:00:00`, empty-string) -> 400; `top` 0, -1, 21, `x` -> 400 key `top`, omitted -> 5, 20 accepted; future `to` accepted with zero days.
- [ ] T018 [P] [US3] Write `APIT/DashboardSeriesTests.cs` FIRST (US3): gaps filled in a 5-day range; ascending dates; same-day multiple orders (any status) count, revenue counts Paid only; boundary times on one date; invariants `sum(daily.orders) = totalOrders` and `sum(daily.revenue) = revenue`; 366-day range has 366 entries.
- [ ] T019 [P] [US4] Write `APIT/DashboardTopProductsTests.cs` FIRST (US4): ordering by quantity desc, tie broken by `productId` asc; `top` cuts the list; Cancelled orders' items excluded, Pending/Confirmed/Unpaid/Paid included; items with NULL `ProductId` excluded; renamed product yields one row (two items with different snapshot names -> one entry, name = the maximum string); revenue = sum of `LineTotal`; fewer products than `top`; none -> `[]`.
- [ ] T020 [US1] Create `API/Services/DashboardService.cs` (scoped; ctor `ApplicationDbContext`, `IOptions<DashboardOptions>`, `IOptions<InventoryOptions>`, `IOptions<PaymentOptions>` (026; currency), `IMemoryCache`, `ILogger`): `GetSummaryAsync(DateOnly from, DateOnly to, int top, CancellationToken)` with the six queries of `research.md` R2-R4 (all `AsNoTracking`, projections, no `Include`, no `ToList` before grouping), the in-memory gap fill, the average rounding (`Math.Round(..., 2, MidpointRounding.AwayFromZero)`), `GeneratedAtUtc = DateTime.UtcNow`. Use the real enum names from T002 (`OrderPaymentStatus.Paid/Refunded`). Cache comes in T026; write the compute method as a separate private method now. (FR-003, FR-004, FR-005, FR-006)
- [ ] T021 [US1] Create `API/Controllers/DashboardController.cs`: `[ApiController]`-consistent with the others (T002), `[Route("api/[controller]")]`, `[Authorize(Roles = "Admin")]`, `[HttpGet("summary")] Summary(DateOnly? from, DateOnly? to, int? top, CancellationToken ct)`; compute defaults from options and "today UTC", validate (400 `ValidationProblem` keys `from`/`to`/`top`), call the service, return `Ok(dto)`. A model-binding failure must also be a 400 with a `from`/`to` key (verify what the framework emits and, if needed, bind as `string?` and parse with `DateOnly.TryParseExact("yyyy-MM-dd")` so the key and message are stable). (FR-001, FR-002)
- [ ] T022 [US1] Edit `API/Program.cs`: `builder.Services.AddScoped<DashboardService>()` (re-read first). Make sure the `/api/dashboard` routes are not caught by any existing fallback.
- [ ] T023 [US1] Run the Phase 4 tests until green; fix translation problems by following the fallbacks in `research.md` R3, never by adding client evaluation of a whole table.

---

## Phase 5: Authorization and caching (US5)

**Independent Test**: `dotnet test CategoryProduct.API.Tests --filter "FullyQualifiedName~DashboardAuthTests|FullyQualifiedName~DashboardCacheTests"`

- [ ] T024 [P] [US5] Write `APIT/DashboardAuthTests.cs` FIRST: anonymous 401; plain user 403 (also with a valid range); Admin 200; a token of a user whose role was demoted keeps the existing behavior (no new logic).
- [ ] T025 [P] [US5] Write `APIT/DashboardCacheTests.cs` FIRST using a factory subclass that sets `Dashboard:CacheSeconds=60`: first call, then insert an order, second identical call returns the same `generatedAtUtc` and unchanged totals (hit); a different `to` or `top` shows the new order (miss); invalid input does not populate the cache (a later valid call computes); with `CacheSeconds=0` (default factory) a changed database is reflected immediately.
- [ ] T026 [US5] Add the cache to `DashboardService`: key `dashboard:{from:yyyy-MM-dd}:{to:yyyy-MM-dd}:{top}`, `TryGetValue` / compute / `Set` with `AbsoluteExpirationRelativeToNow = EffectiveCacheSeconds`, bypass when 0; log Information `Dashboard summary from=... to=... top=... cache=hit|miss elapsedMs=...` (no data values). Run Phase 5 tests. (FR-007, FR-016)

---

## Phase 6: MVC Dashboard page (US7)

**Independent Test**: `dotnet test CategoryProduct.MVC.Tests --filter "FullyQualifiedName~DashboardTests"`

- [ ] T027 [US7] Write `MVCT/DashboardTests.cs` FIRST and add `ApiJson.DashboardSummary(...)` (additive, defaults for every field) to `MVCT/Support/ApiJson.cs`: Admin `GET /Dashboard` calls `api/dashboard/summary` and renders KPI values (orders, revenue with currency, average, new users, low stock count) and the Inventory link (`href` contains `/Inventory`); orders-by-status block; SVG present with one `<rect>` per day and `<title>` text for a known day; data table rows; top products rows (and the encoded `<script>` product name); empty state notes (`No orders in this range.`, `No sales in this range.`); query string forwarded (`?from=2026-11-01&to=2026-11-07&top=3` appears in the recorded API request) and the form prefilled; invalid inputs (bad format, `from > to`, 367 days, `top=0`/`21`) show messages and record NO API request; API 400 `ValidationProblem` JSON mapped to field messages; API 401 -> sign out redirect; API 403 -> AccessDenied; API 429/500/unavailable -> no crash (existing patterns, see `OrdersTests`/`ApiUnavailableTests`); non-Admin and anonymous -> redirect (AccessDenied/login) and no API request; nav link "Dashboard" present for Admin and absent for a plain user.
- [ ] T028 [US7] Create `MVC/Models/DashboardModels.cs` (own records tolerant of missing fields: defaults 0/empty lists; `DashboardQueryModel`, `DashboardPageModel`). (FR-009)
- [ ] T029 [US7] Edit `MVC/Services/CategoryProductApiClient.cs`: `GetDashboardSummaryAsync(string? from, string? to, int? top)` following the existing `SendAsync<T>`/`ApiResult` pattern; omit empty parameters. (FR-009)
- [ ] T030 [US7] Create `MVC/Controllers/DashboardController.cs`: `[Authorize(Roles = "Admin")]`, `Index(string? from, string? to, int? top)`; pre-validation (format `yyyy-MM-dd`, order, 366 days, `top` 1..20) adding `ModelState` errors with keys `from`/`to`/`top` and rendering the form without an API call; map `ApiResult` outcomes per `contracts/dashboard-page.md` (401 -> `SessionExpiredAsync` copied from `InventoryController`). (FR-009, FR-011)
- [ ] T031 [US7] Create `MVC/Views/Dashboard/Index.cshtml` and `MVC/Views/Dashboard/_DailyBarChart.cshtml` per `contracts/dashboard-page.md` and research R7 (inline SVG, no script tag, `<title>` per bar, table in `<details>`, KPI card ids, UTC note). Razor-encode everything; build SVG numbers with `CultureInfo.InvariantCulture` (the decimal separator must never be a comma inside SVG attributes). (FR-010)
- [ ] T032 [US7] Edit `MVC/Views/Shared/_Layout.cshtml`: Dashboard nav link inside the Admin block. (FR-009)
- [ ] T033 [US7] Run MVC tests (all, not only the new ones; existing tests unchanged).

---

## Phase 7: SQL Server test (US1, US5)

- [ ] T034 [US1] Write `SQLT/DashboardSqlServerTests.cs` (pattern of `UserEmailIndexSqlServerTests`: `WithDatabaseAsync`, full `MigrateAsync`, never migrate to an older migration here): seed users (with and without `CreatedAtUtc`), products around the threshold, orders with every `Status` x `PaymentStatus` combination at chosen UTC dates incl. a 23:59:59 and 00:00:00 boundary, items with a NULL `ProductId` and a tie; call `DashboardService` with `CacheSeconds = 0` (construct with `Options.Create`, `MemoryCache`, a real context) and assert the same expectations as the API tests (counts, exact decimals, gap-filled daily series, top order with tie, new users ignoring NULL); attach a `DbCommandInterceptor` counting executed commands and assert at most 8 for one summary call, and the same count for a larger dataset (no N+1); assert the `AddUserCreatedAt` column exists, is nullable, and a user inserted with raw SQL without the column has `CreatedAtUtc` NULL (read through a projection). (FR-015, SC-001)

---

## Phase 8: Review and hygiene

- [ ] T035 `git status` review against the "Source changes" list of plan.md: only the listed files; existing migrations untouched; the new migration only adds `Users.CreatedAtUtc`; `CategoryProduct.slnx` and the CI workflow unchanged; no existing test expectation changed (only the additive `ApiFactory` setting and helpers); no JavaScript or CDN reference added (`grep -ri "cdn\|chart.js\|<script" MVC/Views/Dashboard` finds nothing).
- [ ] T036 Secret hygiene grep over the diff and tracked files: no key/secret/connection string added; `Dashboard` config holds only numbers; logs carry no data values; the endpoint returns no user names/emails.

---

## Phase 9: Final verification and Known Gaps (US8)

- [ ] T037 Run `dotnet build CategoryProduct.slnx` (0 errors, no new warnings in touched files) and `dotnet build CategoryProduct.API.SqlServerTests`.
- [ ] T038 Run `dotnet test CategoryProduct.slnx` THREE times in a row; all green each time; record the test counts (more than the T001 baseline).
- [ ] T039 Run the SQL Server tests on LocalDB (`dotnet test CategoryProduct.API.SqlServerTests`, at least `DashboardSqlServerTests`, `MigrationTests`, `SeededTestUserNeutralizationSqlServerTests`, `PaymentIndexSqlServerTests`); all green. If LocalDB is unavailable, record that in Known Gaps.
- [ ] T040 Complete `specs/027-admin-dashboard/spec.md` Known Gaps with the final facts (test counts, whether the SQL Server tests ran on LocalDB or only CI, migration name, deviations from the plan such as the group-by fallback of R3 if it was needed) and tick anything left in `checklists/requirements.md`; set the spec Status line.
- [ ] T041 After pushing (when the user pushes), confirm `build-test`, `docker-build`, `sqlserver-tests` (the new dashboard test) and `compose-smoke` are green and record the run in Known Gaps. LEFT OPEN until the user pushes.

> **Final note on features 025 and 026**: 025 (password management) and 026 (payments) change shared files while this feature was specified (`CategoryProduct.API/Program.cs`, `CategoryProduct.API.Tests/ApiFactory.cs`, `CategoryProduct.API/Controllers/AccountController.cs`, `CategoryProduct.API/Controllers/OrdersController.cs`, `CategoryProduct.API/Models/Order.cs` and the other models, `CategoryProduct.API/Data/ApplicationDbContext.cs`, `appsettings.json`, the migrations and snapshot, `NotificationService.cs`, MVC `OrderModels`/client). The implementer MUST re-read `Program.cs`, `ApiFactory.cs`, `OrdersController.cs` and the models (`Order`, `OrderItem`, `Product`, `User`, `OrderPaymentStatus`, `Payment`) after both have landed, before editing anything, and adapt names (enum members, options classes, factory hooks, DI registrations) in this task list; the migration `AddUserCreatedAt` must be generated last so it follows both.

---

## Dependencies and execution order

- T001 first; T002 any time before code.
- Phase 2: T003 first; T004 after T003; T005, T007 independent; T006 after T004.
- Phase 3: T008 first; T009 -> T010 -> T011 -> T012 strictly in order (migration generated after the model is final and after any 025/026 migration); T013 after T012; T014 last.
- Phase 4 needs Phase 2 and Phase 3 (the service reads `User.CreatedAtUtc`): T015 first (helpers), then tests T016-T019 in parallel, then T020 -> T021 -> T022 -> T023.
- Phase 5 after Phase 4 (same service file): T024 and T025 in parallel, then T026.
- Phase 6 (MVC) needs only the contract (`contracts/dashboard-endpoints.md`) and can run in parallel with Phases 4-5 on a different agent (T027 -> T028 -> T029 -> T030 -> T031 -> T032 -> T033), except the shared `CategoryProductApiClient.cs` and `_Layout.cshtml` edits.
- T034 after T012 and T020.
- T035-T036 after all code; T037-T041 last.

## Parallel example

Agent A: T003-T014 (primitives, model, migration), then T015-T026 (API). Agent B: T027-T033 (MVC against the contract and `FakeApi`). Then Agent A: T034; then one agent: T035-T041.

## Implementation strategy

MVP = Phases 2-4 (summary API with correct numbers). Phase 5 (cache) is small and low risk; Phase 6 gives the visible page; Phase 7 proves the SQL Server translation, which is the main technical risk (keep it ahead of any refinements). Keep every existing test green at each checkpoint.
