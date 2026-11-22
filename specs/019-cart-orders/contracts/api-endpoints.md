# Contract: Cart and Order endpoints (CategoryProduct.API)

Controllers `CartController` (`api/cart`) and `OrdersController` (`api/orders`), route `api/[controller]` (lowercase URLs). All endpoints require `Authorization: Bearer <JWT>`. Roles: `[Authorize]` (any signed-in role) unless stated. Problem responses use `application/problem+json`.

## Cart

Cart is always the caller's own (user id from the token). Responses 200 return the cart:

```json
{ "items": [ { "productId": 1, "productName": "Widget A", "unitPrice": 30.00, "quantity": 2,
               "lineTotal": 60.00, "availableQuantity": 5 } ],
  "itemCount": 1, "total": 60.00 }
```

Lines ordered by `AddedAtUtc`, then `Id`. Not paged (bounded: one line per product, 100 per line).

### GET api/cart

| Status | When |
|---|---|
| 200 | cart (empty cart: `items` [], `itemCount` 0, `total` 0) |
| 401 | no/invalid token |

### POST api/cart/items

Body `{ "productId": 1, "quantity": 2 }`: `productId` >= 1, `quantity` 1..100. Adds a line or merges into the existing line.

| Status | When |
|---|---|
| 200 | cart after the change |
| 400 | validation problem: `ProductId`/`Quantity` out of range; `Quantity` when the merged quantity would exceed 100 (message "The cart cannot hold more than 100 of one product.") |
| 401 | no token |
| 404 | unknown product |

### PUT api/cart/items/{productId}

Body `{ "quantity": 7 }` (1..100) replaces the line quantity.

| Status | When |
|---|---|
| 200 | cart after the change |
| 400 | `Quantity` out of range |
| 401 | no token |
| 404 | the cart has no line for `productId` |

### DELETE api/cart/items/{productId}

| Status | When |
|---|---|
| 200 | cart after removal |
| 401 | no token |
| 404 | no such line |

### DELETE api/cart

| Status | When |
|---|---|
| 204 | all lines removed (also when already empty) |
| 401 | no token |

## Orders

Order body (`OrderResponseDto`):

```json
{ "id": 7, "userId": 2, "userName": "plainuser", "status": "Pending", "total": 60.00,
  "createdAtUtc": "2026-11-22T10:00:00Z", "updatedAtUtc": "2026-11-22T10:00:00Z",
  "items": [ { "id": 11, "productId": 1, "productName": "Widget A", "unitPrice": 30.00,
               "quantity": 2, "lineTotal": 60.00 } ] }
```

`items[].productId` is null when the product was deleted. Summary (`OrderSummaryDto`, used by lists): `id, userId, userName, status, total, itemCount, createdAtUtc, updatedAtUtc`.

### POST api/orders (checkout, no body)

| Status | When |
|---|---|
| 201 | order created; `Location` -> `GET api/orders/{id}`; body `OrderResponseDto` (status `Pending`) |
| 400 | validation problem on key `Cart`: the cart is empty |
| 401 | no token |
| 409 | `ProblemDetails`: (a) insufficient stock: `title` "Insufficient stock", `detail` "Not enough stock for: Widget A (requested 3, available 1); ...", extension `items` = `[ { "productId": 1, "productName": "Widget A", "requested": 3, "available": 1 } ]`; or (b) 3 concurrency attempts failed: `title` "Conflict" |

Side effects (all or nothing): per line `Product.Quantity` reduced, `ConcurrencyStamp` regenerated, one `StockMovement` (`Sale`, negative change, `quantityAfter`, user = caller, note "Checkout"); one `Order` (`Pending`) with item snapshots; the caller's cart lines deleted. Order of checks: auth, empty cart (400), stock (409), apply.

### GET api/orders?page=1&pageSize=10

Caller's orders. `page` min 1; `pageSize` clamped to 1..50 (default 10). 200 `PagedResponseDto<OrderSummaryDto>` ordered `createdAtUtc` desc then `id` desc. 401.

### GET api/orders/{id:int}

| Status | When |
|---|---|
| 200 | `OrderResponseDto` for the owner or an Admin |
| 401 | no token |
| 404 | unknown id, or the order belongs to someone else and the caller is not Admin |

### GET api/orders/all?status=&page=1&pageSize=10

`[Authorize(Roles = "Admin")]`. `status` optional (`Pending`, `Confirmed`, `Cancelled`, case-insensitive name, numbers not accepted). 200 `PagedResponseDto<OrderSummaryDto>` of all users' orders, newest first. 400 (key `status`) for an invalid status, 401, 403.

### POST api/orders/{id:int}/cancel (no body)

Owner or Admin, while `Pending`.

| Status | When |
|---|---|
| 200 | `OrderResponseDto` with status `Cancelled` |
| 401 | no token |
| 404 | unknown order, or another user's order for a non-Admin |
| 409 | order is not `Pending`, or a product quantity would overflow, or 3 concurrency attempts failed |

Side effects: status `Cancelled`, `updatedAtUtc` refreshed, new `ConcurrencyStamp`; for each item whose `ProductId` is not null and whose product exists: quantity added back and one `StockMovement` (`OrderCancelled`, positive change, user = caller, note "Order #{id} cancelled"); items of deleted products are skipped and logged.

### PUT api/orders/{id:int}/status

`[Authorize(Roles = "Admin")]`. Body `{ "status": "Confirmed" }`.

| Status | When |
|---|---|
| 200 | `OrderResponseDto` after the change (`Cancelled` behaves like the cancel endpoint, including stock restore) |
| 400 | `Status` empty, unknown or numeric |
| 401 / 403 | no token / not Admin (the owner without Admin is 403) |
| 404 | unknown order |
| 409 | transition not allowed (order not `Pending`, or target `Pending`), or conflict after 3 attempts |

Order of checks: auth (401/403), DTO validation and status parse (400), order exists (404), transition (409), apply.
