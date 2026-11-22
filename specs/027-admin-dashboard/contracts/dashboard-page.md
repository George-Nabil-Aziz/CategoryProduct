# Contract: MVC Dashboard page

Route `GET /Dashboard` (controller `Dashboard`, action `Index`), `[Authorize(Roles = "Admin")]`. Query string: `from`, `to` (`yyyy-MM-dd`), `top` (1..20).

## Layout of the page

1. Title "Dashboard" and the range form: `<form method="get" asp-controller="Dashboard" asp-action="Index">` with `<input type="date" name="from">`, `<input type="date" name="to">`, `<input type="number" name="top" min="1" max="20">`, button "Show", link "Last 30 days" (no query). Text: "Dates are UTC, inclusive, at most 366 days." Field messages in `<span class="text-danger" data-valmsg-for="from|to|top">`. Values prefilled from the request, or from the API's effective `from`/`to` when the form was empty.
2. KPI cards (`.card`, ids for tests): `kpi-orders` (total, with pending/confirmed/cancelled sub-line), `kpi-revenue` (amount + currency, sub-line "N paid orders, refunded X"), `kpi-aov`, `kpi-new-users`, `kpi-low-stock` (count, link `asp-controller="Inventory" asp-action="Index"` text "View inventory", threshold note).
3. Charts: two `<svg role="img" aria-label="Orders per day">` / `"Revenue per day"` from the partial `_DailyBarChart` (`viewBox="0 0 720 160"`, one `<rect>` per day with `<title>"yyyy-MM-dd: N orders"` / `"yyyy-MM-dd: X.XX USD"`), axis labels for first/last date and the maximum. `<details><summary>Daily data</summary><table>` with the same numbers (Date, Orders, Revenue). If `totalOrders == 0`: note "No orders in this range." (charts still render with flat bars).
4. Top products table: columns Product, Quantity sold, Sales; empty state "No sales in this range."; names Razor-encoded.
5. Footer line: "Generated <generatedAtUtc> UTC" (cached for up to a minute).

## Behavior

| Case | Result |
|---|---|
| Anonymous | redirect to login (existing cookie handling) |
| Non-Admin | redirect to `/Account/AccessDenied`, no API call |
| Valid or empty query | API call `GET api/dashboard/summary` with the given params only (omitted when empty); page 200 |
| Client-detectable invalid input | page 200 with field messages, no API call |
| API 400 | field messages from `ApiResult.Errors` (keys `from`, `to`, `top`), no summary section |
| API 401 | `SessionExpiredAsync` (sign out, redirect to login) |
| API 403 | redirect to AccessDenied |
| API 429 / 5xx / unreachable | existing handlers (`ApiErrorHandler` / `TempData["Error"]` pattern), no unhandled exception |

## Client method

`CategoryProductApiClient.GetDashboardSummaryAsync(string? from, string? to, int? top)` -> `ApiResult<DashboardSummaryModel>` (`SendAsync<T>` pattern, bearer from the session); query built with `Uri.EscapeDataString`.

## Navigation

`_Layout.cshtml`: `<li class="nav-item"><a class="nav-link text-dark" asp-controller="Dashboard" asp-action="Index">Dashboard</a></li>` inside the existing `@if (User.IsInRole("Admin"))` block, before Users.
