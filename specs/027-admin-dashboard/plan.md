# Implementation Plan: Admin dashboard

**Branch**: `027-admin-dashboard` | **Date**: 2026-11-22 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/027-admin-dashboard/spec.md`

## Summary

(1) `GET api/dashboard/summary?from=&to=&top=` (Admin only) backed by a scoped `DashboardService` that runs a fixed set of server-side aggregate queries (status counts, paid and refunded sums, daily group, new users, low stock, top products), fills empty days in memory, and caches the result in `IMemoryCache` for 60 s. (2) A nullable `User.CreatedAtUtc` with one generated migration `AddUserCreatedAt` so "new users" is real. (3) An MVC `Dashboard` page (Admin only, nav link) with KPI cards, a server-rendered inline-SVG chart and accessible table, a top products table, a low-stock card linking to Inventory, and a validated GET date-range form. (4) Tests: API, MVC and SQL Server (translation and query count on a real SQL Server).

## Technical Context

**Language/Version**: C# / .NET 10.

**Primary Dependencies**: none added (`Microsoft.Extensions.Caching.Memory` ships with the shared framework; `services.AddMemoryCache()`). No JavaScript library.

**Storage**: SQL Server via EF Core; one new nullable column `Users.CreatedAtUtc` (migration). Reads `Orders`, `OrderItems`, `Products`, `Users`. InMemory in tests.

**Testing**: xunit. `CategoryProduct.API.Tests` (`ApiFactory`, direct `DbContext` seeding), `CategoryProduct.MVC.Tests` (`FakeApi`, `MvcTestBase`, `HtmlHelpers`, `ApiJson`), `CategoryProduct.API.SqlServerTests` (`SqlServerEnv`; not in `CategoryProduct.slnx`; CI and LocalDB).

**Target Platform**: local `dotnet test CategoryProduct.slnx`; GitHub Actions jobs unchanged.

**Project Type**: existing web API + MVC; no new project.

**Constraints**: constitution I-V; read-only feature; every existing test keeps its expectations; API contract only grows; queries must translate on SQL Server (not only InMemory); no client evaluation of grouping.

**Scale/Scope**: ~4 new API files (`DashboardController`, `DashboardService`, `DashboardOptions`, `DashboardDtos`), `User.cs`, `ApplicationDbContext` (property config only if needed), `AccountController` + `BootstrapAdmin` (set timestamp), `Program.cs` (options, memory cache, service), `appsettings.json`, 1 migration, MVC (`DashboardController`, `DashboardModels`, client method, 2 views, nav link), ~7 API test files, 1 MVC test file, 1 SQL Server test file.

## Constitution Check

Checked against `.specify/memory/constitution.md` v1.0.0.

| Principle | Check | Status |
|---|---|---|
| I. Consistent REST API Design | `api/dashboard/summary` follows `api/[controller]` with a lower-case plural-style resource route of the existing controllers (`api/product`, `api/users`, `api/orders`, `api/account`); GET, standard `Ok`/`ValidationProblem`/`401`/`403`. It is a single aggregate document, not a collection of records, so pagination does not apply; every nested list is bounded by construction (days <= 366, top <= 20, 3 statuses). Errors are `ProblemDetails` like the rest. | PASS |
| II. Schema Changes Only Through EF Core Migrations | `User.CreatedAtUtc` ships with `dotnet ef migrations add AddUserCreatedAt --project CategoryProduct.API` in the same change (task T012), generated after the 025 and 026 migrations so the snapshot is consistent; nothing hand-edited; the MVC keeps its own models. | PASS |
| III. Simplicity & YAGNI | Controllers MAY use the `DbContext`; a `DashboardService` is added anyway because it has two concrete consumers (the controller and the SQL Server tests that must run the queries without HTTP) and owns cross-cutting cache logic; no repository layer, no interface (a concrete class registered scoped, like `InventoryService`). No new index, no distributed cache, no chart library (see R7). | PASS (justified in Complexity Tracking) |
| IV. Secrets & Credential Hygiene (NON-NEGOTIABLE) | No secrets, no keys; config holds only numbers; the endpoint returns aggregates, never user names, emails, hashes or tokens; logs carry range and timing only. | PASS |
| V. API/MVC Separation | MVC calls `api/dashboard/summary` over HTTP with its own `DashboardSummaryModel` records; no API type referenced; MVC tests reference only `CategoryProduct.MVC`. | PASS |

Workflow gates: "Every change that adds or modifies a Model class MUST include the migration": T012 (generation) and T035 (review). "No secret values in tracked configuration": T036. "Controller behavior verified": automated tests per story.

## Project Structure

### Documentation

```text
specs/027-admin-dashboard/
  spec.md  plan.md  research.md  data-model.md  quickstart.md  tasks.md
  contracts/{dashboard-endpoints.md, dashboard-config.md, dashboard-page.md}
  checklists/requirements.md
```

### Source changes

```text
CategoryProduct.API/Models/User.cs                              EDIT add DateTime? CreatedAtUtc
CategoryProduct.API/Data/ApplicationDbContext.cs                EDIT only if needed (no config required for a plain DateTime?; 025/026 also edit this file)
CategoryProduct.API/Data/BootstrapAdmin.cs                      EDIT set CreatedAtUtc on the created admin
CategoryProduct.API/Controllers/AccountController.cs            EDIT Register sets CreatedAtUtc (025 also edits this file)
CategoryProduct.API/Migrations/<ts>_AddUserCreatedAt(.Designer).cs  NEW generated; snapshot regenerated by the tool
CategoryProduct.API/Services/DashboardOptions.cs                NEW Dashboard section with Effective* accessors
CategoryProduct.API/Services/DashboardService.cs                NEW queries + cache
CategoryProduct.API/Dtos/DashboardDtos.cs                       NEW DashboardSummaryDto + nested records
CategoryProduct.API/Controllers/DashboardController.cs          NEW GET summary, validation
CategoryProduct.API/Program.cs                                  EDIT Configure<DashboardOptions>, AddMemoryCache, AddScoped<DashboardService> (025/026 also edit)
CategoryProduct.API/appsettings.json                            EDIT "Dashboard": { ... } (optional values)
CategoryProduct.MVC/Models/DashboardModels.cs                   NEW DashboardSummaryModel + nested records, DashboardQueryModel
CategoryProduct.MVC/Services/CategoryProductApiClient.cs        EDIT GetDashboardSummaryAsync
CategoryProduct.MVC/Controllers/DashboardController.cs          NEW Index (Admin)
CategoryProduct.MVC/Views/Dashboard/Index.cshtml                NEW cards, form, tables
CategoryProduct.MVC/Views/Dashboard/_DailyBarChart.cshtml       NEW inline SVG partial
CategoryProduct.MVC/Views/Shared/_Layout.cshtml                 EDIT nav link inside the Admin block
CategoryProduct.API.Tests/ApiFactory.cs                         EDIT setting Dashboard:CacheSeconds=0, optional factory override (025/026 also edit)
CategoryProduct.API.Tests/DashboardTestSupport.cs               NEW seeding helpers (orders, items, users with chosen dates)
CategoryProduct.API.Tests/{DashboardAuthTests,DashboardSummaryTests,DashboardRangeTests,DashboardSeriesTests,DashboardTopProductsTests,DashboardCacheTests,DashboardOptionsTests,UserCreatedAtTests}.cs   NEW
CategoryProduct.MVC.Tests/DashboardTests.cs                     NEW (+ ApiJson.DashboardSummary helper)
CategoryProduct.API.SqlServerTests/DashboardSqlServerTests.cs   NEW
```

## Design

- **Service shape**: `DashboardService.GetSummaryAsync(DateOnly from, DateOnly to, int top, CancellationToken)` returns `DashboardSummaryDto`. It converts the range to `[fromUtc, toExclusiveUtc)` once, builds one `IQueryable<Order>` for the range (`AsNoTracking`), and runs the aggregates (research R2): status group (<= 3 rows), paid and refunded aggregate (one grouped query on `PaymentStatus`), daily group (<= 366 rows), top products (<= 20 rows), new users count, low-stock count. Total 6 queries; the SQL Server test asserts <= 8.
- **Daily grouping**: `GroupBy(o => o.CreatedAtUtc.Date)` and `Select(g => new { Day = g.Key, Orders = g.Count(), Revenue = g.Sum(o => o.PaymentStatus == Paid ? o.Total : 0m) })`; `DateTime.Date` translates to `CAST(... AS date)` on SQL Server and is evaluated natively on InMemory; the fallback if the translation ever fails is `GroupBy(o => new { o.CreatedAtUtc.Year, o.CreatedAtUtc.Month, o.CreatedAtUtc.Day })` (R3).
- **Gap filling**: a `Dictionary<DateOnly, (int, decimal)>` from the day rows, then a loop over the days of the range (<= 366) - O(days), no query.
- **Top products**: `OrderItems.Where(i => i.ProductId != null && i.Order.CreatedAtUtc in range && i.Order.Status != Cancelled)` grouped by `ProductId` with `Sum(Quantity)`, `Sum(LineTotal)`, `Max(ProductName)`; `OrderByDescending(Quantity).ThenBy(ProductId).Take(top)` (R4). Navigation `i.Order` translates to a join inside the query, not N+1.
- **Cache**: `IMemoryCache.TryGetValue(key)`, else compute and `Set` with `AbsoluteExpirationRelativeToNow`; `CacheSeconds <= 0` bypasses; the cached value is the immutable DTO (R5).
- **Validation** in the controller: bind `DateOnly? from, DateOnly? to, int? top`; model-binding failures yield the standard 400 `ValidationProblem` (the controller does not use `[ApiController]` auto-400 differently from the others; check at T002 how other controllers validate and follow the same pattern); explicit checks add `from`/`to`/`top` entries.
- **MVC**: `DashboardController.Index(string? from, string? to, int? top)` parses and validates strings (so an invalid date shows a field message instead of a framework binding error), calls the client, passes a view model with the form values and the summary. The chart partial takes a series selector (orders or revenue) and computes bar geometry in the view (fixed 720x160 viewBox, bar width from the count, heights scaled to the maximum, zero-safe).
- **Nav**: one `<li>` with `asp-controller="Dashboard" asp-action="Index"` inside the existing `User.IsInRole("Admin")` block.

## Complexity Tracking

| Item | Why needed | Simpler alternative rejected because |
|---|---|---|
| `DashboardService` class | two consumers (controller, SQL Server tests) and cache logic | queries inline in the controller cannot be exercised on SQL Server without HTTP and would mix caching into the controller |
| `IMemoryCache` | repeated range scans for a page that is refreshed often | no cache would rerun 6 aggregates per click; Redis/distributed would add infrastructure |
| Nullable `User.CreatedAtUtc` + migration | "new users" is impossible otherwise | dropping the metric loses a requested KPI; non-null default would fake data for old users |
| Inline SVG chart | no JS, no CDN, no SRI, testable HTML | Chart.js adds an external dependency and CSP/SRI concerns and is hard to test without a browser (028) |

## Phase order

Setup -> options and DTOs -> model + migration + timestamp writes (US6) -> service and endpoint (US1-US4) -> authorization and cache (US5) -> MVC (US7) -> SQL Server test -> review/hygiene -> final verification and Known Gaps (US8).
