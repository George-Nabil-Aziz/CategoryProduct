# Research: Inventory

## Verified facts about the current code

- `ProductController` (`CategoryProduct.API/Controllers/ProductController.cs`) uses `ApplicationDbContext` directly. `Post` sets `Quantity = request.Quantity`; `Put` sets `oldProduct.Quantity = request.Quantity`. Routes: `[HttpGet("{id}")]`, `[HttpPut("{id}")]`, `[HttpDelete("{id}")]`, all untyped. The list action does `pageSize = Math.Clamp(pageSize, 1, 50)`.
- `ProductRequestDto` has `[Range(0, int.MaxValue)] int Quantity`. `ProductResponseDto(int Id, string Name, decimal Price, int Quantity, int CategoryId)` today; 017 adds `string? ImageUrl = null`. `PagedResponseDto<T>(Items, Page, PageSize, TotalCount, TotalPages)`.
- `CategoryController.GetAllProducts` (`GET api/category/{catId}/products`) also builds `ProductResponseDto` (017 switches it to `ToResponse(Url)`).
- `Product` has no concurrency token. `User` has `Id`, `UserName`, `PasswordHash`, `Role`. `RefreshToken` has `Guid ConcurrencyStamp` configured with `.IsConcurrencyToken()` (015).
- `TokenService` writes `ClaimTypes.NameIdentifier` (user id), `ClaimTypes.Name`, `ClaimTypes.Role`. Nothing in the API reads the id claim yet.
- 409 pattern: `UsersController` returns `Conflict(new ProblemDetails { Status = 409, Title = "Conflict", Detail = "..." })`; MVC `CategoryProductApiClient.SendAsync` reads `Detail` into `ApiResult.Conflict` and 400 `errors` into `ApiResult.Errors`.
- Tests: `ApiFactory` swaps in `UseInMemoryDatabase`, seeds an Admin (`tester`), a User (`plainuser`) and three products with Quantity 1. `AuthHelper.LoggedInAsync` (in `UsersTests.cs`) logs in a client. `RefreshTokenTests.cs` has `RacingFactory`/`RaceInterceptor : SaveChangesInterceptor` (a second context bumps the stamp during `SavingChangesAsync`). No test performs a product PUT.
- `DatabaseMigrator` (016) skips non-relational providers via `Database.IsRelational()`.
- MVC: `ProductsController.Edit` binds `Id,Name,Price,Quantity,CategoryId`; `Views/Products/Edit.cshtml` has an editable Quantity input; `_Layout.cshtml` shows the Users link inside `@if (User.IsInRole("Admin"))`; `UsersController` is `[Authorize(Roles = "Admin")]` and uses `TempData["Success"/"Error"]`; `GetUsersAsync` uses `ApplyBearerToken` for authenticated GETs while `GetOrNullAsync` does not attach a token. MVC route is `{controller=Categories}/{action=Index}/{id?}`.

## Decision 1: Append-only movements, quantity stays on Product

**Decision**: keep `Product.Quantity` as the value read by every existing consumer; each change also writes a movement with `QuantityAfter`.
**Rationale**: no change to existing queries; `QuantityAfter` makes audit and display trivial.
**Alternatives**: derive quantity as the sum of movements (rejected: breaks existing filtering and responses, needs backfill).

## Decision 2: One shared `InventoryService.ApplyMovementAsync`

**Decision**: a scoped service in `Services/InventoryService.cs` applies a movement to an existing product (load, check result is >= 0 and no overflow, set quantity and a new `ConcurrencyStamp`, add the movement, `SaveChanges`, retry on `DbUpdateConcurrencyException`). `ProductController.Post` does not use it: the product does not exist yet, so it adds the product and the `Initial` movement in one save.
**Rationale**: the stock endpoint is consumer one; feature 019 checkout/cancel is a concrete second consumer that must share the concurrency and non-negative rules (Constitution III).
**Alternatives**: logic inside the controller (rejected: 019 would copy the retry loop).

## Decision 3: Guid `ConcurrencyStamp`, retry 3 times, then 409

**Decision**: as in 015: Guid token, `IsConcurrencyToken()`, regenerated on every stock change. The service catches `DbUpdateConcurrencyException`, calls `ChangeTracker.Clear()`, reloads and retries up to 3 attempts; after that it returns `Conflict`. `PUT` and `DELETE` catch the exception and return 409.
**Rationale**: concurrent restock and sale must not lose updates; works on InMemory so it is testable with the 015 interceptor approach.
**Alternatives**: `rowversion` (rejected: InMemory), serializable transaction (rejected: heavier, deadlock-prone), atomic `ExecuteUpdate` (rejected: InMemory unsupported and no atomic movement insert).

## Decision 4: PUT rejects a different quantity, accepts an equal one

**Decision**: compare `request.Quantity` with the stored value; different -> `ModelState.AddModelError(nameof(request.Quantity), "Use stock movements to change quantity.")` and `ValidationProblem`.
**Rationale**: the MVC Edit form and any client that PUTs a whole product keep working; the error key `Quantity` shows next to the field in MVC through the existing `AddErrors`.
**Alternatives**: silently ignoring a different quantity (rejected: hides data loss); a new PUT DTO without quantity (rejected: breaking contract change).

## Decision 5: `IsLowStock` computed on read

**Decision**: `ToResponse(this Product, IUrlHelper, int lowStockThreshold)` sets `IsLowStock = Quantity < threshold`; the threshold comes from `IOptions<InventoryOptions>` injected into `ProductController` and `CategoryController`; no stored column. `InventoryOptions.Effective` returns 5 for negative values.
**Rationale**: config changes apply immediately and no backfill is needed.
**Alternatives**: stored flag (rejected: stale when config changes); separate endpoint only (rejected: the alert should be visible to product consumers).

## Decision 6: Route `low-stock`

**Decision**: `[HttpGet("low-stock")]` beside `[HttpGet("{id}")]`; ASP.NET Core ranks literal segments above parameters, so no constraints are added to existing actions. A test asserts the route works.
**Alternatives**: `{id:int}` on `Get/Put/Delete` (rejected: unneeded change, and `GET api/product/abc` would change from 400 to 404).

## Decision 7: `UserId` FK delete behavior

**Decision**: `OnDelete(DeleteBehavior.SetNull)`.
**Rationale**: SQL Server rejects multiple cascade paths only when one table is reachable by two cascading routes from the same root; `Users` and `Products` are unrelated parents, so one cascade (Product) plus one SetNull (User) is allowed. SetNull keeps history if a user is ever removed.

## Decision 8: Reason parsing

**Decision**: store the enum with `HasConversion<string>().HasMaxLength(30)`; the request takes `string Reason`; the controller matches it against `Enum.GetNames` case-insensitively and checks it is in the admin-allowed set.
**Rationale**: `Enum.TryParse` accepts numeric strings such as `"3"`; name matching does not.

## Decision 9: MVC data access

**Decision**: new `InventoryController` (`[Authorize(Roles = "Admin")]`); `Index` uses conventional routing, while history and add-movement use attribute routes `Inventory/Product/{id:int}` with action names `History` and `AddMovement` (avoids a member named `Product` clashing with the `Product` model type). The client gets `GetStockMovementsAsync` (with `ApplyBearerToken`), `GetLowStockCountAsync` (low-stock endpoint with `pageSize=1`, reads `TotalCount`) and `AddStockMovementAsync` (existing `SendAsync`). The list reuses `GetProductsAsync`.
**Rationale**: no new patterns; the banner needs one tiny request.
