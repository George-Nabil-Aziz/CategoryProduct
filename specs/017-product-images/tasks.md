---

description: "Task list for 017-product-images"
---

# Tasks: Product Images

**Input**: Design documents from `/specs/017-product-images/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Required. API integration tests go in `CategoryProduct.API.Tests/ProductImageTests.cs` and are written before the implementation in each story. MVC has no test project; MVC behavior is verified through `quickstart.md`.

**Rules**: Do not hand-edit migrations. Do not run `dotnet ef database update`. 015 and 016 are implemented first; edit `Program.cs` only additively.

## Format: `[ID] [P?] [Story] Description`

## Phase 1: Setup

- [X] T001 [P] Add `CategoryProduct.API/App_Data/` to `.gitignore`
- [X] T002 [P] Add `"ProductImages": { "StoragePath": "App_Data/product-images", "MaxBytes": 2097152 }` to `CategoryProduct.API/appsettings.json`
- [X] T003 [P] Create `CategoryProduct.API/Services/ProductImageOptions.cs` (`StoragePath`, `MaxBytes`, const `SectionName = "ProductImages"`)

---

## Phase 2: Foundational (Blocking Prerequisites)

- [X] T004 Add `public string? ImageFileName { get; set; }` to `CategoryProduct.API/Models/Product.cs`
- [X] T005 Add `modelBuilder.Entity<Product>().Property(p => p.ImageFileName).HasMaxLength(100);` to `CategoryProduct.API/Data/ApplicationDbContext.cs`
- [X] T006 Generate the migration with `dotnet ef migrations add AddProductImage --project CategoryProduct.API` (output in `CategoryProduct.API/Migrations/`); do not edit it and do not run `database update`
- [X] T007 [P] Create `CategoryProduct.API/Services/IProductImageStorage.cs` and `CategoryProduct.API/Services/ProductImageStorage.cs`: `SaveAsync(int productId, Stream content, string ext, CancellationToken)` returning the generated `{productId}-{Guid:N}.{ext}`; `Delete(string? fileName)` best effort with logging; `TryGetPath(string fileName)` rejecting separators/`..` and paths outside the directory; static helper `ProductImageFormats` (allowed extensions, extension to content type, magic-byte sniffing for jpg/png/webp, `.jpeg` normalized to `jpg`)
- [X] T008 Register `Configure<ProductImageOptions>(builder.Configuration.GetSection("ProductImages"))` and `AddSingleton<IProductImageStorage, ProductImageStorage>()` in `CategoryProduct.API/Program.cs` (additive lines only)
- [X] T009 [P] Change `CategoryProduct.API/Dtos/ProductResponseDto.cs` to `record ProductResponseDto(int Id, string Name, decimal Price, int Quantity, int CategoryId, string? ImageUrl = null)`
- [X] T010 [P] Create `CategoryProduct.API/Infrastructure/ProductMappingExtensions.cs` with `ToResponse(this Product, IUrlHelper)` building `ImageUrl` via `Url.ActionLink("GetImage", "Product", new { id })` when `ImageFileName` is set
- [X] T011 [P] Update `CategoryProduct.API/Infrastructure/GlobalExceptionHandler.cs` so `BadHttpRequestException` yields its own `StatusCode` (413 for oversized bodies) as ProblemDetails; all other exceptions stay 500
- [X] T012 Update `CategoryProduct.API.Tests/ApiFactory.cs`: create a unique temp directory, `builder.UseSetting("ProductImages:StoragePath", dir)`, expose it as `ImageStoragePath`, delete it in `Dispose(bool)`

**Checkpoint**: `dotnet build CategoryProduct.slnx` succeeds and all existing tests still pass unchanged.

---

## Phase 3: User Story 1 - Admin uploads, replaces, removes (Priority: P1) MVP

**Goal**: Safe Admin-only upload, replace and removal.

**Independent Test**: Upload a PNG as Admin and get 200 with `imageUrl`; replace it and see the old file gone; delete it and get 204.

### Tests for User Story 1 (write first, expect failure)

- [X] T013 [US1] In `CategoryProduct.API.Tests/ProductImageTests.cs` add fixture helpers (login, create a fresh product per test, build multipart with minimal PNG/JPEG/WebP byte headers) and tests: upload succeeds for png, jpg, webp (200, `imageUrl` non-null, one file on disk named `{id}-*.{ext}`)
- [X] T014 [US1] In `CategoryProduct.API.Tests/ProductImageTests.cs` add rejection tests (400 with `file` error, no file on disk): wrong extension (`.gif`, `.txt`), disallowed content type, spoofed content (HTML or EXE bytes named `.png`), PNG bytes named `.jpg`, empty file, missing `file` field, file of `MaxBytes + 1`, WAV-style RIFF named `.webp`; also a client name `../../evil.png` stores a normal generated name inside the directory
- [X] T015 [US1] In `CategoryProduct.API.Tests/ProductImageTests.cs` add auth tests: anonymous upload/delete 401, non-Admin 403, unknown product 404 (no file written)
- [X] T016 [US1] In `CategoryProduct.API.Tests/ProductImageTests.cs` add replace test: second upload leaves exactly one file for the product and the first file name no longer exists
- [X] T017 [US1] In `CategoryProduct.API.Tests/ProductImageTests.cs` add removal tests: delete image returns 204, file gone, `imageUrl` null on GET product; second delete returns 204; plus a unit-level test that `GlobalExceptionHandler` returns 413 for a `BadHttpRequestException` with status 413

### Implementation for User Story 1

- [X] T018 [US1] Add `UploadImage` (`[Authorize(Roles = "Admin")]`, `[HttpPost("{id}/image")]`, `[RequestSizeLimit(2_359_296)]`, `IFormFile? file`) to `CategoryProduct.API/Controllers/ProductController.cs`; inject `IProductImageStorage`, `IOptions<ProductImageOptions>`, `ILogger<ProductController>`; validate per `contracts/api-endpoints.md`; delete the new file if `SaveChangesAsync` throws; delete the old file after success
- [X] T019 [US1] Add `DeleteImage` (`[Authorize(Roles = "Admin")]`, `[HttpDelete("{id}/image")]`, 204/404) to `CategoryProduct.API/Controllers/ProductController.cs`

**Checkpoint**: US1 tests pass.

---

## Phase 4: User Story 2 - Anyone can see the image (Priority: P1)

**Goal**: `imageUrl` in all product responses and an anonymous image endpoint.

**Independent Test**: After upload, GET product shows `imageUrl`; GET that URL returns the bytes.

### Tests for User Story 2

- [X] T020 [US2] In `CategoryProduct.API.Tests/ProductImageTests.cs` add tests: `GET api/product/{id}/image` anonymous returns exact bytes and correct content type for png/jpg/webp with `nosniff` and an ETag; 404 for unknown product, product without image, and image row whose file was deleted from disk; `imageUrl` is an absolute URL ending `/api/product/{id}/image` in `GET api/product/{id}`, in `GET api/product` list items, and in `GET api/category/{catId}/products`; `imageUrl` null for a product without image; seeded products keep all existing fields

### Implementation for User Story 2

- [X] T021 [US2] In `CategoryProduct.API/Controllers/ProductController.cs` switch `Get` (single), `Post`, `Put` and the list action to `ToResponse(Url)`; in the list action read entities with `AsNoTracking()` and map after `ToListAsync()` (the `Select(... new ProductResponseDto ...)` projection cannot build URLs)
- [X] T022 [P] [US2] In `CategoryProduct.API/Controllers/CategoryController.cs` change `GetAllProducts` the same way (entities then `ToResponse(Url)`)
- [X] T023 [US2] Add `GetImage` (`[HttpGet("{id}/image")]`, anonymous, `PhysicalFile` with content type from extension, `ETag`, `Cache-Control: no-cache`, `X-Content-Type-Options: nosniff`, 404 cases) to `CategoryProduct.API/Controllers/ProductController.cs`

**Checkpoint**: US1 and US2 tests pass; all pre-existing tests still pass.

---

## Phase 5: User Story 3 - Product delete cleans up the file (Priority: P2)

### Tests for User Story 3

- [X] T024 [US3] In `CategoryProduct.API.Tests/ProductImageTests.cs` add: deleting a product with an image returns 200 and removes the file; deleting a product with a missing file still returns 200

### Implementation for User Story 3

- [X] T025 [US3] In `ProductController.Delete` (`CategoryProduct.API/Controllers/ProductController.cs`) capture `ImageFileName`, remove and save, then call `storage.Delete` (best effort, warning logged on failure); response stays `Ok()`

**Checkpoint**: US3 tests pass.

---

## Phase 6: User Story 4 - MVC display and Admin upload (Priority: P2)

**Goal**: Visitors see images through MVC; Admins manage them. Verified manually via `quickstart.md` step 8.

- [X] T026 [P] [US4] Add `public string? ImageUrl { get; set; }` to `CategoryProduct.MVC/Models/Product.cs` (comment: used only as a has-image flag)
- [X] T027 [US4] In `CategoryProduct.MVC/Services/CategoryProductApiClient.cs` add a private `SendAsync(HttpMethod, string, HttpContent?)` overload (existing JSON `SendAsync` delegates to it), `GetProductImageAsync` (bytes and content type, null on 404, via `_http.GetAsync`), `UploadProductImageAsync` (`MultipartFormDataContent`, field `file`) and `DeleteProductImageAsync`
- [X] T028 [US4] In `CategoryProduct.MVC/Controllers/ProductsController.cs` add `Image(int id)` (anonymous, 404 when none), `UploadImage(int id, IFormFile? file)` and `RemoveImage(int id)` (both `[Authorize(Roles = "Admin")]`, `[HttpPost]`, `[ValidateAntiForgeryToken]`; upload also `[RequestSizeLimit(2_359_296)]`; use `AddErrors` and `SessionExpiredAsync`; on 400 reload the product, resolve category name, and return `View("Details", product)`)
- [X] T029 [US4] Update `CategoryProduct.MVC/Views/Products/Details.cshtml`: `<img src="@Url.Action("Image", "Products", new { id = Model.Id })">` when `Model.ImageUrl != null`; Admin-only upload form (`enctype="multipart/form-data"`, `accept=".jpg,.jpeg,.png,.webp"`, anti-forgery) and "Remove image" form; `asp-validation-summary="All"`
- [X] T030 [P] [US4] Update `CategoryProduct.MVC/Views/Products/Index.cshtml`: thumbnail column (max height 48px) using the same MVC URL when `item.ImageUrl != null`

**Checkpoint**: manual MVC check passes (quickstart step 8); page source contains no API host.

---

## Phase 7: Polish

- [X] T031 Run `dotnet build CategoryProduct.slnx` and `dotnet test CategoryProduct.slnx`; confirm all tests, old and new, pass
- [X] T032 If present, add to `docker-compose.yml`: `ProductImages__StoragePath=/data/product-images` on the `api` service, a named volume `productimages` mounted at `/data/product-images`, and the volume under top-level `volumes`
- [X] T033 If present, in `CategoryProduct.API/Dockerfile` create and own the directory before the non-root `USER` line: `RUN mkdir -p /data/product-images && chown $APP_UID /data/product-images`
- [ ] T034 Walk through `specs/017-product-images/quickstart.md` (API steps 1 to 7, MVC step 8, Docker step 9 if applicable) and fix any discrepancy

---

## Dependencies & Execution Order

- Phase 1 and Phase 2 first; T006 depends on T004/T005; T008 depends on T003 and T007; T010 depends on T004 and T009.
- US1 (T013 to T019) then US2 (T020 to T023); US2 reuses the test helpers from T013. US3 depends on US1 helpers. US4 depends on US1 and US2 (API behavior) but its tasks T026 and T030 can start in parallel with API work.
- T032 and T033 depend on 016 being implemented; skip if the files do not exist.
- Within each story, tests are written and seen failing before the implementation tasks.
