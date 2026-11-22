# Research: Cart and Orders

> **Analyze resolution (2026-11-22)**: 018 is implemented and already provides `InventoryService.StageMovementAsync(int productId, int quantityChange, StockMovementReason reason, string? note, int? userId, CancellationToken ct = default)` returning `MovementResult(MovementStatus Status, StockMovement? Movement)` (statuses `Ok`, `NotFound`, `InsufficientStock`; no save), plus `LogIfLowStock(StockMovement)` to call after a successful save and `int Threshold`. 019 uses these as-is; wherever this document says `StageMovement(Product ...)` / `StagedMovement`, read `StageMovementAsync(productId, ...)` / `MovementResult`. It loads the product by id, which returns the already-tracked instance from the cart `Include(Product)`, so staging several lines and saving once still works. No change to `InventoryService.cs` is needed.

## Verified facts about the current code

- `ApplicationDbContext` (`CategoryProduct.API/Data/ApplicationDbContext.cs`) has `Categories`, `Products`, `Users`, `RefreshTokens`; `OnModelCreating` configures `RefreshToken` (unique `TokenHash`, `ConcurrencyStamp` token, `UserId` cascade), `Product.ImageFileName` (max 100, from 017), `User.UserName` unique, `User.Role` max 20. The model snapshot has `Product.Price` as `decimal(18,2)`, `Product.CategoryId` cascading from `Categories`, `RefreshToken.UserId` cascading from `Users`. `Product.Name` is `nvarchar(max)` in the model but limited to 2..100 by `ProductRequestDto`.
- `Product` model: `Id, Name, Price, Quantity, CategoryId, Category, ImageFileName`. No `ConcurrencyStamp` yet (planned by 018). `User`: `Id, UserName, PasswordHash, Role`.
- `ProductController` (`CategoryProduct.API/Controllers/ProductController.cs`): direct `ApplicationDbContext`; `Delete` does `FindAsync`, `Remove`, `SaveChangesAsync` (no concurrency handling until 018; 018 adds 409). 017 is already in the working tree (`IProductImageStorage`, `ProductMappingExtensions.ToResponse(Url)`).
- `UsersController`: `[Authorize(Roles = "Admin")]`, `Conflict(new ProblemDetails { Status = 409, Title = "Conflict", Detail = ... })`, paging with `Math.Max(page, 1)` and `Math.Clamp(pageSize, 1, 50)`, default `pageSize` 10, `PagedResponseDto`; the transaction pattern is `if (!_context.Database.IsRelational()) return await ApplyRoleAsync(...)` else `BeginTransactionAsync(IsolationLevel.Serializable)` (015). Only `GET` and `PUT {id}/role`: users are never deleted.
- `PagedResponseDto<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount, int TotalPages)`.
- `TokenService.cs` writes `ClaimTypes.NameIdentifier` (user id), `ClaimTypes.Name`, `ClaimTypes.Role`; nothing reads the id claim yet (018 will be the first reader).
- `Program.cs`: `AddControllers()`, lowercase URLs (`LowercaseUrls = true`), `AddProblemDetails`, `GlobalExceptionHandler` (unhandled exceptions become 500 problem details), `UseSqlServer` with no execution strategy, `AddAuthorization()` with no custom policies; role checks use `[Authorize(Roles = "Admin")]`.
- Tests: `ApiFactory` (EF InMemory with a shared `InMemoryDatabaseRoot`, seeds Admin `tester`, User `plainuser`, 3 categories, 3 products with Quantity 1: "Widget A" 30, "Widget B" 10, "Gadget" 20); `AuthHelper.LoggedInAsync(factory, user)` and `AuthHelper.Unique(prefix)` are in `UsersTests.cs`; `RefreshTokenTests.cs` has `RacingFactory`/`RaceInterceptor : SaveChangesInterceptor` (second context bumps `ConcurrencyStamp` during `SavingChangesAsync`, armed once through `Arm()`), configured with `services.ConfigureDbContext<ApplicationDbContext>(o => o.AddInterceptors(...))`.
- MVC (`CategoryProduct.MVC`): `CategoryProductApiClient` attaches the JWT with `ApplyBearerToken` (reads the `access_token` claim from the cookie principal); `SendAsync` maps 404/403/429/401/409 (reads `Detail`)/400 (reads `errors`) into `ApiResult` but discards success bodies; `GetOrNullAsync` and `GetProductsAsync` send no token. `ApiErrorHandler` converts 5xx and connection failures into `ApiUnavailableException`. `UsersController` is `[Authorize(Roles = "Admin")]`, posts with `[ValidateAntiForgeryToken]`, uses `TempData["Success"/"Error"]`. `ProductsController` has `SessionExpiredAsync` (sign out, redirect to login). Routing is conventional `{controller=Categories}/{action=Index}/{id?}`; attribute routes are used by 018 for Inventory. `_Layout.cshtml` shows admin-only links inside `@if (User.IsInRole("Admin"))` and logout when `User.Identity?.IsAuthenticated == true`. `Views/Products/Index.cshtml` and `Details.cshtml` render per-row actions; the MVC `Product` model has no `ImageUrl` or `IsLowStock` yet.
- 018 plan (not yet implemented): `InventoryService.ApplyMovementAsync(productId, change, reason, note, userId, ct)` loads, checks, sets quantity and a new stamp, adds the movement and calls `SaveChangesAsync` with a 3-attempt retry; `StockMovement` has no `OrderId`; `Sale` and `OrderCancelled` are rejected by the admin endpoint and reserved for 019.

## Decision 1: Cart in the database, one row per (user, product)

**Decision**: `CartItem` with unique (`UserId`, `ProductId`), quantity 1..100, no stock reservation.
**Rationale**: matches the requirement; the unique index gives merge semantics and guards double-click adds; no reservation avoids expiry jobs (YAGNI).
**Alternatives**: a `Cart` header table (rejected: no data beyond the owner); session cart (rejected: not persisted, not API-friendly).

## Decision 2: Cart responses show live prices, orders snapshot them

**Decision**: the cart reads current `Product.Price` and `Quantity`; `OrderItem` stores `ProductName` and `UnitPrice`.
**Rationale**: an order must not change when prices change; a cart should always show what checkout would charge.
**Alternatives**: price in the cart row (rejected: stale prices, extra sync logic).

## Decision 3: FK delete behaviors (SQL Server multiple cascade paths)

**Decision**: `CartItem.UserId` cascade, `CartItem.ProductId` cascade, `Order.UserId` Restrict, `OrderItem.OrderId` cascade, `OrderItem.ProductId` SetNull (nullable).
**Rationale**: SQL Server rejects a table reachable by two cascading paths (SetNull counts) from one root. Roots: `Users` reaches `CartItems` (cascade), `RefreshTokens` (cascade), `StockMovements` (set null, 018), not `Orders` (restrict) so `Orders` and `OrderItems` are not reached from `Users`; `Categories` reaches `Products`, which reaches `CartItems`, `OrderItems` (set null) and `StockMovements` (cascade) once each; `Orders` reaches `OrderItems` once. `OrderItems.ProductId` SetNull keeps products deletable (SC-006). `Order.UserId` Restrict keeps sales history; it blocks only user deletion, which has no endpoint. A migration generated against this model is the real proof; task T010 verifies it generates without error and the quickstart applies it to SQL Server.
**Alternatives**: `Order.UserId` cascade (rejected: deleting a user would erase sales records; also adds a second path to `OrderItems` only via Orders, harmless but unwanted); `OrderItem.ProductId` Restrict (rejected: products with history could never be deleted, contradicting existing `DELETE api/product/{id}`).

## Decision 4: Stock staging, one SaveChanges

**Decision**: add `InventoryService.StageMovement` (no save, no log) and call it per line; the controller does the single `SaveChanges` inside an explicit transaction when `IsRelational()`.
**Rationale**: all-or-nothing needs one commit point; the concurrency token on `Product` makes the single UPDATE batch fail as a whole.
**Alternatives**: calling `ApplyMovementAsync` per line inside a transaction (rejected: each call retries and clears the change tracker internally, `ChangeTracker.Clear()` inside the loop would discard the staged order and earlier lines, and InMemory has no transactions); `ExecuteUpdate` atomic decrements (rejected: unsupported on InMemory, no movement insert in the same statement).

## Decision 5: Retry the whole checkout, not single lines

**Decision**: on `DbUpdateConcurrencyException` clear the change tracker, re-read the cart and products and redo checks; 3 attempts, then 409.
**Rationale**: stock may have dropped below a line's quantity while retrying, so the shortage check must run again; it also makes the double-checkout race end with an empty-cart 400.
**Alternatives**: retry only the failing product (rejected: partially stale state).

## Decision 6: Cancel uses an order concurrency token

**Decision**: `Order.ConcurrencyStamp` (Guid, `IsConcurrencyToken()`), regenerated on every status change.
**Rationale**: two simultaneous cancels (or cancel vs confirm) must not both pass the `Pending` check; a Guid token also works on InMemory (same reason as 015 and 018).
**Alternatives**: `rowversion` (rejected: InMemory); serializable transaction (rejected: heavier, deadlock-prone).

## Decision 7: Ownership leaks nothing

**Decision**: a non-owner, non-Admin requesting `GET api/orders/{id}` or `POST api/orders/{id}/cancel` gets 404 (the same body as an unknown id).
**Rationale**: avoids confirming that an order id exists.
**Alternatives**: 403 (rejected: leaks existence).

## Decision 8: Status parsing

**Decision**: match `Enum.GetNames` case-insensitively (as 018 does for reasons); `Enum.TryParse` accepts numeric strings.
**Rationale**: consistent behavior and no surprises such as `"1"`.

## Decision 9: Order notes link movements loosely

**Decision**: `Sale` note "Checkout", `OrderCancelled` note "Order #{id} cancelled", no `OrderId` column on `StockMovement`.
**Rationale**: the order id does not exist before the single save; a second save would break atomicity, and a schema change to 018 is not needed by any requirement.
**Alternatives**: nullable `StockMovement.OrderId` FK with the `Order` navigation staged (EF would fix up the id in the same save): feasible and tidy, but expands 018's model for audit convenience only; listed as a possible follow-up.

## Decision 10: MVC patterns reused

**Decision**: `CartController` and `OrdersController` mirror `UsersController` (TempData messages, redirect after post, anti-forgery) and `ProductsController` (session-expired helper). The client needs an authenticated GET helper and a `SendAsync<T>` that returns the created or updated body (`ApiResult<T>`) because checkout must redirect to the new order id and the cart/order pages need typed data.
**Rationale**: no new patterns beyond one generic result type.
**Alternatives**: parse `Location` in `ApiResult` (rejected: still needs the body for the cart after mutations).
