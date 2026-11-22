# Feature Specification: Inventory (Stock Movements and Low-Stock Alerts)

**Feature Branch**: `018-inventory`

**Created**: 2026-11-22

**Status**: Draft

**Input**: User description: "Stock movement log (quantity, reason, user, time). An admin endpoint adjusts stock through a movement instead of editing the quantity directly. Low-stock alert when quantity is below a configurable threshold. An MVC page."

**Depends on**: 017-product-images (adds `ProductResponseDto.ImageUrl` and `ToResponse(Url)`); implemented after 017. Feeds 019 (orders), which will reuse the stock-change service.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Admin adjusts stock through a movement (Priority: P1)

An Admin records a stock movement (a signed quantity change, a reason and an optional note) for a product. The product's quantity changes by exactly that amount and the movement is stored with the resulting quantity, the acting user and the time. A change that would make the quantity negative is refused.

**Why this priority**: This is the core of the feature: every stock change must leave an auditable record.

**Independent Test**: As Admin, `POST api/product/{id}/stock-movements` with `{ "quantityChange": 10, "reason": "Restock" }` on a product with quantity 5 returns 201 with `quantityAfter` 15, and `GET api/product/{id}` shows quantity 15.

**Acceptance Scenarios**:

1. **Given** a product with quantity 5, **When** an Admin posts `quantityChange` 10 with reason `Restock`, **Then** the response is 201, the body shows `quantityChange` 10, `quantityAfter` 15, the reason, the Admin's user id and user name and a UTC timestamp, and the product quantity is 15.
2. **Given** a product with quantity 5, **When** an Admin posts `quantityChange` -3 with reason `Damage`, **Then** the response is 201 with `quantityAfter` 2.
3. **Given** a product with quantity 5, **When** an Admin posts `quantityChange` -6, **Then** the response is 409 (problem details), the quantity stays 5 and no movement is stored.
4. **Given** any product, **When** the body has `quantityChange` 0, an unknown reason, a reason that is not allowed for this endpoint (`Sale`, `OrderCancelled`, `Initial`), a missing reason, a numeric reason such as `"3"`, or a note longer than 200 characters, **Then** the response is 400 (validation problem) and nothing is stored.
5. **Given** an unknown product id, **Then** the response is 404.
6. **Given** no token, **Then** 401; given a non-Admin token, **Then** 403; in both cases nothing changes.
7. **Given** two stock changes for the same product that run at the same time, **Then** neither is lost: the final quantity equals the starting quantity plus both changes, because the loser is retried automatically (up to 3 attempts) and finally receives 409 only if it still cannot be applied.

---

### User Story 2 - Movement history per product (Priority: P1)

An Admin lists a product's movements, newest first, with paging. Creating a product with a quantity above zero records an `Initial` movement so history starts at the first quantity.

**Why this priority**: A log nobody can read is not auditable; the initial entry makes the history add up.

**Independent Test**: Create a product with quantity 7, post a Restock of 3, then `GET api/product/{id}/stock-movements`: two items, newest (Restock, `quantityAfter` 10) first, then `Initial` (`quantityAfter` 7).

**Acceptance Scenarios**:

1. **Given** a product created through `POST api/product` with `quantity` > 0, **Then** exactly one movement exists with reason `Initial`, `quantityChange` equal to the quantity, `quantityAfter` equal to the quantity and the creating Admin as user.
2. **Given** a product created with `quantity` 0, **Then** no movement is created.
3. **Given** a product with several movements, **When** an Admin calls `GET api/product/{id}/stock-movements?page=1&pageSize=2`, **Then** the response is a `PagedResponseDto` with the newest two movements, ties broken by higher id first, with correct `totalCount` and `totalPages`.
4. **Given** an unknown product, **Then** 404; no token 401; non-Admin 403.
5. **Given** a `pageSize` above 50 or below 1, or `page` below 1, **Then** values are clamped exactly like the existing product list (`page` min 1, `pageSize` 1 to 50).
6. **Given** products that existed before this feature, **Then** they have an empty history (no backfill).

---

### User Story 3 - Low-stock alert (Priority: P1)

A product whose quantity is below the configured threshold (`Inventory:LowStockThreshold`, default 5) is flagged. Every product response carries `isLowStock`, an Admin endpoint lists the low-stock products, and the server logs a warning when a movement drops a product below the threshold.

**Why this priority**: The alert is the reason for tracking stock at all.

**Independent Test**: With threshold 5, a product at quantity 4 has `isLowStock` true and appears in `GET api/product/low-stock`; after a Restock to 5 it has `isLowStock` false and disappears from the list.

**Acceptance Scenarios**:

1. **Given** threshold 5, **When** a product has quantity 4, **Then** `isLowStock` is true in `GET api/product/{id}`, in `GET api/product` items and in `GET api/category/{catId}/products` items; at quantity 5 or more it is false (strictly below).
2. **Given** an Admin, **When** `GET api/product/low-stock?page&pageSize` is called, **Then** only products with quantity below the threshold are returned, lowest quantity first then lowest id, as a `PagedResponseDto<ProductResponseDto>`.
3. **Given** a non-Admin or no token, **Then** 403 or 401.
4. **Given** configuration `Inventory:LowStockThreshold` = 0, **Then** no product is ever low stock; a missing or negative value falls back to the default 5.
5. **Given** a movement with a negative change that leaves quantity below the threshold, **Then** one warning is logged with product id, quantity after and threshold.
6. **Given** the existing `GET api/product/{id}` route, **Then** `GET api/product/low-stock` is not captured by it and still returns the list.

---

### User Story 4 - Quantity changes only through movements (Priority: P2)

`PUT api/product/{id}` no longer changes a product's quantity. Sending the current quantity is accepted (clients that round-trip the whole product keep working); sending a different one is rejected.

**Why this priority**: Without this rule the log could be bypassed and would stop adding up.

**Independent Test**: `PUT` a product with a different `quantity` and expect 400 on key `Quantity`; `PUT` with the unchanged `quantity` and a new name and expect 200.

**Acceptance Scenarios**:

1. **Given** a product with quantity 5, **When** an Admin sends PUT with `quantity` 9, **Then** the response is 400 `ValidationProblem` with errors key `Quantity` and message "Use stock movements to change quantity.", and nothing (name, price, category) is changed.
2. **Given** the same product, **When** PUT has `quantity` 5 and a new name, **Then** 200 and the name is updated.
3. **Given** a stock movement commits while a PUT of the same product is in flight, **Then** the PUT returns 409 (problem details) instead of overwriting stock.
4. **Given** `POST api/product` with a quantity, **Then** it still sets the initial quantity (and records the `Initial` movement, story 2).

---

### User Story 5 - Inventory page in the MVC app (Priority: P2)

An Admin opens "Inventory" from the navigation bar and sees every product with its quantity and a low-stock badge, with a banner counting low-stock products at the top. Selecting a product shows its movement history (paged) and a form to add a movement. The Product Edit page shows quantity read-only.

**Why this priority**: Gives the feature a usable front end; the API is complete without it.

**Independent Test**: Sign in as Admin, open `/Inventory`, see the banner and badges, open a product, add a Restock and see the new row and quantity; add a movement that would go negative and see the API error message.

**Acceptance Scenarios**:

1. **Given** an Admin, **Then** the navigation bar shows "Inventory" (hidden for everyone else) and `/Inventory` lists products paged with quantity and a "Low stock" badge on flagged ones.
2. **Given** at least one low-stock product, **Then** a warning banner shows "N product(s) low on stock"; with none, no banner.
3. **Given** `/Inventory/Product/{id}`, **Then** the product name, quantity, a paged movement table (time, change, quantity after, reason, note, user) and a form (reason dropdown with Restock, Adjustment, Damage, Return; signed quantity; note) are shown.
4. **Given** a valid submission, **Then** the page reloads with a success message and the new row; **Given** an API error (400 or 409), **Then** the message is shown.
5. **Given** a non-Admin or anonymous user, **Then** `/Inventory...` redirects to login or access denied (same as the Users page); an unknown product id returns 404.
6. **Given** the Product Edit page, **Then** Quantity is shown read-only with a hint pointing to Inventory, and saving still succeeds because the unchanged quantity is posted back.

---

### Edge Cases

- Overflow: a change that would push quantity above `int.MaxValue` returns 409; the request DTO also limits `quantityChange` to -1,000,000..1,000,000 (400 otherwise).
- A note of only whitespace is stored as null; notes are trimmed.
- Reason matching is case-insensitive (`restock` is accepted) and the stored/returned value is the canonical enum name.
- Product deleted: its movements are deleted by the database cascade (verified on SQL Server only; see Known Gaps).
- A product created with quantity 0 has no movements until the first one is posted.
- Existing products have no history; their first movement shows `quantityAfter` computed from the current quantity.
- Movement whose acting user claim is missing or unparseable: stored with `UserId` null (cannot happen with tokens issued by `TokenService`).
- Threshold change in configuration takes effect on the next request (the flag is computed at read time, nothing is stored).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST store each stock change as a `StockMovement` with product, signed quantity change, quantity after, reason, optional note, acting user and UTC time.
- **FR-002**: `POST api/product/{id}/stock-movements` MUST be Admin-only, accept `{ quantityChange, reason, note? }` and return 201 with the movement (including `quantityAfter`).
- **FR-003**: The endpoint MUST accept only reasons `Restock`, `Adjustment`, `Damage`, `Return`; `Initial`, `Sale` and `OrderCancelled` are reserved (`Sale`/`OrderCancelled` for feature 019) and MUST be rejected with 400.
- **FR-004**: The endpoint MUST return 400 for a zero change, an invalid or missing reason, or a note over 200 characters; 404 for an unknown product; 409 problem details if the resulting quantity would be negative or overflow; 401/403 for missing/insufficient credentials.
- **FR-005**: The movement and the product quantity update MUST be saved in one `SaveChanges` so they cannot diverge.
- **FR-006**: Stock changes MUST be protected by an optimistic concurrency token (`Product.ConcurrencyStamp`, regenerated on every stock change); on conflict the service MUST reload and retry up to 3 attempts, then return 409.
- **FR-007**: `GET api/product/{id}/stock-movements` MUST be Admin-only, paged (`page`, `pageSize`, same clamping as products) and ordered newest first (`CreatedAtUtc` desc, `Id` desc), returning `PagedResponseDto<StockMovementResponseDto>`; 404 for an unknown product.
- **FR-008**: `POST api/product` with `Quantity` > 0 MUST record an `Initial` movement in the same save; with 0 it MUST NOT.
- **FR-009**: `PUT api/product/{id}` MUST return 400 `ValidationProblem` on key `Quantity` when `request.Quantity` differs from the stored quantity, leaving the product unchanged; an equal quantity is accepted. A concurrency conflict on PUT or DELETE MUST return 409 problem details.
- **FR-010**: `ProductResponseDto` MUST gain `bool IsLowStock = false` as the last positional parameter, after `ImageUrl`, set by `ToResponse` for every product response shape.
- **FR-011**: `Inventory:LowStockThreshold` (default 5) MUST define low stock as `Quantity < threshold`; a negative value falls back to 5; 0 disables the alert.
- **FR-012**: `GET api/product/low-stock` MUST be Admin-only, paged, ordered by `Quantity` then `Id`, and return only low-stock products.
- **FR-013**: When a movement with a negative change leaves quantity below the threshold the server MUST log one warning (Serilog via `ILogger`).
- **FR-014**: The schema change MUST be delivered as one generated EF Core migration `AddInventory`; existing products get no movement history.
- **FR-015**: MVC MUST provide an Admin-only Inventory list, product movement history with paging and an add-movement form, an "Inventory" nav link visible to Admins only, and a read-only Quantity on Product Edit; it MUST talk to the API only over HTTP.
- **FR-016**: All existing consumers that read `id`, `name`, `price`, `quantity`, `categoryId`, `imageUrl` MUST keep working; the only response-shape change is one additional boolean.

### Key Entities

- **StockMovement**: `Id`; `ProductId` (FK, cascade delete); `QuantityChange` (int, never 0); `QuantityAfter` (int, >= 0); `Reason` (enum stored as string, max 30); `Note` (string?, max 200); `UserId` (int?, FK to Users, set null on user delete); `CreatedAtUtc`. Index (`ProductId`, `CreatedAtUtc`).
- **Product** (existing): gains `ConcurrencyStamp` (Guid, concurrency token).
- **Low-stock threshold**: configuration value, not stored.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For every product created after this feature, the sum of its movements' `quantityChange` equals its current quantity.
- **SC-002**: 100% of quantity changes made through the API produce exactly one movement; 0 are possible through `PUT`.
- **SC-003**: No request ever leaves a product with a negative quantity.
- **SC-004**: A product below the threshold is flagged in 100% of product responses and listed by the low-stock endpoint; one at or above is in none.
- **SC-005**: Existing product consumers and tests keep working unchanged apart from the PUT quantity rule.

## Assumptions

- **Movement model**: fields as in Key Entities. `UserId` is `int?` with `OnDelete(SetNull)`; SQL Server allows this next to the product cascade because the two foreign keys come from different parents (no shared cascade path). No endpoint deletes users today (`UsersController` only has `GET` and `PUT {id}/role`).
- **Reasons**: `Initial`, `Restock`, `Adjustment`, `Damage`, `Return`, `Sale`, `OrderCancelled`, stored as strings (max 30). The admin endpoint accepts only `Restock`, `Adjustment`, `Damage`, `Return`; `Sale` and `OrderCancelled` are defined now so feature 019 needs no schema change.
- **Response code for movement create**: 201 without a `Location` header (there is no single-movement GET).
- **Acting user** is read from `ClaimTypes.NameIdentifier` (put there by `TokenService`; no controller reads it yet, so this feature is the first consumer).
- **Low-stock endpoint is paged** (Constitution I) although the request only said "list"; route `api/product/low-stock` is a literal segment, which ASP.NET Core attribute routing ranks above the untyped `{id}` template, so no constraint change to existing actions is needed.
- **`IsLowStock` is a new optional positional parameter** (`bool IsLowStock = false`) after `ImageUrl`: additive, existing constructor calls keep compiling, and JSON consumers see one extra property. All real responses go through `ToResponse`.
- **Concurrency** uses a client-set Guid token (not `rowversion`) for the same reason as 015: it also works on EF InMemory in tests.
- **No backfill**: existing products get no `Initial` movement; the generated migration is not hand-edited (Constitution II), so their `ConcurrencyStamp` is `Guid.Empty`, which is a valid token.
- **Existing tests**: no current test sends a PUT to a product (verified by search of `CategoryProduct.API.Tests`: only `POST api/product` and GETs), so no existing test changes quantity through PUT; new tests cover the rule. The requested "update existing tests" step is therefore a no-op, and the tasks include a verification step.
- **Depends on 017**: tasks build on 017's `ProductResponseDto.ImageUrl`, `Infrastructure/ProductMappingExtensions.cs` and the controllers' use of `ToResponse(Url)`.
- **Out of scope**: orders (019), reservation holds, per-location stock, CSV export, email alerts, editing or deleting movements (the log is append-only).

## Known Gaps

- COVERED by `020-hardening-followups` US1 SQL Server tests, pending the first CI run (cascade, `SetNull`, concurrent writes). Original gap: database cascade of movements on product delete, the `SetNull` foreign key and truly concurrent writes are SQL Server behaviors that EF InMemory does not reproduce; they are verified manually (quickstart), and concurrency is simulated with a `SaveChangesInterceptor` as in 015.
- No backfill means pre-existing products show history only from their first movement on.
- The warning log is not an alert delivery channel; it only helps operators reading the logs.
- CLOSED by `020-hardening-followups` US3 (now `409`). Original gap: the 017 image endpoints (`POST`/`DELETE api/product/{id}/image`) also update `Product` but do not
  catch `DbUpdateConcurrencyException`; if a stock change commits between their read and save they
  return `500` instead of `409` (rare; found in Review).
- The MVC Inventory pages were not run in a browser.
