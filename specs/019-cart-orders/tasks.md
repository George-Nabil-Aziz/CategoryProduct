---

description: "Task list for 019-cart-orders"
---

# Tasks: Cart and Orders

**Input**: Design documents from `/specs/019-cart-orders/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Required for the API. API integration tests go in `CategoryProduct.API.Tests` (`CartTests.cs`, `OrderCheckoutTests.cs`, `OrderReadTests.cs`, `OrderLifecycleTests.cs`) and are written before the implementation in each story. MVC has no test project; MVC behavior is verified through `quickstart.md`.

**Rules**: Do not hand-edit migrations. Do not run `dotnet ef database update`. 015, 016, 017 and 018 are implemented first: this feature uses 018's `InventoryService`, `StockMovement`, `StockMovementReason.Sale`/`OrderCancelled` and `Product.ConcurrencyStamp`. `Program.cs` needs no change. Phases 2 to 7 are API work and phases 8 to 10 are MVC work: they touch disjoint files, so two agents can work in parallel once phase 7's API contract (`contracts/api-endpoints.md`) is fixed (MVC work needs only the contract, not the finished API, until the manual walkthrough).

## Format: `[ID] [P?] [Story] Description`

## Phase 1: Setup

- [X] T001 Confirm prerequisites: `dotnet build CategoryProduct.slnx` and `dotnet test CategoryProduct.slnx` pass, and these exist: `CategoryProduct.API/Services/InventoryService.cs`, `CategoryProduct.API/Models/StockMovement.cs`, `CategoryProduct.API/Models/StockMovementReason.cs` (with `Sale` and `OrderCancelled`), `Product.ConcurrencyStamp` in `CategoryProduct.API/Models/Product.cs`, `CategoryProduct.API.Tests/InventoryTests.cs`. If any is missing, stop: 018 must be implemented first.
- [X] T002 [P] Read `CategoryProduct.API.Tests/RefreshTokenTests.cs` (`RacingFactory`, `RaceInterceptor`) and 018's `RacingFactory` in `InventoryTests.cs` so the race helpers in T012 reuse the same pattern (no code change)

---

## Phase 2: Foundational API (Blocking Prerequisites)

**StageMovementAsync (already provided by 018; verify + tests)**

- [X] T003 In `CategoryProduct.API.Tests/InventoryTests.cs` add unit tests for the existing `InventoryService.StageMovementAsync` used the 019 way (InMemory context built directly): staging changes for two tracked products then ONE `SaveChangesAsync` persists both quantities and two movements; a staged `InsufficientStock` leaves the product unchanged and adds no movement; staging does not save (a fresh context sees old values before save)
- [X] T004 Verify (no code change expected) that `CategoryProduct.API/Services/InventoryService.cs` exposes `StageMovementAsync`, `LogIfLowStock` and `MovementResult` as described in the plan's Analyze resolution note; call `LogIfLowStock` for each staged movement after a successful checkout save

**Models, schema and DTOs**

- [X] T005 [P] Create `CategoryProduct.API/Models/OrderStatus.cs`: enum `Pending, Confirmed, Cancelled` and static `OrderStatusExtensions.TryParseName(string?, out OrderStatus)` matching `Enum.GetNames` case-insensitively (rejects numeric strings and whitespace)
- [X] T006 [P] Create `CategoryProduct.API/Models/CartItem.cs`, `CategoryProduct.API/Models/Order.cs` and `CategoryProduct.API/Models/OrderItem.cs` per `data-model.md` (`Order.ConcurrencyStamp = Guid.NewGuid()`, `Order.Items = new()`)
- [X] T007 In `CategoryProduct.API/Data/ApplicationDbContext.cs` add `DbSet<CartItem> CartItems`, `DbSet<Order> Orders`, `DbSet<OrderItem> OrderItems` and configure: `CartItem` unique index (`UserId`, `ProductId`), `HasOne(User)` `OnDelete(Cascade)`, `HasOne(Product)` `OnDelete(Cascade)`; `Order` `Status` `.HasConversion<string>().HasMaxLength(20).IsRequired()`, `Total` `decimal(18,2)`, `ConcurrencyStamp` `.IsConcurrencyToken()`, `HasOne(User)` `OnDelete(Restrict)`, `HasMany(Items).WithOne(Order).HasForeignKey(OrderId).OnDelete(Cascade)`, `HasIndex(UserId, CreatedAtUtc)`; `OrderItem` `ProductName` max 100 required, `UnitPrice` and `LineTotal` `decimal(18,2)`, `HasOne<Product>().WithMany().HasForeignKey(ProductId).OnDelete(SetNull)`
- [X] T008 Generate the migration with `dotnet ef migrations add AddCartAndOrders --project CategoryProduct.API` (output in `CategoryProduct.API/Migrations/`); do not edit it and do not run `database update`; confirm it contains `CartItems`, `Orders`, `OrderItems`, the unique index, `ReferentialAction.Restrict` for `Orders.UserId` and `SetNull` for `OrderItems.ProductId`, and that generation produced no error; confirm the snapshot diff contains only 019's tables (018's `AddInventory` already applied to the snapshot)
- [X] T009 [P] Create DTOs per `data-model.md`: `CategoryProduct.API/Dtos/CartItemRequestDto.cs` (`AddCartItemRequestDto`, `UpdateCartItemRequestDto` with `[Range]` attributes), `CategoryProduct.API/Dtos/CartResponseDto.cs` (`CartResponseDto`, `CartItemResponseDto`), `CategoryProduct.API/Dtos/OrderResponseDto.cs` (`OrderResponseDto`, `OrderItemResponseDto`, `OrderSummaryDto`), `CategoryProduct.API/Dtos/UpdateOrderStatusRequestDto.cs` (`[Required(AllowEmptyStrings = false)] string Status`)
- [X] T010 [P] Create `CategoryProduct.API/Infrastructure/OrderMappingExtensions.cs`: `ToResponse(this Order)` to `OrderResponseDto` (status as `ToString()`, items mapped, `UserName` from the `User` navigation when loaded) and `ToSummary(this Order)` is NOT needed (lists project in SQL); add `BuildCart(IEnumerable<CartItemResponseDto>)` returning `CartResponseDto` with `ItemCount` and summed `Total`
- [X] T011 [P] Create `CategoryProduct.API.Tests/CartOrderTestHelpers.cs` (internal static class): `CreateProductAsync(admin client, name, price, quantity)` through `POST api/product` returning the id; `AddToCartAsync(client, productId, quantity)`; `CheckoutAsync(client)`; `GetProductQuantityAsync(factory/client, id)` via `GET api/product/{id}`; `GetMovementsAsync(adminClient, productId)` via `GET api/product/{id}/stock-movements`; `RegisterAndLoginAsync(factory)` creating a fresh user with `POST api/account/register` and `AuthHelper.LoggedInAsync`; readers for the order JSON
- [X] T012 [P] In `CategoryProduct.API.Tests/CartOrderTestHelpers.cs` add `CartRacingFactory : ApiFactory` with a `SaveChangesInterceptor` (pattern from `RefreshTokenTests.RaceInterceptor` and 018's `RacingFactory`): a configurable `Action<ApplicationDbContext>`/mode that, when armed N times and the saving context has a tracked `Order` entry in state `Added` or `Modified` (or a `StockMovement` `Added`), uses a second `ApplicationDbContext` on the same `DbName`/`DbRoot` to change the stock of a given product (add quantity and set a new `ConcurrencyStamp`) or to set the order's `ConcurrencyStamp`/`Status`; expose `Arm(int times)` and `Hits`

**Checkpoint**: `dotnet build CategoryProduct.slnx` succeeds and all existing tests still pass unchanged.

---

## Phase 3: User Story 1 - Cart API (Priority: P1) MVP

**Goal**: Persisted per-user cart with add/merge, update, remove, clear and read.

**Independent Test**: As `plainuser` add product 1 twice (2 then 3) and `GET api/cart` shows one line of quantity 5.

### Tests for User Story 1 (write first, expect failure)

- [X] T013 [US1] In `CategoryProduct.API.Tests/CartTests.cs` add read and add tests (fixture `IClassFixture<ApiFactory>`): empty cart returns `items` [] and `total` 0; add returns 200 with name, `unitPrice`, `lineTotal`, `availableQuantity`, `total`; merge of 2 then 3 gives one line of 5; cap: 98 then +5 returns 400 on `Quantity` and the line stays 98; quantity 0, -1, 101 and `productId` 0 return 400; unknown product 404 and nothing stored; ordering by add time
- [X] T014 [US1] In `CartTests.cs` add update/remove/clear tests: PUT sets exactly 7 (not merge), PUT 0/101 gives 400, PUT or DELETE of a missing line 404, DELETE line returns 200 with the cart, DELETE `api/cart` returns 204 and again 204 on an empty cart
- [X] T015 [US1] In `CartTests.cs` add isolation and auth tests: two users have separate carts (A's line never appears for B, B cannot change A's line through any route), Admin has their own cart, no token gives 401 on all five routes, a price change by Admin shows in the next `GET api/cart`, a line above stock is kept and `availableQuantity` shows the real stock, a product deleted through `DELETE api/product/{id}` leaves `GET api/cart` returning 200 (the cascade removal itself is verified on SQL Server, see quickstart)

### Implementation for User Story 1

- [X] T016 [US1] Create `CategoryProduct.API/Controllers/CartController.cs` (`[ApiController]`, `[Route("api/[controller]")]`, `[Authorize]`, injects `ApplicationDbContext`): `CurrentUserId()` helper from `ClaimTypes.NameIdentifier` (401 if unparseable), private `BuildCartAsync(userId)` (no-tracking query joined to `Products`, ordered by `AddedAtUtc` then `Id`, `CartResponseDto`), `[HttpGet]` returning it
- [X] T017 [US1] In `CartController.cs` add `[HttpPost("items")] AddItem(AddCartItemRequestDto)`: 404 for an unknown product, merge or create the line, model error on `Quantity` ("The cart cannot hold more than 100 of one product.") and `ValidationProblem` when the merge passes 100, `SaveChangesAsync`, on `DbUpdateException` `ChangeTracker.Clear()` and retry the merge once, return the cart
- [X] T018 [US1] In `CartController.cs` add `[HttpPut("items/{productId:int}")]`, `[HttpDelete("items/{productId:int}")]` (200 with cart, 404 when no line) and `[HttpDelete]` clear (`RemoveRange`, 204)

**Checkpoint**: US1 tests pass.

---

## Phase 4: User Story 2 - Checkout (Priority: P1)

**Goal**: All-or-nothing order creation with stock deduction, `Sale` movements, retries.

**Independent Test**: Cart of 2 x product 1 (stock 5): `POST api/orders` gives a Pending order, stock 3, a `Sale` -2 movement and an empty cart.

### Tests for User Story 2 (write first)

- [X] T019 [US2] In `CategoryProduct.API.Tests/OrderCheckoutTests.cs` add success tests: 201 with `Location` header pointing at `api/orders/{id}`, status `Pending`, items with snapshots and `total` equal to the sum; stock reduced by each quantity for a multi-line cart; exactly one `Sale` movement per line (`quantityChange` negative, `quantityAfter` correct, `userId` = buyer, note "Checkout") readable through `GET api/product/{id}/stock-movements` as Admin; the cart is empty afterwards; checkout of exactly the full stock leaves quantity 0 and succeeds; a later price change by Admin does not alter the order (`GET api/orders/{id}` after the change)
- [X] T020 [US2] In `OrderCheckoutTests.cs` add rejection tests: empty cart 400 on `Cart` and no order; insufficient stock 409 whose `detail` and `items` extension list every short product (two short products listed together) and afterwards stock, movements (no new `Sale`), `GET api/orders` and the cart are all unchanged; a cart with one short and one fine line changes nothing for the fine line; stock 0 product in the cart blocks checkout; no token 401
- [X] T021 [US2] In `OrderCheckoutTests.cs` add concurrency tests with `CartRacingFactory`: a stock change on a cart product committed during the first checkout save (armed once) makes the checkout succeed on attempt 2 with the right final stock (start + raced change - quantity); a raced change that leaves less stock than needed ends in the stock 409 with nothing created; armed on every attempt gives 409 after exactly 3 attempts (assert `Hits`) with no order, no `Sale` movement and the cart intact; two parallel checkouts of the same cart produce one order (`GET api/orders` count 1) and one deduction; two users buying the last unit in parallel give one 201 and one 409 and stock 0, never negative

### Implementation for User Story 2

- [X] T022 [US2] Create `CategoryProduct.API/Controllers/OrdersController.cs` (`[ApiController]`, `[Route("api/[controller]")]`, `[Authorize]`, injects `ApplicationDbContext`, `InventoryService`, `ILogger<OrdersController>`) with the same `CurrentUserId()` helper and a `[HttpGet("{id:int}")] GetById` placeholder returning 404 (completed in T028) so `CreatedAtAction` resolves
- [X] T023 [US2] In `OrdersController.cs` add `[HttpPost] Checkout()` per plan "Checkout": 3-attempt loop with `ChangeTracker.Clear()`, cart load with `Include(Product)` tracked, empty check (400 on `Cart`), shortage list (409 `ProblemDetails` with `Detail` text and `Extensions["items"]`), explicit transaction only when `_context.Database.IsRelational()`, `StageMovement(product, -quantity, Sale, "Checkout", userId)` per line (treat `InsufficientStock` as a shortage), order and item snapshots with computed totals, `RemoveRange` of the cart lines, ONE `SaveChangesAsync`, commit, catch `DbUpdateConcurrencyException` (roll back, retry; 409 after 3), log order id and total, call `LogIfLowStock` per line after commit, return `CreatedAtAction(nameof(GetById), new { id }, order.ToResponse())`

**Checkpoint**: US1 and US2 tests pass.

---

## Phase 5: User Story 3 - Read orders (Priority: P1)

**Goal**: Own orders, order details with ownership rule, Admin list with status filter.

**Independent Test**: A checks out; A lists and opens it; B gets an empty list and 404; Admin sees it in `all`.

### Tests for User Story 3 (write first)

- [X] T024 [US3] In `CategoryProduct.API.Tests/OrderReadTests.cs` add list tests: `GET api/orders` returns only the caller's orders, newest first with `id` desc tie-break, paging (`pageSize=2` over 5 orders gives `totalCount` 5, `totalPages` 3, correct contents), clamping (`pageSize=0` becomes 1, `500` becomes 50, `page=0` becomes 1), summary fields (`itemCount`, `total`, `status`), empty list for a user with no orders, 401
- [X] T025 [US3] In `OrderReadTests.cs` add detail tests: owner 200 with items; another non-Admin 404 (same status and body shape as an unknown id); Admin 200 for any order; unknown id 404; `api/orders/abc` 404; 401; items of a deleted product (delete the product as Admin) still show the snapshot name and price (`productId` null is verified on SQL Server only, see Known Gaps)
- [X] T026 [US3] In `OrderReadTests.cs` add Admin list tests: `GET api/orders/all` includes other users' orders with `userId` and `userName`; `status=Pending` filter, lower-case `pending` accepted, `status=Cancelled` after a cancel (use the cancel endpoint, so write this test last and mark it pending until T033 is done), `status=1` and `status=abc` give 400, paging and clamping, non-Admin 403, 401

### Implementation for User Story 3

- [X] T027 [US3] In `OrdersController.cs` add `[HttpGet] GetMine(int page = 1, int pageSize = 10)` and `[HttpGet("all"), Authorize(Roles = "Admin")] GetAll(string? status, int page = 1, int pageSize = 10)`: clamp like `UsersController`, `OrderStatusExtensions.TryParseName` for the filter (400 on key `status` via `ValidationProblem`), no-tracking query ordered `CreatedAtUtc` desc then `Id` desc, `CountAsync`, `Skip/Take`, project to `OrderSummaryDto` (`UserName` from the `User` navigation, `ItemCount = o.Items.Count`), return `PagedResponseDto`
- [X] T028 [US3] In `OrdersController.cs` replace the T022 placeholder with the real `[HttpGet("{id:int}")] GetById(int id)`: `AsNoTracking`, `Include(Items)`, `Include(User)`; return `NotFound()` when missing or when `order.UserId != callerId && !User.IsInRole("Admin")`; otherwise `Ok(order.ToResponse())`

**Checkpoint**: US1 to US3 tests pass.

---

## Phase 6: User Story 4 - Cancel restores stock (Priority: P1)

**Goal**: Owner or Admin cancels a Pending order; stock restored exactly once.

**Independent Test**: Checkout 2 of product 1 (5 to 3), cancel, stock 5, `OrderCancelled` +2 movement.

### Tests for User Story 4 (write first)

- [X] T029 [US4] In `CategoryProduct.API.Tests/OrderLifecycleTests.cs` add cancel tests: owner cancel 200 with status `Cancelled` and refreshed `updatedAtUtc`; stock restored for a multi-line order; exactly one `OrderCancelled` movement per line (positive change, correct `quantityAfter`, user = caller, note "Order #{id} cancelled"); Admin may cancel another user's Pending order; another non-Admin gets 404 and nothing changes; unknown id 404; cancelling a `Confirmed` or `Cancelled` order 409 with no stock change; 401; the restore adds to the current stock when it changed after checkout; sum of movements still equals quantity
- [X] T030 [US4] In `OrderLifecycleTests.cs` add edge and concurrency tests: an order whose product was deleted (checkout, Admin deletes the product, cancel) returns 200, status `Cancelled`, and no movement is written for that line while other lines are still restored; with `CartRacingFactory` a raced stock change during the cancel save (armed once) still ends with the correct final stock; armed on every attempt gives 409 after 3 attempts with the order still `Pending` and no restore; two parallel cancels give one 200 and one 409 and stock restored once; a raced status change to `Confirmed` during a cancel makes the cancel return 409 and stock unchanged

### Implementation for User Story 4

- [X] T031 [US4] In `OrdersController.cs` add private `CancelCoreAsync(int orderId, int callerId, bool isAdmin)` per plan "Cancel" (3-attempt loop, `ChangeTracker.Clear()`, order with `Include(Items)` tracked, 404 for missing or foreign-for-non-Admin, 409 when not `Pending`, explicit transaction only when `IsRelational()`, per item `FindAsync` product and `StageMovement(product, +quantity, OrderCancelled, $"Order #{id} cancelled", callerId)`, skip and `LogWarning` for null product or null `ProductId`, set `Status`, `UpdatedAtUtc`, new `ConcurrencyStamp`, one `SaveChangesAsync`, commit, retry on `DbUpdateConcurrencyException`, 409 after 3)
- [X] T032 [US4] In `OrdersController.cs` add `[HttpPost("{id:int}/cancel")] Cancel(int id)` calling `CancelCoreAsync(id, callerId, User.IsInRole("Admin"))` and mapping its result to 200 (`order.ToResponse()`), 404 or 409 (`ProblemDetails`, `Title` "Conflict", `Detail` such as "Only a Pending order can be cancelled.")

**Checkpoint**: US1 to US4 tests pass.

---

## Phase 7: User Story 5 - Admin status changes (Priority: P2)

**Goal**: Admin confirms or cancels; invalid transitions refused.

**Independent Test**: Admin confirms a Pending order (200); repeating returns 409.

### Tests for User Story 5 (write first)

- [X] T033 [US5] In `OrderLifecycleTests.cs` add status tests: Pending to `Confirmed` 200 with refreshed `updatedAtUtc` and stock untouched; Pending to `Cancelled` restores stock with `OrderCancelled` movements exactly like cancel; `Confirmed` to anything 409 (including `Confirmed`, `Cancelled`, `Pending`); `Cancelled` to anything 409; `Pending` to `Pending` 409; status `""`, missing, `"abc"` and `"1"` give 400 on `Status`; lower-case `confirmed` accepted; the order's owner (non-Admin) 403; no token 401; unknown order 404; a confirmed order can no longer be cancelled by its owner (409)
- [X] T034 [US5] In `OrderLifecycleTests.cs` add race tests with `CartRacingFactory`: a raced status change to `Cancelled` during an Admin `Confirmed` save leads to retry and 409 (not a confirmed-after-cancelled order); a confirm and a cancel sent in parallel produce exactly one 200 and one 409, and stock is restored only if the cancel won

### Implementation for User Story 5

- [X] T035 [US5] In `OrdersController.cs` add `[HttpPut("{id:int}/status"), Authorize(Roles = "Admin")] SetStatus(int id, UpdateOrderStatusRequestDto request)`: parse with `OrderStatusExtensions.TryParseName` (400 on key `Status`); `Cancelled` calls `CancelCoreAsync(id, adminId, true)`; `Confirmed` runs a 3-attempt loop (clear tracker, load order, 404, not `Pending` gives 409, set `Status`, `UpdatedAtUtc`, new `ConcurrencyStamp`, save, retry on `DbUpdateConcurrencyException`, 409 after 3); target `Pending` gives 409 ("Invalid status transition."); return the order

**Checkpoint**: all API tests pass: US1 to US5. API contract is final.

---

## Phase 8: MVC foundation

**Note**: Phases 8 to 10 edit only `CategoryProduct.MVC` and can run in parallel with phases 3 to 7 once `contracts/api-endpoints.md` is fixed. No MVC project reference to the API (Constitution V).

- [X] T036 [P] Create `CategoryProduct.MVC/Models/CartModels.cs` (`CartModel(IReadOnlyList<CartLine> Items, int ItemCount, decimal Total)`, `CartLine(int ProductId, string ProductName, decimal UnitPrice, int Quantity, decimal LineTotal, int AvailableQuantity)`) and `CategoryProduct.MVC/Models/OrderModels.cs` (`OrderModel`, `OrderLine(int Id, int? ProductId, string ProductName, decimal UnitPrice, int Quantity, decimal LineTotal)`, `OrderSummaryItem`); in `CategoryProduct.MVC/Models/ApiModels.cs` add `ApiResult<T> : ApiResult` (`T? Value`) and request records `AddCartItemRequest(int ProductId, int Quantity)`, `UpdateCartItemRequest(int Quantity)`, `UpdateOrderStatusRequest(string Status)`
- [X] T037 In `CategoryProduct.MVC/Services/CategoryProductApiClient.cs` add a private generic `SendAsync<T>(HttpMethod, string url, object? body = null)` mirroring the existing `SendAsync` (same 404/403/429/401/409/400 mapping, 409 `Detail` into `Conflict`) that reads the 2xx body into `ApiResult<T>.Value`, and a private authenticated GET helper (`ApplyBearerToken`) returning `ApiResult<T>` (404 -> `NotFound`, 401 -> `Unauthorized`); then the public methods `GetCartAsync`, `AddCartItemAsync`, `UpdateCartItemAsync`, `RemoveCartItemAsync`, `ClearCartAsync` (204 has no body: use the non-generic `SendAsync`), `CheckoutAsync`, `GetMyOrdersAsync(page, pageSize)`, `GetOrderAsync(id)`, `GetAllOrdersAsync(status, page, pageSize)`, `CancelOrderAsync(id)`, `SetOrderStatusAsync(id, status)` per `contracts/mvc-routes.md`; the existing methods stay unchanged
- [X] T038 [P] In `CategoryProduct.MVC/Views/Shared/_Layout.cshtml` add, inside the nav list, `@if (User.Identity?.IsAuthenticated == true)` items "Cart" (`asp-controller="Cart" asp-action="Index"`) and "My Orders" (`asp-controller="Orders" asp-action="Index"`), and "Manage Orders" (`asp-controller="Orders" asp-action="Manage"`) inside the existing `@if (User.IsInRole("Admin"))` block

---

## Phase 9: User Story 6 - Cart and orders pages (Priority: P2)

**Goal**: Signed-in users add to cart, check out, view and cancel their orders. Verified manually via `quickstart.md` (MVC walkthrough).

- [X] T039 [US6] Create `CategoryProduct.MVC/Controllers/CartController.cs` (`[Authorize]`, injects `CategoryProductApiClient`): `Index` (GET `/Cart`), `Add(int productId, int quantity = 1, string? returnUrl)`, `Update(int productId, int quantity)`, `Remove(int productId)`, `Clear()`, `Checkout()` as POSTs with `[ValidateAntiForgeryToken]`; `TempData["Success"|"Error"]` using the API `Conflict` message or flattened `Errors` or "Not found"/"Could not ..." fallbacks like `UsersController.ChangeRole`; `returnUrl` honored only if `Url.IsLocalUrl`; API 401 -> private `SessionExpiredAsync()` copy (sign out, redirect to login with `returnUrl`); `Checkout` success redirects to `Orders/Details/{id}` with "Order placed."
- [X] T040 [P] [US6] Create `CategoryProduct.MVC/Views/Cart/Index.cshtml`: TempData alerts, table of lines (name, unit price, quantity form with Update, line total, stock warning badge when `AvailableQuantity < Quantity`, Remove form), total, "Clear cart" and "Checkout" forms (anti-forgery), empty-cart message without the Checkout button
- [X] T041 [US6] Create `CategoryProduct.MVC/Controllers/OrdersController.cs` (`[Authorize]`): `Index(int page = 1)` (GET `/Orders`), `[HttpGet("Orders/{id:int}")] Details(int id)` (404 -> `NotFound()`), `[HttpPost("Orders/{id:int}/Cancel"), ValidateAntiForgeryToken] Cancel(int id, string? returnTo)` with TempData messages and `SessionExpiredAsync` like T039
- [X] T042 [P] [US6] Create `CategoryProduct.MVC/Views/Orders/Index.cshtml` (alerts, table id/date in local time/status badge/item count/total/Details link, Previous/Next like `Views/Users/Index.cshtml`) and `CategoryProduct.MVC/Views/Orders/Details.cshtml` (header with id, date, status badge, items table with "(product removed)" marker when `ProductId` is null, total, Cancel form shown only when status is `Pending`, Confirm form shown only for Admin on Pending, alerts)
- [X] T043 [P] [US6] In `CategoryProduct.MVC/Views/Products/Index.cshtml` and `CategoryProduct.MVC/Views/Products/Details.cshtml` add, for authenticated users only, an "Add to cart" form (hidden `productId`, number `quantity` 1..100 default 1, hidden `returnUrl` = current URL, anti-forgery) posting to `Cart/Add`; show TempData success/error alerts on the product list if not yet shown there

**Checkpoint**: manual MVC check passes for the customer flow (quickstart steps 1 to 4 and 6 of the MVC walkthrough).

---

## Phase 10: User Story 7 - Admin manage orders page (Priority: P2)

- [X] T044 [US7] In `CategoryProduct.MVC/Controllers/OrdersController.cs` add `[HttpGet("Orders/Manage"), Authorize(Roles = "Admin")] Manage(int page = 1, string? status = null)` (invalid status values treated as "All") and `[HttpPost("Orders/{id:int}/Status"), Authorize(Roles = "Admin"), ValidateAntiForgeryToken] SetStatus(int id, string status, string? returnTo, int page = 1, string? filter = null)` redirecting back to `Manage` (preserving `page` and `filter`) or to `Details`; TempData messages for 409/400/404
- [X] T045 [P] [US7] Create `CategoryProduct.MVC/Views/Orders/Manage.cshtml`: status filter form (All, Pending, Confirmed, Cancelled), table (id, user name, date, status badge, total, Details link, Confirm and Cancel forms only on Pending rows), alerts, Previous/Next preserving the filter

**Checkpoint**: manual MVC check passes for the Admin flow (quickstart MVC walkthrough step 5 and 6).

---

## Phase 11: Polish

- [X] T046 Run `dotnet build CategoryProduct.slnx` and `dotnet test CategoryProduct.slnx`; confirm every old and new test passes; confirm `dotnet ef migrations has-pending-model-changes --project CategoryProduct.API` reports none
- [X] T047 Search `CategoryProduct.API.Tests` for tests that assume exact product counts or seeded quantities that new tests could disturb (new tests create their own products and users, so none is expected); fix any that appear
- [X] T048 Verify `CategoryProduct.API/CategoryProduct.API.http` or Swagger shows the new endpoints and, if the `.http` file lists endpoints, add cart and order examples
- [ ] T049 Walk through `specs/019-cart-orders/quickstart.md` (API, MVC and SQL Server steps) and fix any discrepancy; record SQL Server results (cascade, set-null, unique index, atomicity) for the Known Gaps

---

## Dependencies & Execution Order

- Phase 1 first. Phase 2: T003 before T004; T005 and T006 parallel; T007 depends on T005, T006; T008 on T007; T009, T010, T011, T012 can run in parallel (T010 needs T006 and T009; T012 needs T011).
- US1 (T013 to T018) needs phase 2; tests T013 to T015 are written before T016 to T018; T017 and T018 edit `CartController.cs` after T016.
- US2 (T019 to T023) needs US1 (the cart) and T004; T023 depends on T022; tests T019 to T021 first.
- US3 (T024 to T028): T028 replaces T022's placeholder; T026's Cancelled-filter test depends on T032 (US4).
- US4 (T029 to T032): T031 depends on T004, T012; T032 on T031.
- US5 (T033 to T035): T035 depends on T031.
- Tasks that edit `OrdersController.cs` (T022, T023, T027, T028, T031, T032, T035) are sequential, never `[P]`.
- MVC phases 8 to 10 are independent of API tasks except for the final manual verification (needs the API from phase 7): T036 and T038 `[P]`; T037 depends on T036; T039 depends on T037; T041 depends on T037; T044 depends on T041; views are `[P]` once their controller actions exist.
- API agent: T001 to T035, T046 to T048. MVC agent: T036 to T045. Both: T049.
- Within each story, tests are written and seen failing before the implementation tasks.
