# Data Model: Admin dashboard

## Schema impact

One migration `AddUserCreatedAt` (generated, not hand-edited), created after the 025 and 026 migrations.

### Changed table `Users`

| Column | Type | Notes |
|---|---|---|
| `CreatedAtUtc` | datetime2, NULL | `User.CreatedAtUtc` is `DateTime?`; set on registration and bootstrap admin; NULL for rows that existed before the migration (unknown); no default, no index, no backfill |

No other table, column or index changes. (Index on `Orders.CreatedAtUtc` is a Known Gap, spec A12.)

## Read models (existing, after 026)

| Source | Columns used |
|---|---|
| `Orders` | `Status`, `PaymentStatus` (026), `Total`, `CreatedAtUtc` |
| `OrderItems` | `OrderId`, `ProductId`, `ProductName`, `Quantity`, `LineTotal` |
| `Products` | `Quantity` |
| `Users` | `CreatedAtUtc` |

## Non-persistent types

- `DashboardOptions { DefaultRangeDays = 30, MaxRangeDays = 366, DefaultTopProducts = 5, MaxTopProducts = 20, CacheSeconds = 60 }` with `Effective*` accessors (invalid -> defaults; `CacheSeconds` < 0 -> 60, 0 = off).
- `DashboardService.GetSummaryAsync(DateOnly from, DateOnly to, int top, CancellationToken)` -> `DashboardSummaryDto`.
- API DTOs (`Dtos/DashboardDtos.cs`), camelCase JSON via the existing serializer defaults:

```text
DashboardSummaryDto(
  string From, string To, int Days, DateTime GeneratedAtUtc, string Currency,
  int TotalOrders, OrdersByStatusDto OrdersByStatus,
  decimal Revenue, int PaidOrders, decimal RefundedTotal, decimal AverageOrderValue,
  int NewUsers, int LowStockCount, int LowStockThreshold,
  IReadOnlyList<TopProductDto> TopProducts, IReadOnlyList<DailyPointDto> Daily)
OrdersByStatusDto(int Pending, int Confirmed, int Cancelled)
TopProductDto(int ProductId, string Name, int QuantitySold, decimal Revenue)
DailyPointDto(string Date, int Orders, decimal Revenue)
```

`From`, `To`, `Date` are `yyyy-MM-dd` strings (no time zone ambiguity in JSON).

- MVC (`Models/DashboardModels.cs`): `DashboardSummaryModel` and nested records with defaults for missing fields; `DashboardQueryModel(string? From, string? To, int? Top)` for the form; `DashboardPageModel(DashboardQueryModel Query, DashboardSummaryModel? Summary)` (view model).

## Definitions (precise)

Let `R` = orders with `CreatedAtUtc` in `[fromUtc, toExclusiveUtc)`.

- `totalOrders = |R|`; `ordersByStatus.x = |{o in R : o.Status = x}|`.
- `revenue = Sum(o.Total) for o in R with o.PaymentStatus = Paid`; `paidOrders = |{o in R : Paid}|`.
- `refundedTotal = Sum(o.Total) for o in R with o.PaymentStatus = Refunded` (never part of `revenue`; 026 refunds are full, so this is the "minus refunded" part).
- `averageOrderValue = paidOrders = 0 ? 0 : Round(revenue / paidOrders, 2, AwayFromZero)`.
- `newUsers = |{u : u.CreatedAtUtc in range}|` (NULL excluded).
- `lowStockCount = |{p : p.Quantity < Inventory threshold}|` (not range dependent).
- `daily[d].orders = |{o in R : o.CreatedAtUtc.Date = d}|`, `daily[d].revenue = Sum(Total) of Paid orders of that day`; every day in `[from, to]` is present.
- `topProducts`: items of `o in R` with `o.Status != Cancelled` and `ProductId != null`, grouped by `ProductId`, `quantitySold = Sum(Quantity)`, `revenue = Sum(LineTotal)`; ordered by `quantitySold` desc then `productId` asc; first `top`.

## Invariants (asserted in tests)

`sum(daily.orders) = totalOrders`; `sum(daily.revenue) = revenue`; `ordersByStatus.pending + confirmed + cancelled = totalOrders`; `daily.Count = days`; `topProducts.Count <= top`.

## Production code touched

`Models/User.cs`, `Data/BootstrapAdmin.cs`, `Controllers/AccountController.cs`, `Services/DashboardOptions.cs` (new), `Services/DashboardService.cs` (new), `Dtos/DashboardDtos.cs` (new), `Controllers/DashboardController.cs` (new), `Program.cs`, `appsettings.json`, `Migrations/*AddUserCreatedAt*` (generated), MVC files listed in plan.md.

## Test infrastructure touched

`ApiFactory.cs` (setting `Dashboard:CacheSeconds=0`, optional override hook for the cache test), `DashboardTestSupport.cs` (new helpers: `SeedOrderAsync(userId, createdAtUtc, status, paymentStatus, total, items...)`, `SeedUserAsync(createdAtUtc)`), `ApiJson.cs` in MVC tests (additive `DashboardSummary(...)` builder with defaults), `SqlServerEnv` unchanged.
