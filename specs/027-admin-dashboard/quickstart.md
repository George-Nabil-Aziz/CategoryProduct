# Quickstart: Admin dashboard

## Run everything (no Docker, SQL Server or network)

```powershell
dotnet build CategoryProduct.slnx
dotnet test CategoryProduct.slnx
```

## Targeted runs

```powershell
dotnet test CategoryProduct.API.Tests --filter "FullyQualifiedName~Dashboard|FullyQualifiedName~UserCreatedAt"
dotnet test CategoryProduct.MVC.Tests --filter "FullyQualifiedName~DashboardTests"
```

## SQL Server tests (LocalDB)

```powershell
dotnet build CategoryProduct.API.SqlServerTests
dotnet test CategoryProduct.API.SqlServerTests --filter "FullyQualifiedName~DashboardSqlServerTests"
```

Use the same LocalDB connection setup as the other SQL Server tests (see `SqlServerEnv`).

## Try it by hand

1. Start API and MVC, sign in as an Admin, create a few orders as users (with 026: pay some, cancel one paid order).
2. Open the "Dashboard" nav link: KPI cards, charts, top products. Low stock card -> Inventory.
3. Change the range (for example the last 7 days); try `from` after `to` and a 400-day range: field messages appear, no API call.
4. API: `GET api/dashboard/summary?from=2026-11-01&to=2026-11-30&top=3` with an Admin bearer token; a user token -> 403; no token -> 401.
5. Within 60 s repeat the call: same `generatedAtUtc`. Set `Dashboard__CacheSeconds=0` to disable caching.

## Configuration

`Dashboard:DefaultRangeDays`, `MaxRangeDays`, `DefaultTopProducts`, `MaxTopProducts`, `CacheSeconds` (all optional, see `contracts/dashboard-config.md`). Low-stock uses `Inventory:LowStockThreshold`; currency label uses `Payments:Currency`.

## Manual checks

- Existing users show as "not counted" in New users until new registrations happen after deployment (expected).
- `dotnet ef migrations list --project CategoryProduct.API` shows `AddUserCreatedAt` last.
