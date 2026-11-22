# Contract: Dashboard endpoint

Route `api/dashboard`, class-level `[Authorize(Roles = "Admin")]`.

## GET api/dashboard/summary?from=2026-11-01&to=2026-11-30&top=5

| Parameter | Type | Default | Rules |
|---|---|---|---|
| `from` | `yyyy-MM-dd` (UTC date, inclusive) | `to` - (`DefaultRangeDays` - 1) | must parse; `from <= to` |
| `to` | `yyyy-MM-dd` (UTC date, inclusive) | today (UTC) | must parse; range length `(to - from) + 1 <= MaxRangeDays` (366) |
| `top` | int | `DefaultTopProducts` (5) | `1..MaxTopProducts` (20) |

| Case | Status | Body |
|---|---|---|
| OK (including an empty database) | 200 | `DashboardSummaryDto` |
| Invalid date format, `from` after `to`, range too long, `top` out of range or not an integer | 400 | `ValidationProblem` with entries keyed `from`, `to` or `top` |
| No/invalid token | 401 | empty |
| Not an Admin | 403 | empty |

### Response

```json
{
  "from": "2026-11-01",
  "to": "2026-11-30",
  "days": 30,
  "generatedAtUtc": "2026-11-30T10:15:00Z",
  "currency": "USD",
  "totalOrders": 12,
  "ordersByStatus": { "pending": 4, "confirmed": 6, "cancelled": 2 },
  "revenue": 1234.50,
  "paidOrders": 7,
  "refundedTotal": 80.00,
  "averageOrderValue": 176.36,
  "newUsers": 3,
  "lowStockCount": 2,
  "lowStockThreshold": 5,
  "topProducts": [
    { "productId": 7, "name": "Widget", "quantitySold": 21, "revenue": 420.00 }
  ],
  "daily": [
    { "date": "2026-11-01", "orders": 0, "revenue": 0 }
  ]
}
```

Rules:

- `ordersByStatus` always carries all three keys; `topProducts` is `[]` when empty; `daily` always has `days` entries ascending.
- Money values are decimals with at most 2 decimals; `averageOrderValue` is `0` when `paidOrders` is `0`.
- All date-based figures attribute an order to its `CreatedAtUtc` date (UTC).
- `revenue` = Paid orders only; `refundedTotal` = Refunded orders (see data-model definitions).
- No user names, emails or order identifiers are returned.
- Caching: identical `(from, to, top)` within `CacheSeconds` returns the same document (same `generatedAtUtc`); no `Cache-Control` headers are added (server-side cache only).
- Logging: Information `Dashboard summary from=... to=... top=... cache=hit|miss elapsedMs=...`.

Existing endpoints are unchanged; `UserResponseDto` does not gain `createdAtUtc`.
