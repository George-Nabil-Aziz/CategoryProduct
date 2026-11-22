# Implementation Plan: Cart and Orders

**Branch**: `019-cart-orders` | **Date**: 2026-11-22 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/019-cart-orders/`

**Dependency**: implemented AFTER 018-inventory (which is implemented after 017). Planned by 018 and not yet in the code: `Services/InventoryService.cs` (`ApplyMovementAsync`, `MovementStatus`, `MovementResult`), `Models/StockMovement.cs`, `Models/StockMovementReason.cs` (`Sale`, `OrderCancelled`), `Product.ConcurrencyStamp` (Guid token regenerated on every stock change), `Inventory:LowStockThreshold`. Already in the code: `PagedResponseDto<T>`, `ProductMappingExtensions.cs` (017), `User`, `ApplicationDbContext`, the `IsRelational()` transaction pattern in `UsersController`, `ApiFactory` and `AuthHelper`. One required change to 018's `InventoryService`: a non-saving `StageMovement` (task T003).

> **Analyze resolution (2026-11-22)**: 018 is implemented and already provides `InventoryService.StageMovementAsync(int productId, int quantityChange, StockMovementReason reason, string? note, int? userId, CancellationToken ct = default)` returning `MovementResult(MovementStatus Status, StockMovement? Movement)` (statuses `Ok`, `NotFound`, `InsufficientStock`; no save), plus `LogIfLowStock(StockMovement)` to call after a successful save and `int Threshold`. 019 uses these as-is; wherever this document says `StageMovement(Product ...)` / `StagedMovement`, read `StageMovementAsync(productId, ...)` / `MovementResult`. It loads the product by id, which returns the already-tracked instance from the cart `Include(Product)`, so staging several lines and saving once still works. No change to `InventoryService.cs` is needed.

## Summary

Add a persisted per-user cart (`api/cart`) and orders (`api/orders`). Checkout reads the cart, rejects an empty cart (400) or short stock (409 listing products), and otherwise, in one transaction and one `SaveChanges`, stages a `Sale` movement per line through `InventoryService`, creates a `Pending` `Order` with price snapshots, and deletes the cart lines; any `DbUpdateConcurrencyException` re-reads and retries up to 3 attempts. Orders move `Pending` to `Confirmed` (Admin) or `Cancelled` (owner or Admin); cancel stages `OrderCancelled` movements and is also retried. Users read their own orders (404 for foreign ids), Admin reads all. MVC gets cart, orders and admin management pages. One generated migration `AddCartAndOrders`.

## Technical Context

**Language/Version**: C# / .NET 10 (existing projects).

**Primary Dependencies**: ASP.NET Core, EF Core 10 SQL Server (existing). No new NuGet package.

**Storage**: SQL Server; new tables `CartItems`, `Orders`, `OrderItems`, via one generated migration `AddCartAndOrders`.

**Testing**: xUnit integration tests in `CategoryProduct.API.Tests` (`ApiFactory`, EF InMemory), `SaveChangesInterceptor` race factories as in `RefreshTokenTests.cs`, and 018's planned `RacingFactory` idea. MVC has no test project; MVC is verified through `quickstart.md`.

**Target Platform**: Windows dev and Linux containers (016).

**Constraints**: additive API only (no change to existing responses); InMemory cannot prove transaction atomicity, unique index, cascade or set-null (Known Gaps); no hand-edited migration; no `database update`; `Program.cs` edited additively.

**Scale/Scope**: API: 1 enum, 3 models, 2 controllers, about 9 DTOs, DbContext config, 1 migration, 1 small `InventoryService` addition; MVC: 2 controllers, about 5 views, nav links, product list/details buttons, about 10 client methods, view models; tests: 4 new API test files plus 1 helper file.

## Constitution Check

| Principle | Check | Status |
|---|---|---|
| I. Consistent REST API Design | `api/[controller]` routes `api/cart` and `api/orders` with plural noun for the collection, standard verbs and codes (200/201/204/400/401/403/404/409). Every list that can grow is paged with `page`/`pageSize` and returns `PagedResponseDto`: `GET api/orders`, `GET api/orders/all`. The cart list is NOT paged: it is bounded by construction (one line per product, 100 per line, each user has one cart) and the response is a single cart object with `total`, not a collection resource; recorded under Complexity Tracking. 409 uses the `ProblemDetails` pattern of `UsersController`. | PASS (one justified exception) |
| II. Schema Changes Only Through EF Core Migrations | `dotnet ef migrations add AddCartAndOrders --project CategoryProduct.API`, committed with the model changes, never hand-edited, no `database update`. MVC keeps its own view models; no shared DbContext or model types. | PASS |
| III. Simplicity & YAGNI | Controllers use `ApplicationDbContext` directly; checkout and cancel are private methods of `OrdersController`; no `OrderService`, no repository, no unit-of-work class. The one shared abstraction is 018's `InventoryService`, justified there by this feature as its second consumer; this feature only adds a non-saving `StageMovement` to it (needed because several movements must share one save). No cart abstraction, no stored totals other than the order's. | PASS (see Complexity Tracking) |
| IV. Secrets & Credential Hygiene | No secrets, no new configuration. | PASS |
| V. API/MVC Separation of Concerns | MVC calls the API only over HTTP through `CategoryProductApiClient` and has its own models (`CartModel`, `OrderModel`, ...); no API project reference. | PASS |

Re-check after design: unchanged, all PASS.

## Project Structure

### Documentation

```text
specs/019-cart-orders/
  spec.md, plan.md, research.md, data-model.md, quickstart.md, tasks.md
  contracts/api-endpoints.md, contracts/mvc-routes.md
  checklists/requirements.md
```

### Source code changes

```text
CategoryProduct.API/
  Models/OrderStatus.cs                          (new: enum + TryParseName helper)
  Models/CartItem.cs                             (new)
  Models/Order.cs                                (new)
  Models/OrderItem.cs                            (new)
  Data/ApplicationDbContext.cs                   (modify: 3 DbSets + configuration)
  Migrations/<timestamp>_AddCartAndOrders*.cs    (generated)
  Dtos/CartItemRequestDto.cs                     (new: AddCartItemRequestDto, UpdateCartItemRequestDto)
  Dtos/CartResponseDto.cs                        (new: CartResponseDto, CartItemResponseDto)
  Dtos/OrderResponseDto.cs                       (new: OrderResponseDto, OrderItemResponseDto, OrderSummaryDto)
  Dtos/UpdateOrderStatusRequestDto.cs            (new)
  Services/InventoryService.cs                   (modify, planned by 018: add StageMovement)
  Controllers/CartController.cs                  (new)
  Controllers/OrdersController.cs                (new: checkout, reads, cancel, status)
  Infrastructure/OrderMappingExtensions.cs       (new: ToResponse for Order and Cart)
CategoryProduct.API.Tests/
  CartOrderTestHelpers.cs                        (new)
  CartTests.cs, OrderCheckoutTests.cs, OrderReadTests.cs, OrderLifecycleTests.cs   (new)
CategoryProduct.MVC/
  Models/ApiModels.cs                            (modify: ApiResult<T>, cart/order request records)
  Models/CartModels.cs, Models/OrderModels.cs    (new: view models)
  Services/CategoryProductApiClient.cs           (modify: cart and order methods)
  Controllers/CartController.cs, Controllers/OrdersController.cs   (new)
  Views/Cart/Index.cshtml                        (new)
  Views/Orders/Index.cshtml, Details.cshtml, Manage.cshtml   (new)
  Views/Products/Index.cshtml, Details.cshtml    (modify: Add to cart form)
  Views/Shared/_Layout.cshtml                    (modify: nav links)
```

`Program.cs` needs no change: controllers are discovered by `AddControllers()`, and `InventoryService` is registered by 018.

**Structure Decision**: existing two-project layout plus the existing test project; no new projects.

## Design notes

- **User id**: private helper `int? CurrentUserId()` reading `User.FindFirstValue(ClaimTypes.NameIdentifier)` with `int.TryParse` (the claim is written by `TokenService.cs` line 24, verified). `[Authorize]` guarantees a token; an unparseable id returns 401 (cannot happen with `TokenService` tokens). Duplicate the 5-line helper in both controllers rather than add a base class.
- **Route ordering**: `OrdersController` uses `[HttpGet("all")]` and `[HttpGet("{id:int}")]` (int constraint; the controller is new so no existing behavior changes). `GET api/orders/abc` is then a 404 from routing.
- **Cart read** (`BuildCartAsync(userId)`): `CartItems.AsNoTracking().Where(UserId).OrderBy(AddedAtUtc).ThenBy(Id)` joined to `Products` through the `Product` navigation, projected to `CartItemResponseDto(ProductId, Product.Name, Product.Price, Quantity, Price * Quantity, Product.Quantity)`; `total` summed in memory.
- **Cart add**: validate DTO (auto 400), `FindAsync` product (404), load existing line by (`UserId`, `ProductId`); new line, or `Quantity += request.Quantity` and if the sum is above 100 add a model error on `Quantity` and return `ValidationProblem` (no change); `SaveChanges`; on `DbUpdateException` (unique index race on SQL Server) `ChangeTracker.Clear()` and retry the merge once; then return the cart.
- **Cart update/remove/clear**: line lookup by (`UserId`, `ProductId`), 404 if missing; clear uses `RemoveRange` of the user's lines (`ExecuteDelete` is not used: unsupported on InMemory).
- **Checkout** (`POST api/orders`): loop `attempt` 1..3: `ChangeTracker.Clear()`; load the caller's `CartItems` with `Include(Product)` tracked, ordered by `Id`; empty -> `ModelState` error key `Cart`, `ValidationProblem` (400); products missing cannot occur (inner join / cascade); compute `shortages` = lines with `Product.Quantity < Quantity`; any -> 409 `ProblemDetails` (`Title` "Insufficient stock", `Detail` "Not enough stock for: Widget A (requested 3, available 1); ...", `Extensions["items"]` list of `{ productId, productName, requested, available }`) and stop (nothing staged). Otherwise begin an explicit transaction only when `_context.Database.IsRelational()` (`BeginTransactionAsync`), then for each line `_inventory.StageMovement(product, -line.Quantity, Sale, "Checkout", userId)` (changes the tracked product and its stamp and adds the movement), build `Order { UserId, Status = Pending, CreatedAtUtc = UpdatedAtUtc = now, Items = lines snapshots, Total = sum }`, `_context.CartItems.RemoveRange(lines)`, ONE `SaveChangesAsync`, commit. Catch `DbUpdateConcurrencyException`: roll back (disposing the transaction), clear the tracker and continue the loop. After 3 failures return 409 ("The order could not be completed because stock changed. Please try again."). After commit log information with order id, user id and total, and low-stock warnings via `InventoryService` if 018's helper is available. Return `CreatedAtAction(nameof(GetById), new { id }, dto)`.
- **Why double checkout is safe**: both requests read the same cart rows; the first deletes them, so the second's `DELETE` of the same rows affects 0 rows (EF raises `DbUpdateConcurrencyException` for a delete of a vanished row) and the stock UPDATEs conflict on `ConcurrencyStamp`; the retry re-reads an empty cart and returns 400.
- **Cancel** (`CancelCoreAsync(orderId, callerId, isAdmin)`): loop 1..3: `ChangeTracker.Clear()`; load the order with `Include(Items)` tracked (`FirstOrDefaultAsync`); not found, or not owner and not Admin -> `NotFound`; `Status != Pending` -> `Conflict`; start the explicit transaction when relational; for each item with `ProductId != null` load the product with `FindAsync` (null -> log warning "skipped, product deleted" and continue) and `StageMovement(product, +item.Quantity, OrderCancelled, "Order #id cancelled", callerId)`; set `Status = Cancelled`, `UpdatedAtUtc`, new `ConcurrencyStamp`; one `SaveChanges`; commit. Concurrency exception -> retry; after 3 -> 409. `StageMovement` overflow (`InsufficientStock` status on a positive change) -> 409.
- **Why double cancel restores once**: the order's `ConcurrencyStamp` is part of the UPDATE; the loser gets a concurrency exception, re-reads `Cancelled` and returns 409 without staging anything.
- **Status** (`PUT api/orders/{id}/status`, Admin): parse by `OrderStatus.TryParseName` (400 on key `Status`); `Cancelled` -> `CancelCoreAsync(id, adminId, isAdmin: true)`; `Confirmed` -> same retry loop: load, 404, not Pending -> 409, set status and stamp, save; any other target (`Pending`) -> 409 "Invalid transition".
- **Reads**: `GetMine` (`Orders.AsNoTracking().Where(UserId)`) and `GetAll` (Admin, optional `Status` filter) order by `CreatedAtUtc` desc then `Id` desc, `CountAsync`, `Skip/Take`, project to `OrderSummaryDto(Id, UserId, UserName, Status, Total, ItemCount, CreatedAtUtc, UpdatedAtUtc)` (`UserName` via the `User` navigation, `ItemCount` = `Items.Count`). `GetById` loads with items (`AsNoTracking`, `Include(Items)`, `Include(User)`), returns 404 when missing or when the caller is neither owner nor Admin.
- **Admin check**: `User.IsInRole("Admin")` (the role claim is `ClaimTypes.Role`, written by `TokenService`, verified).
- **Mapping**: `OrderMappingExtensions.ToResponse(this Order)` returns `OrderResponseDto(Id, UserId, UserName, Status (string), Total, CreatedAtUtc, UpdatedAtUtc, Items)`; status serialized as its name string.
- **InventoryService.StageMovement**: signature `StagedMovement StageMovement(Product product, int quantityChange, StockMovementReason reason, string? note, int? userId)`; computes `long` result, returns `InsufficientStock` for below zero or above `int.MaxValue` without touching anything, otherwise sets `product.Quantity`, `product.ConcurrencyStamp = Guid.NewGuid()`, adds a `StockMovement` (`Product = product`, `QuantityAfter`, `CreatedAtUtc = DateTime.UtcNow`) to the context and returns `Ok` with the movement. `ApplyMovementAsync` is refactored to call it so rules stay in one place. It does NOT save and does NOT log; callers log low-stock after a successful commit (the helper `LogIfLowStock(Product, change)` is public on the service).
- **EF concurrency note**: `Product` UPDATE uses `WHERE Id = @id AND ConcurrencyStamp = @old` (018); two lines of one order never touch the same product because the cart has one line per product.
- **MVC**: see `contracts/mvc-routes.md`. `CartController` and `OrdersController` use `CategoryProductApiClient` methods with the bearer token (`ApplyBearerToken`); the existing `GetOrNullAsync` sends no token and so is not used for these calls. `ApiResult<T>` adds a `Value` for 201/200 bodies. `TempData["Success"|"Error"]` and redirects as in `UsersController`; 401 -> sign out and redirect to login like `ProductsController.SessionExpiredAsync` (private helper duplicated, as 018 does). `returnUrl` for "Add to cart" is accepted only when `Url.IsLocalUrl`.

## Complexity Tracking

| Deviation | Why needed | Simpler alternative rejected because |
|---|---|---|
| Cart endpoint not paged (Constitution I) | One cart per user, at most one line per product and 100 per line; the response is one cart object with a total, which a client needs whole | Paging a cart would break the total and the checkout view and adds nothing for a bounded set |
| `InventoryService.StageMovement` (touches 018's code, Constitution III) | Checkout and cancel must change several products and write several movements in one save; `ApplyMovementAsync` saves internally per call, which would commit lines separately and break all-or-nothing | Calling `ApplyMovementAsync` per line in one transaction works on SQL Server but gives wrong results on InMemory and leaves partial state without a transaction; duplicating the stock rules in the controller would copy the non-negative and overflow logic |
| Explicit transaction around one `SaveChanges` | Required by the feature statement; guarded by `Database.IsRelational()` so tests on InMemory still run | Relying only on the implicit `SaveChanges` transaction is correct today but silently breaks if a second save is added |
