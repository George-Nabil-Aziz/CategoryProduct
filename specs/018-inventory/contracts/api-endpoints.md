# Contract: Inventory endpoints (CategoryProduct.API)

Controller `ProductController`, route `api/[controller]` (`api/product`). All endpoints below require `Authorization: Bearer <JWT>` with role `Admin` (`[Authorize(Roles = "Admin")]`).

## POST api/product/{id}/stock-movements

Request body (JSON):

```json
{ "quantityChange": 10, "reason": "Restock", "note": "Supplier delivery" }
```

- `quantityChange`: int, -1,000,000..1,000,000, not 0.
- `reason`: one of `Restock`, `Adjustment`, `Damage`, `Return` (case-insensitive name; numbers not accepted).
- `note`: optional, at most 200 characters, trimmed, whitespace-only stored as null.

| Status | When |
|---|---|
| 201 | applied; body `StockMovementResponseDto`; no `Location` header |
| 400 | `application/problem+json` validation problem: key `QuantityChange` (zero or out of range), `Reason` (missing, unknown, numeric, or not allowed: `Initial`, `Sale`, `OrderCancelled`), `Note` (too long) |
| 401 | no/invalid token |
| 403 | non-Admin |
| 404 | unknown product |
| 409 | `ProblemDetails` `{ status: 409, title: "Conflict", detail }`: result would be negative or exceed `int.MaxValue`, or 3 concurrency attempts failed |

201 body:

```json
{ "id": 12, "productId": 3, "quantityChange": 10, "quantityAfter": 15, "reason": "Restock",
  "note": "Supplier delivery", "userId": 1, "userName": "tester", "createdAtUtc": "2026-11-22T10:00:00Z" }
```

Side effects: `Product.Quantity` and `ConcurrencyStamp` updated and one `StockMovements` row inserted in one save. A warning is logged if `quantityChange < 0` and `quantityAfter` is below the threshold. Order of checks: auth, DTO validation, zero/reason rules (400), product exists (404), apply (409).

## GET api/product/{id}/stock-movements?page=1&pageSize=10

- `page` min 1 (default 1); `pageSize` clamped to 1..50 (default 10; the product list defaults to 5, history needs more rows per page).
- 200: `PagedResponseDto<StockMovementResponseDto>` ordered `createdAtUtc` desc then `id` desc.
- 401, 403, 404 (unknown product).

## GET api/product/low-stock?page=1&pageSize=10

- 200: `PagedResponseDto<ProductResponseDto>`; items have `quantity < threshold` (`isLowStock` always true), ordered by `quantity` asc then `id` asc. Empty page when threshold is 0 or nothing is low.
- 401, 403.
- Routing: literal `low-stock` beats `{id}`; `GET api/product/low-stock` never reaches `Get(int id)`.

## Changed existing endpoints

| Endpoint | Change |
|---|---|
| every product response (`GET api/product`, `GET api/product/{id}`, `POST`, `PUT`, `GET api/category/{catId}/products`, 017 image upload) | extra property `isLowStock` (bool), after `imageUrl` |
| `POST api/product` | `quantity` > 0 also inserts an `Initial` movement (same save); response unchanged apart from `isLowStock` |
| `PUT api/product/{id}` | 400 `ValidationProblem`, key `Quantity`, "Use stock movements to change quantity." when `quantity` differs from the stored one; equal quantity accepted; 409 `ProblemDetails` if a stock change committed concurrently |
| `DELETE api/product/{id}` | 409 `ProblemDetails` on a concurrency conflict; movements are removed by DB cascade |

## Configuration

`Inventory:LowStockThreshold` (int, default 5; 0 disables; negative falls back to 5).
