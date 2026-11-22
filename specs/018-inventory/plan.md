# Implementation Plan: Inventory

**Branch**: `018-inventory` | **Date**: 2026-11-22 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/018-inventory/`

**Dependency**: implemented AFTER 017-product-images. This plan builds on 017's planned shapes: `ProductResponseDto(..., string? ImageUrl = null)`, `Infrastructure/ProductMappingExtensions.cs` with `ToResponse(this Product, IUrlHelper)`, the list action reading entities then mapping with `ToResponse(Url)`, `CategoryController.GetAllProducts` using `ToResponse(Url)`, `ProductController` already injecting `IProductImageStorage`, and `ApiFactory` already creating a temp image directory. If 017's final code differs, adapt the mechanical edits; the design does not change. 015 (Guid concurrency pattern) and 016 (Docker, `DatabaseMigrator`) are assumed implemented.

## Summary

Every stock change becomes an append-only `StockMovement` (change, quantity after, reason, note, user, time). Admins add movements through `POST api/product/{id}/stock-movements` and read history with a paged `GET`. `PUT api/product/{id}` can no longer change quantity. Products below `Inventory:LowStockThreshold` (default 5) are flagged with `IsLowStock` in every product response, listed at `GET api/product/low-stock` and logged as a warning when a movement drops them below. The shared change logic (non-negative check, Guid concurrency token, retry up to 3 times) lives in `InventoryService`, which feature 019 will reuse. MVC gets an Admin-only Inventory list with a low-stock banner and a per-product history and add-movement page.

## Technical Context

**Language/Version**: C# / .NET 10 (existing projects).

**Primary Dependencies**: ASP.NET Core, EF Core 10 SQL Server (existing). No new NuGet package.

**Storage**: SQL Server; one new table `StockMovements`, one new column `Products.ConcurrencyStamp`, via one generated migration `AddInventory`.

**Testing**: xUnit integration tests in `CategoryProduct.API.Tests` using `WebApplicationFactory` (`ApiFactory`, EF InMemory), a `SaveChangesInterceptor` race factory as in `RefreshTokenTests.cs`, and unit-level `InventoryService` tests. MVC has no test project; MVC is verified through `quickstart.md`.

**Target Platform**: Windows dev and Linux containers (016).

**Constraints**: additive API response change (one boolean); PUT quantity rule is the single deliberate behavior change; Guid token (InMemory-friendly); no hand-edited migration; no `database update`.

**Scale/Scope**: API: 1 model, 1 enum, 1 options class, 1 service, 3 new actions, 2 DTOs, 1 DTO change, changes to `Post`/`Put`/`Delete`, 1 migration; MVC: 1 controller, 2 views, 1 nav link, 3 client methods, 2 view models, small Edit view change; tests: 1 new API test file plus a small `ApiFactory` change.

## Constitution Check

| Principle | Check | Status |
|---|---|---|
| I. Consistent REST API Design | Sub-resource `api/product/{id}/stock-movements` under the existing `api/[controller]` route with standard verbs and codes (201/400/401/403/404/409; 200 for GETs). Both new list endpoints (`stock-movements`, `low-stock`) are paged with `page`/`pageSize` and return the existing `PagedResponseDto`. 409 uses the `ProblemDetails` pattern already used by `UsersController`. The POST returns 201 without `Location` (no single-movement GET), recorded in spec Assumptions. | PASS |
| II. Schema Changes Only Through EF Core Migrations | `dotnet ef migrations add AddInventory --project CategoryProduct.API`, committed with the model changes, never hand-edited, no `database update`. MVC models stay its own (`StockMovementItem`); no shared DbContext or model types. Existing products get `ConcurrencyStamp = Guid.Empty` and no history; documented, not backfilled by editing the migration. | PASS |
| III. Simplicity & YAGNI | Controllers keep using `ApplicationDbContext` directly. One new abstraction, `InventoryService.ApplyMovementAsync`: justified by a concrete second consumer (feature 019 checkout and cancel) and the cross-cutting concurrency rule (token, retry, non-negative). `Post` and the read endpoints query the context directly. One small options class for the threshold. No repository, no event bus, no stored low-stock flag. | PASS (justified, see Complexity Tracking) |
| IV. Secrets & Credential Hygiene | No secrets. `Inventory:LowStockThreshold` is non-secret and committed in `appsettings.json`. | PASS |
| V. API/MVC Separation of Concerns | MVC calls the API only over HTTP through `CategoryProductApiClient` and has its own view models; no API project/type reference. | PASS |

Re-check after design: unchanged, all PASS.

## Project Structure

### Documentation

```text
specs/018-inventory/
  spec.md, plan.md, research.md, data-model.md, quickstart.md, tasks.md
  contracts/api-endpoints.md, contracts/mvc-routes.md
  checklists/requirements.md
```

### Source code changes

```text
CategoryProduct.API/
  Models/StockMovementReason.cs                  (new: enum + AdminReasons helper)
  Models/StockMovement.cs                        (new)
  Models/Product.cs                              (modify: ConcurrencyStamp)
  Data/ApplicationDbContext.cs                   (modify: DbSet, StockMovement + Product stamp config)
  Migrations/<timestamp>_AddInventory*.cs        (generated)
  Dtos/StockMovementRequestDto.cs                (new)
  Dtos/StockMovementResponseDto.cs               (new)
  Dtos/ProductResponseDto.cs                     (modify: IsLowStock = false after ImageUrl)
  Services/InventoryOptions.cs                   (new)
  Services/InventoryService.cs                   (new)
  Infrastructure/ProductMappingExtensions.cs     (modify, from 017: ToResponse(url, threshold))
  Controllers/ProductController.cs               (modify: Post initial movement, Put rule, Delete/Put 409, 3 new actions)
  Controllers/CategoryController.cs              (modify: GetAllProducts passes threshold)
  Program.cs                                     (modify: register options + service only)
  appsettings.json                               (modify: Inventory section)
CategoryProduct.API.Tests/
  ApiFactory.cs                                  (modify: virtual LowStockThreshold setting)
  InventoryTests.cs                              (new)
CategoryProduct.MVC/
  Models/Product.cs                              (modify: IsLowStock)
  Models/ApiModels.cs                            (modify: StockMovementItem, StockMovementRequest)
  Services/CategoryProductApiClient.cs           (modify: 3 methods)
  Controllers/InventoryController.cs             (new)
  Views/Inventory/Index.cshtml, History.cshtml   (new)
  Views/Shared/_Layout.cshtml                    (modify: Admin-only nav link)
  Views/Products/Edit.cshtml                     (modify: read-only Quantity)
```

**Structure Decision**: existing two-project layout plus the existing test project; no new projects.

## Design notes

- **Service contract**: `InventoryService(ApplicationDbContext, IOptions<InventoryOptions>, ILogger<InventoryService>)`; `ApplyMovementAsync(int productId, int quantityChange, StockMovementReason reason, string? note, int? userId, CancellationToken)` returns `MovementResult(MovementStatus Status, StockMovement? Movement)` with status `Ok`, `NotFound`, `InsufficientStock` (below zero or overflow) or `Conflict` (3 attempts exhausted). Loop: `FindAsync`/`FirstOrDefaultAsync` the product; compute `long after`; set `Quantity`, `ConcurrencyStamp = Guid.NewGuid()`; add the movement (`QuantityAfter`, `CreatedAtUtc = DateTime.UtcNow`); `SaveChangesAsync`; on `DbUpdateConcurrencyException` call `_context.ChangeTracker.Clear()` and retry. After success, if `quantityChange < 0 && after < threshold` log `LogWarning("Low stock: product {ProductId} quantity {Quantity} is below threshold {Threshold}", ...)`.
- **Controller flow** (`PostStockMovement`): `[Authorize(Roles = "Admin")]`, `[HttpPost("{id}/stock-movements")]`. Auto-validation covers DTO annotations; then zero check, reason parse (`Enum.GetNames` case-insensitive and in `StockMovementReasonExtensions.AdminReasons`), user id from `User.FindFirstValue(ClaimTypes.NameIdentifier)` (`int.TryParse`, else null), call service; map `NotFound` -> `NotFound()`, `InsufficientStock`/`Conflict` -> `Conflict(new ProblemDetails { Status = 409, Title = "Conflict", Detail = ... })`, `Ok` -> `StatusCode(201, dto)` with `UserName` from `ClaimTypes.Name`.
- **History** (`GetStockMovements`): `AnyAsync` product check (404), `AsNoTracking` query ordered `CreatedAtUtc` desc then `Id` desc, left join to `Users` for `UserName` via the `User` navigation, `Skip/Take`, `PagedResponseDto`.
- **Low stock** (`GetLowStock`): `Where(p => p.Quantity < threshold)`, `OrderBy(Quantity).ThenBy(Id)`, map with `ToResponse(Url, threshold)`. Threshold 0 yields an empty list naturally.
- **Post**: after the category check, build the product, and when `Quantity > 0` add a `StockMovement { Product = product, QuantityChange = Quantity, QuantityAfter = Quantity, Reason = Initial, UserId = ... }` before a single `SaveChangesAsync`.
- **Put**: after the 404/category checks, `if (request.Quantity != oldProduct.Quantity)` add the model error and return `ValidationProblem`; the assignment `oldProduct.Quantity = request.Quantity` is removed. `SaveChangesAsync` wrapped in `try/catch (DbUpdateConcurrencyException)` returning the 409 problem; same wrap in `Delete`.
- **Mapping**: `ToResponse(this Product p, IUrlHelper url, int lowStockThreshold)` appends `IsLowStock: p.Quantity < lowStockThreshold`. Every caller (`ProductController` Get/Post/Put/list/UploadImage, `CategoryController.GetAllProducts`) passes `_inventory.Value.Effective`.
- **Options**: `InventoryOptions { SectionName = "Inventory"; int LowStockThreshold = 5; int Effective => LowStockThreshold < 0 ? 5 : LowStockThreshold }`.
- **Test factory**: `ApiFactory` gets `protected virtual int LowStockThreshold => 5` applied with `UseSetting("Inventory:LowStockThreshold", ...)`.
- **Migration**: generated after model changes; the model snapshot gains `StockMovement` and `Product.ConcurrencyStamp`. A pending-model-changes check (`dotnet ef migrations has-pending-model-changes` or a clean second `migrations add` that would be empty) confirms nothing is missing.
- **MVC**: `InventoryController.Index(page)` loads products (`GetProductsAsync(page, 10)`) and the low-stock count; `History(id, page)` loads the product (404) and movements; `AddMovement(id, quantityChange, reason, note)` posts through the client and uses `TempData` for success/error then redirects to `History` (same pattern as `UsersController.ChangeRole`); on `Unauthorized` it signs out and redirects to login like `ProductsController.SessionExpiredAsync` (duplicate the small private helper rather than adding a base class).

## Complexity Tracking

| Deviation | Why needed | Simpler alternative rejected because |
|---|---|---|
| `InventoryService` (Constitution III) | The retry-on-concurrency and non-negative rules must be identical for the stock endpoint and for feature 019 checkout/cancel (second concrete consumer); unit tests also target it directly | Inline logic in `ProductController` would be copied into the orders controller, and a bug in the retry loop would then exist twice |
| Touch `Put` and `Delete` | The new concurrency token makes them able to conflict with stock changes; without a catch they would return 500 | Leaving them unhandled turns a normal race into a server error |
