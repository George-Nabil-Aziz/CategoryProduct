---

description: "Task list for 018-inventory"
---

# Tasks: Inventory (Stock Movements and Low-Stock Alerts)

**Input**: Design documents from `/specs/018-inventory/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Required. API integration and unit tests go in `CategoryProduct.API.Tests/InventoryTests.cs` and are written before the implementation in each story. MVC has no test project; MVC behavior is verified through `quickstart.md`.

**Rules**: Do not hand-edit migrations. Do not run `dotnet ef database update`. 015, 016 and 017 are implemented first: this feature builds on 017's `ProductResponseDto.ImageUrl`, `Infrastructure/ProductMappingExtensions.cs` (`ToResponse(Url)`) and the controllers' use of it. Edit `Program.cs` only additively.

## Format: `[ID] [P?] [Story] Description`

## Phase 1: Setup

- [X] T001 [P] Add `"Inventory": { "LowStockThreshold": 5 }` to `CategoryProduct.API/appsettings.json`
- [X] T002 [P] Create `CategoryProduct.API/Services/InventoryOptions.cs` (const `SectionName = "Inventory"`, `int LowStockThreshold { get; set; } = 5`, `int Effective => LowStockThreshold < 0 ? 5 : LowStockThreshold`)

---

## Phase 2: Foundational (Blocking Prerequisites)

- [X] T003 [P] Create `CategoryProduct.API/Models/StockMovementReason.cs`: enum `Initial, Restock, Adjustment, Damage, Return, Sale, OrderCancelled` plus static helper `StockMovementReasonExtensions` with `AdminReasons` (`Restock, Adjustment, Damage, Return`) and `TryParseName(string?, out StockMovementReason)` that matches `Enum.GetNames` case-insensitively (rejects numeric strings)
- [X] T004 [P] Create `CategoryProduct.API/Models/StockMovement.cs` per `data-model.md` (Id, ProductId, Product, QuantityChange, QuantityAfter, Reason, Note, UserId, User, CreatedAtUtc)
- [X] T005 Add `public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();` to `CategoryProduct.API/Models/Product.cs`
- [X] T006 In `CategoryProduct.API/Data/ApplicationDbContext.cs` add `DbSet<StockMovement> StockMovements` and configure: `Product.ConcurrencyStamp` `.IsConcurrencyToken()`; `StockMovement` `Reason` `.HasConversion<string>().HasMaxLength(30).IsRequired()`, `Note` `.HasMaxLength(200)`, `HasOne(Product).WithMany().HasForeignKey(ProductId).OnDelete(Cascade)`, `HasOne(User).WithMany().HasForeignKey(UserId).OnDelete(SetNull)`, `HasIndex(ProductId, CreatedAtUtc)`
- [X] T007 Generate the migration with `dotnet ef migrations add AddInventory --project CategoryProduct.API` (output in `CategoryProduct.API/Migrations/`); do not edit it and do not run `database update`; confirm it contains the `StockMovements` table and the `Products.ConcurrencyStamp` column
- [X] T008 [P] Create `CategoryProduct.API/Dtos/StockMovementRequestDto.cs` (`int QuantityChange` with `[Range(-1000000, 1000000)]`, `string Reason` with `[Required(AllowEmptyStrings = false)]`, `string? Note` with `[StringLength(200)]`) and `CategoryProduct.API/Dtos/StockMovementResponseDto.cs` (`Id, ProductId, QuantityChange, QuantityAfter, Reason (string), Note, UserId, UserName, CreatedAtUtc`)
- [X] T009 [P] Update `CategoryProduct.API.Tests/ApiFactory.cs`: add `protected virtual int LowStockThreshold => 5` and `builder.UseSetting("Inventory:LowStockThreshold", LowStockThreshold.ToString())`

**Checkpoint**: `dotnet build CategoryProduct.slnx` succeeds and all existing tests still pass unchanged.

---

## Phase 3: User Story 1 - Admin adjusts stock through a movement (Priority: P1) MVP

**Goal**: Admin-only endpoint that changes stock by recording a movement, safely under concurrency.

**Independent Test**: As Admin post `+10 Restock` to a product at 5 and get 201 with `quantityAfter` 15; the product now has quantity 15.

### Tests for User Story 1 (write first, expect failure)

- [X] T010 [US1] In `CategoryProduct.API.Tests/InventoryTests.cs` add fixture helpers (admin login via `AuthHelper.LoggedInAsync`, create a fresh product with a given quantity through `POST api/product`, read product and movements) and success tests: Restock +10 (201, `quantityAfter` 15, GET product shows 15, `userId` equals the Admin's id, `userName` `tester`, `createdAtUtc` recent UTC), Damage -3, Return and Adjustment accepted, lower-case `restock` accepted and returned as `Restock`, note trimmed and whitespace-only note returned as null
- [X] T011 [US1] In `InventoryTests.cs` add 400 tests (no change stored, product quantity unchanged): `quantityChange` 0, outside +-1,000,000, reason missing/empty/unknown/`"3"`, reasons `Sale`, `OrderCancelled`, `Initial`, note of 201 characters; assert errors keys `QuantityChange`, `Reason`, `Note`
- [X] T012 [US1] In `InventoryTests.cs` add status tests: anonymous 401, `plainuser` 403, unknown product 404 (nothing written), result below zero 409 (`ProblemDetails`, quantity unchanged, no movement), exactly to zero 201, overflow (product at `int.MaxValue - 1` via direct DbContext seed, +5) 409
- [X] T013 [US1] In `InventoryTests.cs` add a `RacingFactory : ApiFactory` with a `SaveChangesInterceptor` (pattern from `RefreshTokenTests.RaceInterceptor`): when armed and a `Product` entry is `Modified`, a second `ApplicationDbContext` adds 100 to the quantity and sets a new `ConcurrencyStamp`; tests: armed once -> 201 and final quantity equals start + 100 + change and two-attempt success; armed always -> 409 after exactly 3 attempts, product quantity is what the interferer wrote, no movement for the loser
- [X] T014 [US1] In `InventoryTests.cs` add `InventoryService` unit tests using an InMemory context built directly: ok path sets quantity, stamp changes and the movement has `QuantityAfter`; `InsufficientStock` leaves everything unchanged; unknown id returns `NotFound`

### Implementation for User Story 1

- [X] T015 [US1] Create `CategoryProduct.API/Services/InventoryService.cs` with `InventoryService(ApplicationDbContext, IOptions<InventoryOptions>, ILogger<InventoryService>)`, `MovementStatus` enum (`Ok, NotFound, InsufficientStock, Conflict`), `MovementResult` record and `ApplyMovementAsync(int productId, int quantityChange, StockMovementReason reason, string? note, int? userId, CancellationToken ct = default)`: up to 3 attempts of load product, compute in `long`, reject below zero or above `int.MaxValue`, set quantity and a new `ConcurrencyStamp`, add movement (`QuantityAfter`, `CreatedAtUtc = DateTime.UtcNow`), `SaveChangesAsync`; on `DbUpdateConcurrencyException` call `ChangeTracker.Clear()` and retry; return `Conflict` after the third failure
- [X] T016 [US1] Register `Configure<InventoryOptions>(builder.Configuration.GetSection(InventoryOptions.SectionName))` and `AddScoped<InventoryService>()` in `CategoryProduct.API/Program.cs` (additive lines only)
- [X] T017 [US1] Add `PostStockMovement` (`[Authorize(Roles = "Admin")]`, `[HttpPost("{id}/stock-movements")]`, `StockMovementRequestDto`) to `CategoryProduct.API/Controllers/ProductController.cs`: inject `InventoryService`; zero check and reason check (`TryParseName` plus `AdminReasons`) as `ModelState` errors on `QuantityChange`/`Reason` then `ValidationProblem`; trim note (null if empty); user id from `User.FindFirstValue(ClaimTypes.NameIdentifier)` with `int.TryParse`; map service result: `NotFound` -> `NotFound()`, `InsufficientStock`/`Conflict` -> `Conflict(new ProblemDetails { Status = 409, Title = "Conflict", Detail = ... })`, `Ok` -> `StatusCode(201, StockMovementResponseDto)` with `UserName` from `ClaimTypes.Name`

**Checkpoint**: US1 tests pass.

---

## Phase 4: User Story 2 - Movement history per product (Priority: P1)

**Goal**: Initial movement on create and a paged history endpoint.

**Independent Test**: Create a product with quantity 7, restock +3, list: newest first, two items.

### Tests for User Story 2

- [X] T018 [US2] In `InventoryTests.cs` add: create with quantity 7 produces one `Initial` movement (`quantityChange` 7, `quantityAfter` 7, user = Admin); create with quantity 0 produces none; seeded products from `ApiFactory` have no movements
- [X] T019 [US2] In `InventoryTests.cs` add history tests: newest first with `id` desc tie-break, paging (`pageSize=2` over 5 movements: `totalCount` 5, `totalPages` 3, correct page contents), clamping (`pageSize=0` -> 1, `pageSize=500` -> 50, `page=0` -> 1), 404 unknown product, 401, 403, and the invariant sum of `quantityChange` equals product quantity

### Implementation for User Story 2

- [X] T020 [US2] In `ProductController.Post` (`CategoryProduct.API/Controllers/ProductController.cs`), when `request.Quantity > 0` add a `StockMovement` (`Product = product`, `QuantityChange = Quantity`, `QuantityAfter = Quantity`, `Reason = Initial`, `UserId` from the claim, `CreatedAtUtc = DateTime.UtcNow`) before the existing single `SaveChangesAsync`
- [X] T021 [US2] Add `GetStockMovements` (`[Authorize(Roles = "Admin")]`, `[HttpGet("{id}/stock-movements")]`, `int page = 1, int pageSize = 10`) to `ProductController`: clamp like the product list, 404 if the product does not exist, `AsNoTracking` query ordered `CreatedAtUtc` desc then `Id` desc, project to `StockMovementResponseDto` with `UserName` from the `User` navigation, return `PagedResponseDto`

**Checkpoint**: US1 and US2 tests pass.

---

## Phase 5: User Story 3 - Low-stock alert (Priority: P1)

**Goal**: `isLowStock` in all product responses, Admin low-stock list, warning log.

**Independent Test**: Threshold 5: product at 4 is flagged and listed; after a restock to 5 it is neither.

### Tests for User Story 3

- [X] T022 [US3] In `InventoryTests.cs` add flag tests: `isLowStock` true at quantity 4 and false at 5 and 6 in `GET api/product/{id}`, in `GET api/product` list items and in `GET api/category/{catId}/products`; flag flips after a Restock; `POST` and `PUT` responses carry the flag; JSON still contains `id, name, price, quantity, categoryId`
- [X] T023 [US3] In `InventoryTests.cs` add low-stock endpoint tests: only products below the threshold are returned (assert by product id sets), order quantity asc then id asc, paging fields, 401, 403, and that `GET api/product/low-stock` returns the list (not 400/404 from the `{id}` route)
- [X] T024 [US3] In `InventoryTests.cs` add derived factories overriding `LowStockThreshold`: 0 -> nothing flagged and empty list; 100 -> every seeded product flagged; verify `InventoryOptions.Effective` maps a negative value to 5 (unit test)
- [X] T025 [US3] In `InventoryTests.cs` add warning-log tests with a capturing `ILogger<InventoryService>`: negative change that crosses below the threshold logs exactly one warning containing product id, quantity and threshold; a restock, or a negative change that stays at or above the threshold, logs none

### Implementation for User Story 3

- [X] T026 [P] [US3] Change `CategoryProduct.API/Dtos/ProductResponseDto.cs` to `record ProductResponseDto(int Id, string Name, decimal Price, int Quantity, int CategoryId, string? ImageUrl = null, bool IsLowStock = false)`
- [X] T027 [US3] Update `CategoryProduct.API/Infrastructure/ProductMappingExtensions.cs` (created by 017): `ToResponse(this Product p, IUrlHelper url, int lowStockThreshold)` sets `IsLowStock: p.Quantity < lowStockThreshold`
- [X] T028 [US3] Inject `IOptions<InventoryOptions>` into `ProductController` (`CategoryProduct.API/Controllers/ProductController.cs`) and `CategoryController` (`CategoryProduct.API/Controllers/CategoryController.cs`) and pass `Value.Effective` to every `ToResponse` call (Get single, list, Post, Put, 017 `UploadImage`, `GetAllProducts`)
- [X] T029 [US3] Add `GetLowStock` (`[Authorize(Roles = "Admin")]`, `[HttpGet("low-stock")]`, `int page = 1, int pageSize = 10`) to `ProductController`: `Where(p => p.Quantity < threshold)`, `OrderBy(Quantity).ThenBy(Id)`, `AsNoTracking`, `ToListAsync` then `ToResponse(Url, threshold)`, `PagedResponseDto`; place it next to `Get(int id)` and verify the literal route wins

**Checkpoint**: US1 to US3 tests pass; all pre-existing tests still pass.

---

## Phase 6: User Story 4 - Quantity changes only through movements (Priority: P2)

### Tests for User Story 4

- [X] T030 [US4] In `InventoryTests.cs` add PUT tests: different quantity -> 400 with errors key `Quantity` and message "Use stock movements to change quantity." and name/price/category unchanged; same quantity with a new name -> 200; PUT with a different quantity creates no movement
- [X] T031 [US4] In `InventoryTests.cs` using `RacingFactory`: a PUT (new name) whose save is raced by a stock change returns 409 `ProblemDetails`, and a DELETE raced the same way returns 409

### Implementation for User Story 4

- [X] T032 [US4] In `ProductController.Put` replace `oldProduct.Quantity = request.Quantity;` with a check: if `request.Quantity != oldProduct.Quantity` add `ModelState` error on `nameof(request.Quantity)` ("Use stock movements to change quantity.") and return `ValidationProblem(ModelState)` before any property is modified
- [X] T033 [US4] Wrap `SaveChangesAsync` in `ProductController.Put` and `ProductController.Delete` with `try/catch (DbUpdateConcurrencyException)` returning `Conflict(new ProblemDetails { Status = 409, Title = "Conflict", Detail = "The product was changed by another request. Reload and try again." })`

**Checkpoint**: US4 tests pass.

---

## Phase 7: User Story 5 - Inventory page in MVC (Priority: P2)

**Goal**: Admin-only list with banner, history and add-movement form. Verified manually via `quickstart.md` (MVC walkthrough).

- [X] T034 [P] [US5] Add `public bool IsLowStock { get; set; }` to `CategoryProduct.MVC/Models/Product.cs` and add `StockMovementItem` and `StockMovementRequest` records to `CategoryProduct.MVC/Models/ApiModels.cs`
- [X] T035 [US5] In `CategoryProduct.MVC/Services/CategoryProductApiClient.cs` add `GetStockMovementsAsync(int id, int page, int pageSize = 10)` (authenticated request with `ApplyBearerToken`, null on 404, `EnsureSuccessStatusCode` otherwise, `PagedResult<StockMovementItem>`), `GetLowStockCountAsync()` (authenticated `api/product/low-stock?page=1&pageSize=1`, returns `TotalCount`) and `AddStockMovementAsync(int id, StockMovementRequest)` (existing `SendAsync` to `api/product/{id}/stock-movements`)
- [X] T036 [US5] Create `CategoryProduct.MVC/Controllers/InventoryController.cs` (`[Authorize(Roles = "Admin")]`): `Index(int page = 1)`, `[HttpGet("Inventory/Product/{id:int}")] History(int id, int page = 1)` (404 for unknown product), `[HttpPost("Inventory/Product/{id:int}"), ValidateAntiForgeryToken] AddMovement(int id, int quantityChange, string reason, string? note, int page = 1)` using `TempData["Success"]`/`TempData["Error"]` (conflict message, joined 400 errors, forbidden), and sign-out redirect on 401 like `ProductsController.SessionExpiredAsync`
- [X] T037 [P] [US5] Create `CategoryProduct.MVC/Views/Inventory/Index.cshtml`: low-stock warning banner when the count is above 0, table (name, quantity, "Low stock" badge, "History" link), Previous/Next like `Views/Users/Index.cshtml`, TempData alerts
- [X] T038 [P] [US5] Create `CategoryProduct.MVC/Views/Inventory/History.cshtml`: product header with quantity and badge, add-movement form (reason dropdown of Restock, Adjustment, Damage, Return; number input for signed quantity; note; anti-forgery), TempData alerts, movements table (local time of `CreatedAtUtc`, change, quantity after, reason, note, user name or "-"), Previous/Next
- [X] T039 [P] [US5] In `CategoryProduct.MVC/Views/Shared/_Layout.cshtml` add an "Inventory" nav item (`asp-controller="Inventory" asp-action="Index"`) inside the existing `@if (User.IsInRole("Admin"))` block
- [X] T040 [P] [US5] In `CategoryProduct.MVC/Views/Products/Edit.cshtml` make the Quantity input `readonly` (still posted) and add a hint with a link to `Inventory/History` for that product; leave `Create.cshtml` editable (the initial quantity becomes the `Initial` movement)

**Checkpoint**: manual MVC check passes (quickstart MVC walkthrough).

---

## Phase 8: Polish

- [X] T041 Search `CategoryProduct.API.Tests` for any existing test that sends a product PUT with a different quantity (expected none, as verified when writing the spec); fix any that appear
- [X] T042 Run `dotnet build CategoryProduct.slnx` and `dotnet test CategoryProduct.slnx`; confirm all tests, old and new, pass; confirm `dotnet ef migrations has-pending-model-changes --project CategoryProduct.API` reports no pending changes
- [X] T043 If `docker-compose.yml` exists, optionally add `Inventory__LowStockThreshold: ${LOW_STOCK_THRESHOLD:-5}` to the `api` service environment and document it in `.env.example`
- [ ] T044 Walk through `specs/018-inventory/quickstart.md` (API, MVC and SQL Server steps) and fix any discrepancy

---

## Dependencies & Execution Order

- Phase 1 and Phase 2 first; T006 depends on T003 to T005; T007 depends on T006; T009 is independent.
- US1 (T010 to T017): T015 depends on T003, T004, T005, T006; T016 on T002 and T015; T017 on T008, T015, T016. Tests T010 to T014 are written before T015 to T017.
- US2 (T018 to T021) reuses the test helpers from T010; T020 and T021 edit `ProductController.cs` after T017.
- US3 (T022 to T029): T027 depends on 017's `ProductMappingExtensions.cs` and T026; T028 depends on T027 and T016; T029 depends on T028. T022 to T025 are written before T026 to T029.
- US4 (T030 to T033) depends on T013 (`RacingFactory`) and T017; T032 and T033 edit `ProductController.cs`.
- US5 depends on the API behavior of US1 to US3; T034, T037, T038, T039, T040 can start in parallel with API work but T036 needs T035.
- Tasks that edit `ProductController.cs` (T017, T020, T021, T028, T029, T032, T033) are sequential, never `[P]`.
- Within each story, tests are written and seen failing before the implementation tasks.
