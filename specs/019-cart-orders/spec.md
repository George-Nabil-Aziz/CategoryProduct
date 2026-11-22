# Feature Specification: Cart and Orders

**Feature Branch**: `019-cart-orders`

**Created**: 2026-11-22

**Status**: Draft

**Input**: User description: "A cart per signed-in user, persisted in the database. Checkout creates an Order and deducts stock in a transaction; reject when stock is insufficient. Order statuses: Pending, then Confirmed or Cancelled; cancelling restores stock. A user sees their own orders; an Admin sees all orders and changes status. MVC pages."

**Depends on**: 018-inventory (planned: `InventoryService`, `StockMovement`, `StockMovementReason.Sale` and `OrderCancelled`, `Product.ConcurrencyStamp`, retry-on-conflict pattern) and, through it, 017-product-images. Implemented after 018. Items marked "planned by 018" do not exist in the code yet; everything else was verified in the repository (see research.md).

> **Analyze resolution (2026-11-22)**: 018 is implemented and already provides `InventoryService.StageMovementAsync(int productId, int quantityChange, StockMovementReason reason, string? note, int? userId, CancellationToken ct = default)` returning `MovementResult(MovementStatus Status, StockMovement? Movement)` (statuses `Ok`, `NotFound`, `InsufficientStock`; no save), plus `LogIfLowStock(StockMovement)` to call after a successful save and `int Threshold`. 019 uses these as-is; wherever this document says `StageMovement(Product ...)` / `StagedMovement`, read `StageMovementAsync(productId, ...)` / `MovementResult`. It loads the product by id, which returns the already-tracked instance from the cart `Include(Product)`, so staging several lines and saving once still works. No change to `InventoryService.cs` is needed.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Signed-in user keeps a cart (Priority: P1)

Any signed-in user (role `User` or `Admin`) has one cart stored in the database. They add products, change quantities, remove lines and clear the cart. Each user sees and changes only their own cart. The cart does not reserve stock.

**Why this priority**: Checkout has nothing to buy without a cart.

**Independent Test**: As `plainuser`, `POST api/cart/items` `{ "productId": 1, "quantity": 2 }` returns 200 and a cart with one line of quantity 2; `GET api/cart` returns the same line with name, unit price, line total, available quantity and a cart total.

**Acceptance Scenarios**:

1. **Given** an empty cart, **When** the user posts `productId` 1 and `quantity` 2, **Then** the response is 200 with one line (product name, `unitPrice` equal to the current product price, `quantity` 2, `lineTotal` price x 2, `availableQuantity` equal to the product's stock) and `total` equal to the sum of line totals.
2. **Given** a cart that already has product 1 with quantity 2, **When** the user posts product 1 with quantity 3, **Then** the lines are merged into one line of quantity 5 (no duplicate line).
3. **Given** a line of quantity 98, **When** the user posts the same product with quantity 5, **Then** the response is 400 (validation problem on `Quantity`: the cart cannot hold more than 100 of one product) and the line stays at 98.
4. **Given** any cart, **When** `quantity` is below 1 or above 100, or `productId` is below 1, **Then** the response is 400 and nothing changes.
5. **Given** an unknown product id, **When** the user posts it, **Then** 404 and nothing is stored.
6. **Given** a line for product 1, **When** the user sends `PUT api/cart/items/1` `{ "quantity": 7 }`, **Then** the line quantity becomes exactly 7 (replace, not merge); 400 outside 1..100; 404 if the cart has no such line.
7. **Given** a line for product 1, **When** the user calls `DELETE api/cart/items/1`, **Then** 200 with the updated cart; 404 if there is no such line.
8. **Given** a cart with lines, **When** the user calls `DELETE api/cart`, **Then** 204 and the cart is empty; calling it on an empty cart also returns 204.
9. **Given** two users with carts, **Then** each `GET api/cart` returns only the caller's lines and no endpoint accepts a user id.
10. **Given** no token, **Then** every cart endpoint returns 401.
11. **Given** the stored product price changes after the line was added, **Then** the next `GET api/cart` shows the new price (the cart shows live prices; the price is frozen only at checkout).
12. **Given** a product whose quantity is lower than the line quantity, **Then** the cart still holds the line and `availableQuantity` shows the real stock so the client can warn.

---

### User Story 2 - Checkout creates an order and deducts stock (Priority: P1)

A user checks out their cart. In one all-or-nothing operation the system deducts the stock of every line, records one `Sale` stock movement per line, creates a `Pending` order with price snapshots and empties the cart. If any product lacks stock nothing changes and the user is told which products are short.

**Why this priority**: This is the point of the feature and the first consumer of 018's stock service.

**Independent Test**: With a cart of product 1 (quantity 2, stock 5), `POST api/orders` returns 201 with a `Pending` order whose total is price x 2; product 1 now has quantity 3, a `Sale` movement of -2 exists for it, and `GET api/cart` is empty.

**Acceptance Scenarios**:

1. **Given** a cart with lines that are all in stock, **When** the user posts `api/orders` (no body), **Then** 201 with a `Location` header for `GET api/orders/{id}` and an order body: status `Pending`, one item per cart line with `productId`, `productName`, `unitPrice`, `quantity`, `lineTotal`, and `total` equal to the sum of line totals.
2. **Given** that checkout, **Then** every product's quantity is reduced by its line quantity, exactly one `StockMovement` per line exists with reason `Sale`, `quantityChange` = minus the line quantity, `quantityAfter` = the new stock and the buyer as user, and the cart has no lines.
3. **Given** an empty cart, **Then** 400 (validation problem on `Cart`) and no order is created.
4. **Given** a cart where at least one product has less stock than its line quantity, **Then** 409 problem details whose detail and `items` extension list each short product (id, name, requested, available); no stock changed, no movement, no order, and the cart is untouched.
5. **Given** a successful checkout, **When** the product price changes later, **Then** the order's `unitPrice`, `lineTotal` and `total` do not change.
6. **Given** a stock change commits between the checkout's read and its save, **Then** the whole checkout is re-read and retried (up to 3 attempts); if stock is now insufficient the result is the stock 409; if all 3 attempts hit conflicts the result is a 409 conflict problem.
7. **Given** two checkouts of the same cart sent at the same time, **Then** at most one order is created and stock is deducted once.
8. **Given** two users buying the last unit of a product at the same time, **Then** exactly one order succeeds and the other gets the stock 409; stock never goes negative.
9. **Given** no token, **Then** 401.

---

### User Story 3 - Users read their orders, Admin reads all (Priority: P1)

A user lists their own orders (newest first, paged) and opens one. An Admin can open any order and list all orders with an optional status filter. A user asking for someone else's order gets 404, never 403.

**Why this priority**: Orders are only useful if people can see them.

**Independent Test**: User A checks out; `GET api/orders` as A lists the order; as B it lists nothing and `GET api/orders/{id}` returns 404; as Admin `GET api/orders/{id}` returns 200 and `GET api/orders/all` includes it.

**Acceptance Scenarios**:

1. **Given** a user with several orders, **When** `GET api/orders?page=1&pageSize=2`, **Then** a `PagedResponseDto` of the caller's orders only, newest first (`createdAtUtc` desc, `id` desc), each summary with `id`, `status`, `total`, `itemCount`, `createdAtUtc`, `updatedAtUtc`, with correct `totalCount` and `totalPages`; `page` is at least 1 and `pageSize` is clamped to 1..50.
2. **Given** an order owned by A, **When** A calls `GET api/orders/{id}`, **Then** 200 with the full order including items; **When** another non-Admin calls it, **Then** 404; **When** an Admin calls it, **Then** 200; an unknown id gives 404 for everyone.
3. **Given** an Admin, **When** `GET api/orders/all?status=Pending&page=1&pageSize=10`, **Then** a paged list of all users' `Pending` orders including `userId` and `userName`, newest first; without `status` all orders; an unknown or numeric status gives 400 on `status`.
4. **Given** a non-Admin, **Then** `GET api/orders/all` is 403; no token gives 401 on every read endpoint.
5. **Given** a product that was deleted after the order was placed, **Then** the order still shows its items from the snapshot (`productName`, `unitPrice`) with `productId` null.

---

### User Story 4 - Cancelling a Pending order restores stock (Priority: P1)

The owner (or an Admin) cancels an order while it is `Pending`. The order becomes `Cancelled` and each line's quantity is returned to stock with an `OrderCancelled` movement.

**Why this priority**: Without it stock would leak on every abandoned order.

**Independent Test**: Check out 2 units of product 1 (stock 5 to 3); `POST api/orders/{id}/cancel` returns 200 with status `Cancelled`; product 1 is back at 5 and has a +2 `OrderCancelled` movement.

**Acceptance Scenarios**:

1. **Given** a `Pending` order owned by the caller, **When** `POST api/orders/{id}/cancel`, **Then** 200 with the order (status `Cancelled`, `updatedAtUtc` refreshed), each line's quantity is added back to its product and exactly one `OrderCancelled` movement per restored line exists (`quantityChange` positive, acting user, note "Order #id cancelled").
2. **Given** an order that is `Confirmed` or already `Cancelled`, **Then** 409 problem details and no stock change.
3. **Given** another user's order, **Then** 404 for a non-Admin; an Admin may cancel any `Pending` order.
4. **Given** an order line whose product has been deleted (`productId` null), **Then** that line is skipped (no stock to restore, no movement) and the cancel still succeeds; the skip is logged.
5. **Given** two cancels of the same order at the same time, **Then** stock is restored once (the second sees `Cancelled` and gets 409).
6. **Given** a concurrent stock change on a line's product, **Then** the cancel is retried up to 3 attempts and then returns 409.
7. **Given** no token, **Then** 401.

---

### User Story 5 - Admin confirms or cancels orders (Priority: P2)

An Admin moves a `Pending` order to `Confirmed` or `Cancelled` with `PUT api/orders/{id}/status`. Every other transition is refused.

**Why this priority**: Completes the lifecycle; the shop works without it only until orders pile up.

**Independent Test**: As Admin `PUT api/orders/{id}/status` `{ "status": "Confirmed" }` on a Pending order returns 200 with status `Confirmed`; repeating it returns 409.

**Acceptance Scenarios**:

1. **Given** a `Pending` order, **When** an Admin sets `Confirmed`, **Then** 200, status `Confirmed`, `updatedAtUtc` refreshed, stock untouched.
2. **Given** a `Pending` order, **When** an Admin sets `Cancelled`, **Then** it behaves exactly like story 4 (stock restored with `OrderCancelled` movements).
3. **Given** a `Confirmed` or `Cancelled` order, **When** any status is sent (including the same one), **Then** 409 and no change; **Given** a `Pending` order and status `Pending`, **Then** 409.
4. **Given** a status that is empty, unknown, or numeric such as `"1"`, **Then** 400 on `Status`.
5. **Given** a non-Admin (including the order's owner), **Then** 403; no token 401; unknown order 404.
6. **Given** a confirm and a cancel of the same order at the same time, **Then** exactly one wins and the other gets 409; a cancelled order never ends as `Confirmed` or the reverse.

---

### User Story 6 - Cart and checkout pages in the MVC app (Priority: P2)

A signed-in user sees an "Add to cart" button on the product list and product details, opens "Cart" from the navigation bar to change quantities, remove lines and check out, and lands on the new order's page. "My Orders" lists their orders and each order's page shows the items and a Cancel button while it is Pending. API errors such as insufficient stock are shown.

**Why this priority**: Gives the feature a usable front end; the API is complete without it.

**Independent Test**: Sign in as a normal user, add a product from `/Products`, open `/Cart`, change the quantity, press Checkout, see the order page with status Pending, cancel it and see Cancelled.

**Acceptance Scenarios**:

1. **Given** a signed-in user, **Then** the product list and details show an "Add to cart" form (quantity 1 to 100, anti-forgery token); anonymous visitors do not see it. After adding, the user returns to the same page with a success message.
2. **Given** the navigation bar, **Then** signed-in users see "Cart" and "My Orders"; anonymous users see neither.
3. **Given** `/Cart`, **Then** each line shows name, unit price, quantity (editable, with Update), line total, a stock warning when `availableQuantity` is lower than the quantity, a Remove button, the cart total, "Clear cart" and "Checkout". An empty cart shows a message and no Checkout button.
4. **Given** Checkout succeeds, **Then** the user is redirected to `/Orders/{id}`; **Given** 409 (insufficient stock) or 400, **Then** the cart page shows the API message (which products are short) and the cart is unchanged.
5. **Given** `/Orders`, **Then** the user's orders are listed newest first (id, date, status, total, item count) with paging and a Details link.
6. **Given** `/Orders/{id}`, **Then** items, totals and status are shown, with a Cancel button only while Pending; another user's order id shows 404.
7. **Given** an expired session (API 401), **Then** the user is signed out and sent to the login page like `ProductsController`.
8. **Given** any post action, **Then** it requires the anti-forgery token.

---

### User Story 7 - Admin order management page in the MVC app (Priority: P2)

An Admin opens "Manage Orders" from the navigation bar, filters by status and confirms or cancels Pending orders.

**Why this priority**: Gives the Admin workflow a front end.

**Independent Test**: As Admin open `/Orders/Manage`, filter `Pending`, press Confirm on an order and see it become Confirmed.

**Acceptance Scenarios**:

1. **Given** an Admin, **Then** the navigation bar shows "Manage Orders" (hidden for everyone else) and `/Orders/Manage` lists all orders paged with user name, status, total, date and a status filter (All, Pending, Confirmed, Cancelled).
2. **Given** a Pending row, **Then** Confirm and Cancel buttons are shown; other rows show none. Pressing one shows a success message or the API error (for example a 409 because someone else already changed the order).
3. **Given** a non-Admin or anonymous user, **Then** `/Orders/Manage` redirects to login or access denied; an Admin can open any order at `/Orders/{id}`.

---

### Edge Cases

- The cart never reserves stock: adding is allowed even when `quantity` exceeds stock; only checkout enforces stock. A product with stock 0 can sit in a cart but blocks checkout until the line is removed or reduced.
- Cart line quantity is capped at 100 per product; a merge that would pass 100 is refused whole (400), not clamped.
- Checkout order of checks: auth, empty cart (400), stock (409), apply. The 409 lists all short products, not just the first.
- A cart line whose product was deleted is removed by the database cascade (verified on SQL Server only; see Known Gaps).
- Totals are exact `decimal(18,2)` sums of `unitPrice x quantity`.
- Order status strings are matched by name, case-insensitively; the stored and returned value is the canonical name.
- An Admin may check out, view and cancel like any user; cart endpoints never take a user id.
- Cancel adds the restored quantity to the current stock whatever it is now; overflow beyond `int.MaxValue` is treated as a conflict (practically unreachable).
- A user who double-clicks Checkout gets one order and a 400 or 409 for the duplicate request.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST persist one cart per user as `CartItem` rows (`UserId`, `ProductId`, `Quantity` 1..100, `AddedAtUtc`) with a unique (`UserId`, `ProductId`) constraint; carts MUST NOT reserve stock.
- **FR-002**: `api/cart` endpoints MUST be `[Authorize]` (any role), MUST operate only on the caller's cart (user id from `ClaimTypes.NameIdentifier`) and MUST NOT accept a user id.
- **FR-003**: `GET api/cart` MUST return lines with `productId`, `productName`, `unitPrice`, `quantity`, `lineTotal`, `availableQuantity` and the cart `total`, ordered by `AddedAtUtc` then `Id`. The cart is bounded (one line per product, 100 each) so it is not paged; this is a deliberate exception to Constitution I, recorded in the plan.
- **FR-004**: `POST api/cart/items` MUST add a product or merge into the existing line, return 400 when the quantity is outside 1..100 or the merged quantity would exceed 100, 404 for an unknown product, and 200 with the updated cart.
- **FR-005**: `PUT api/cart/items/{productId}` MUST set the line quantity (1..100), `DELETE api/cart/items/{productId}` MUST remove the line (200 with the cart), and `DELETE api/cart` MUST clear the cart (204); a missing line is 404.
- **FR-006**: `POST api/orders` MUST create an order from the caller's cart: 400 on an empty cart, 409 problem details listing every product with insufficient stock (nothing changed), otherwise 201 with the order and a `Location` header.
- **FR-007**: A successful checkout MUST, in one unit of work, deduct each line's stock through `InventoryService` with reason `Sale` (one `StockMovement` per line, `UserId` = buyer), create an `Order` with status `Pending` and `OrderItem` snapshots (`ProductName`, `UnitPrice`, `Quantity`, `LineTotal`), compute `Total`, delete the caller's cart lines and persist everything with one `SaveChanges`, inside an explicit database transaction when `Database.IsRelational()`.
- **FR-008**: On `DbUpdateConcurrencyException` the whole checkout MUST be re-read and retried, up to 3 attempts, then return 409.
- **FR-009**: Order status MUST be one of `Pending`, `Confirmed`, `Cancelled`, stored as a string. Allowed transitions: `Pending` to `Confirmed` and `Pending` to `Cancelled`; all others are refused with 409.
- **FR-010**: `PUT api/orders/{id}/status` MUST be Admin-only, accept `{ status }` (matched by name), return 400 for an invalid status value, 404 for an unknown order, 409 for a refused transition, and 200 with the order.
- **FR-011**: `POST api/orders/{id}/cancel` MUST be available to the order's owner or an Admin while the order is `Pending` (409 otherwise; 404 for a non-owner non-Admin).
- **FR-012**: Cancelling (by either route) MUST, in one unit of work, set the status to `Cancelled`, add each line's quantity back to its product through `InventoryService` with reason `OrderCancelled` (one movement per restored line, acting user), skip lines whose product no longer exists, and retry up to 3 attempts on concurrency conflicts, then 409. A cancelled order's stock MUST be restored exactly once.
- **FR-013**: `Order` MUST carry a `ConcurrencyStamp` (Guid concurrency token) regenerated on every status change so racing status changes cannot both succeed.
- **FR-014**: `GET api/orders` MUST return the caller's orders as a `PagedResponseDto` (`page` min 1, `pageSize` 1..50, default 10), newest first (`CreatedAtUtc` desc, `Id` desc). `GET api/orders/{id}` MUST return the order to its owner or an Admin and 404 to anyone else. `GET api/orders/all?status=&page=&pageSize=` MUST be Admin-only, paged, newest first, with an optional validated status filter.
- **FR-015**: Deleting a product MUST remain possible when orders reference it: `OrderItem.ProductId` is nullable with `OnDelete(SetNull)` and the item keeps its snapshot; cart lines for the product are removed by cascade.
- **FR-016**: The schema change MUST be one generated EF Core migration `AddCartAndOrders`, not hand-edited, with no `database update`.
- **FR-017**: MVC MUST provide: "Add to cart" on the product list and details for signed-in users; `/Cart` (update quantity, remove, clear, checkout); `/Orders` (own orders, paged); `/Orders/{id}` (details, Cancel while Pending); Admin `/Orders/Manage` (all orders, status filter, Confirm and Cancel); navigation links "Cart" and "My Orders" for signed-in users and "Manage Orders" for Admins. It MUST talk to the API only over HTTP, show API errors, and protect every post with an anti-forgery token.
- **FR-018**: Checkout and cancel MUST keep `Product.Quantity` equal to the sum of its movements (for products created after 018) and MUST never leave a negative quantity.

### Key Entities

- **CartItem**: `Id`; `UserId` (FK Users, cascade); `ProductId` (FK Products, cascade); `Quantity` (int, 1..100); `AddedAtUtc`. Unique index (`UserId`, `ProductId`).
- **Order**: `Id`; `UserId` (FK Users, restrict); `Status` (`OrderStatus`, string, max 20); `Total` (decimal 18,2); `CreatedAtUtc`; `UpdatedAtUtc`; `ConcurrencyStamp` (Guid token). Index (`UserId`, `CreatedAtUtc`).
- **OrderItem**: `Id`; `OrderId` (FK Orders, cascade); `ProductId` (int?, FK Products, set null); `ProductName` (max 100); `UnitPrice` (decimal 18,2); `Quantity`; `LineTotal` (decimal 18,2).
- **OrderStatus**: `Pending`, `Confirmed`, `Cancelled`.
- **StockMovement / Product.ConcurrencyStamp** (planned by 018): reused unchanged.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: After any number of checkouts and cancellations, each product's quantity equals its starting quantity minus the quantities of non-cancelled orders; none is ever negative.
- **SC-002**: A checkout either completes fully (order, items, deductions, movements, empty cart) or changes nothing; 100% of 409 stock rejections leave stock, movements, orders and the cart as they were.
- **SC-003**: Every cancellation of a Pending order restores stock exactly once, whatever the number of simultaneous attempts.
- **SC-004**: 0 requests let a user read or change another user's cart or order; non-owner order reads return 404.
- **SC-005**: Order totals never change after creation when product prices change.
- **SC-006**: A product with order history can still be deleted.
- **SC-007**: Existing API consumers and tests keep working unchanged (this feature adds endpoints and tables only).

## Assumptions

- **Cart model**: fields as in Key Entities; `Quantity` limited to 1..100 by the request DTOs and checked on merge; unique (`UserId`, `ProductId`); both foreign keys cascade. Safe on SQL Server: `CartItems` is reached from `Users` by one cascade path and from `Categories` through `Products` by one cascade path; no table is reached twice from the same root.
- **Cart API shapes**: every cart mutation except clear returns the updated cart (200) so a client needs no second call; clear returns 204. Cart endpoints are not paged (bounded size), recorded as a Constitution I exception in the plan.
- **Concurrent cart adds**: the unique index is the guard; a `DbUpdateException` from a duplicate insert is retried once as a merge. EF InMemory does not enforce unique indexes, so this path is verified on SQL Server only (Known Gaps).
- **Order model**: `Order.UserId` uses `OnDelete(Restrict)` (not cascade) so sales records outlive a user; no endpoint deletes users (verified: `UsersController` has only `GET` and `PUT {id}/role`), so this blocks nothing today. `OrderItem.OrderId` cascades; `OrderItem.ProductId` is `int?` with `SetNull`. Cascade-path check against the current model (`Product.CategoryId` cascades from `Categories`; `RefreshTokens.UserId` cascades from `Users`, both verified in the snapshot): from `Products` the new tables add `CartItems` (cascade), `OrderItems` (set null) and the planned `StockMovements` (cascade), each reached once; `Users` reaches `CartItems` (cascade), `RefreshTokens` (cascade), `Orders` (restrict) and `StockMovements` (set null, planned); `Orders` reaches `OrderItems` (cascade). SQL Server accepts this because no table has two cascading paths from one root.
- **Snapshots**: `OrderItem.ProductName` (max 100, same as the product name request limit) and `UnitPrice` are copied at checkout; `LineTotal` and `Order.Total` are computed once and stored.
- **Money type**: `decimal(18,2)`, the same as `Product.Price` in the current model snapshot (verified).
- **Status handling**: names matched case-insensitively against the enum names (numeric strings rejected), as 018 does for reasons. `PUT status` with `Pending` or a repeat of the current status is a valid name but an invalid transition and yields 409; an unrecognised value yields 400.
- **Cancel by owner vs by Admin**: both use the same code. `POST api/orders/{id}/cancel` hides foreign orders as 404 for non-Admins; `PUT status` is Admin-only so it never needs the ownership rule.
- **Movements link to orders only by note**: the planned 018 `StockMovement` has no `OrderId`. `Sale` movements use note "Checkout" (the order id does not exist until the single save); `OrderCancelled` movements use note "Order #{id} cancelled". Adding an `OrderId` column to `StockMovement` was rejected as a change to 018's model that nothing here needs.
- **Acting user** for movements is the caller read from `ClaimTypes.NameIdentifier` (written by `TokenService`, verified); for a cancel by an Admin it is the Admin.
- **Stock service**: planned 018 `InventoryService.ApplyMovementAsync` loads the product and calls `SaveChanges` itself (018 tasks T015). Checkout and cancel stage several movements and save once, so a non-saving `StageMovement` overload is a required change to 018's code (tasks T003; fold it into 018 T015 if 018 is not merged yet).
- **Transactions**: a single `SaveChanges` is already atomic on SQL Server; the explicit transaction guarded by `Database.IsRelational()` (pattern of `UsersController.UpdateRoleOnceAsync`, verified) is kept as required, so the code stays correct if a second save is ever added. No retrying execution strategy is configured (`UseSqlServer` without `EnableRetryOnFailure` in `Program.cs`, verified), so user-initiated transactions are allowed.
- **Where the logic lives**: checkout and cancel are private methods of `OrdersController` (Constitution III); the only shared abstraction is the planned `InventoryService`. No `OrderService`.
- **Pagination defaults**: order lists default `pageSize` 10, clamped 1..50, like `GET api/users`.
- **Response codes**: checkout 201 with `Location`; cancel and status 200 with the order; the stock shortage 409 is `ProblemDetails` with an `items` extension; other 409s use the same `ProblemDetails` shape as `UsersController`.
- **Existing tests**: none touches carts or orders; products seeded by `ApiFactory` have quantity 1 (verified), so tests needing more stock create products through `POST api/product` as Admin (which writes an `Initial` movement once 018 is in).
- **Out of scope**: payment, shipping, addresses, order editing, partial cancel or refund, further statuses, stock reservation or cart expiry, coupons and taxes, email, user deletion, guest carts.

## Known Gaps

- COVERED by `020-hardening-followups` US1 SQL Server tests, pending the first CI run. Original gap: same-cart double checkout: on SQL Server it cannot produce two orders (both requests include the cart's Product rows, so the `ConcurrencyStamp` UPDATE and the cart-row DELETE of the loser affect 0 rows inside a rolled-back transaction; under READ COMMITTED a reader never sees the winner's uncommitted state). EF InMemory applies a save entry by entry and lets readers see it half-applied, so a real two-thread test was intermittently red (two orders). It is replaced by an interceptor-driven interleaving (`ParallelCheckoutOfSameCart_CreatesOneOrder`, asserts the loser gets 400 after one retry; the no-second-order guarantee itself is not assertable on InMemory); true parallelism is verified on SQL Server only.

- COVERED by `020-hardening-followups` US1 SQL Server tests (atomicity/rollback, unique cart index, `SetNull`, cascade), pending the first CI run. Original gap: true transaction atomicity, the unique cart index, concurrent cart inserts, `SetNull` on `OrderItems.ProductId` and the cascade removal of cart lines on product delete are SQL Server behaviors that EF InMemory does not reproduce; they are verified manually (quickstart). Concurrency of stock and order changes is simulated with a `SaveChangesInterceptor` as in 015 and 018, which proves the retry logic but not database locking.
- `Sale` movements are not linked to their order by a column, only by the note "Checkout"; the order id cannot be added without a second save or a schema change to 018.
- Cart lines show live prices; a user can see a different price at checkout than when adding. The order stores what was charged.
- No stock reservation: stock can run out between adding to the cart and checking out; checkout reports it.
- The MVC app has no test project; MVC behavior is verified through `quickstart.md`.
- T049 stays open until the 020 `sqlserver-tests` CI run is green. SQL Server behavior was not verified in this implementation pass (T049 is open): explicit transactions and rollback, cascade removal of cart lines, `SetNull` on `OrderItems.ProductId`, the unique (`UserId`, `ProductId`) cart index and the migration being applied have only been checked against EF InMemory and the generated migration text. EF InMemory is not transactional, so the API race tests (`CartRacingFactory`) prove the retry and 409 logic and the final stock/status, but not that a failed save leaves no order, no `Sale`/`OrderCancelled` movement and an intact cart; that rollback is expected on SQL Server only. To make a failed first cancel save reach the product update before the order update on InMemory, `CancelCoreAsync` reads the order untracked and attaches it after staging the movements.
