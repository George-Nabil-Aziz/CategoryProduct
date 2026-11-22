# Research: Product Images

## Decision 1: Store file name in `Product.ImageFileName`, bytes on disk
- **Decision**: nullable `string?` column max 100; files under `ProductImages:StoragePath`.
- **Rationale**: matches the requirement (disk storage); keeps the database small; `{productId}-{Guid:N}.{ext}` is under 50 characters.
- **Alternatives**: `varbinary(max)` column (rejected: explicit requirement for disk, bloats backups); separate `ProductImage` table (rejected: one image per product, YAGNI).

## Decision 2: Server-generated unique file names
- **Decision**: `{productId}-{Guid:N}.{ext}`; replace = write new, update row, delete old.
- **Rationale**: no path traversal from client names, no in-place overwrite races, ETags change on replace. The productId prefix makes orphans attributable.
- **Alternatives**: `{productId}.{ext}` (rejected: overwrite races, stale caches, extension change leaves old file).

## Decision 3: Validate extension, content type and magic bytes
- **Decision**: all must agree; magic bytes authoritative. JPEG `FF D8 FF`, PNG `89 50 4E 47 0D 0A 1A 0A`, WebP `RIFF` + 4 bytes + `WEBP`. Read the first 12 bytes, then stream-copy with a hard byte cap.
- **Rationale**: extension and content type are client-controlled; sniffing blocks renamed executables/HTML. Serving with `nosniff` and a content type derived from our own extension adds defense in depth.
- **Alternatives**: decode with an imaging library (rejected: new dependency, Simplicity).

## Decision 4: Size enforcement in two layers
- **Decision**: `IFormFile.Length > MaxBytes` gives 400; `[RequestSizeLimit(2_359_296)]` on the action rejects huge bodies before buffering (413).
- **Verified in code**: `Infrastructure/GlobalExceptionHandler.cs` maps every exception to 500, so it must be taught that `BadHttpRequestException` carries its own status code (413). This is a small change to an existing file that the spec calls out.
- **Rationale**: the length check alone runs only after the body is parsed; the limit protects memory and disk.
- **Alternatives**: attribute reading `MaxBytes` dynamically (rejected: custom filter machinery for a rarely changed value; documented as Known Gap).

## Decision 5: `IProductImageStorage` service (Constitution III justification)
- **Decision**: singleton `ProductImageStorage` with `SaveAsync`, `Delete` (best effort), `TryGetPath`, plus a static format-sniffing helper in the same folder.
- **Rationale**: used by upload, replace, remove, product delete and GET; path-safety logic lives once; tests redirect the directory via configuration. This is the genuine cross-cutting concern exception the Constitution allows; no repository or unit-of-work is added.
- **Alternatives**: `File.*` calls inline in the controller (rejected: duplicated path-safety code in five places).

## Decision 6: `ImageUrl` built per request as an absolute URL
- **Decision**: `ProductResponseDto` gets `string? ImageUrl = null` (last positional parameter, defaulted). A small extension `ToResponse(this Product, IUrlHelper)` in `CategoryProduct.API/Infrastructure/ProductMappingExtensions.cs` builds the DTO using `Url.ActionLink("GetImage", "Product", new { id })`. Verified: the list endpoint (`ProductController.Get(int page...)`) and `CategoryController.GetAllProducts` currently project inside the EF query with `.Select(p => new ProductResponseDto(...))`; URL building cannot run in SQL, so they will read the entities (`AsNoTracking`) and map after `ToListAsync()`.
- **Verified callers**: `ProductResponseDto` is constructed in `ProductController` (list, get, post, put) and `CategoryController.GetAllProducts`; no test constructs it. A defaulted extra parameter breaks nothing, but all callers are updated so `imageUrl` is never wrongly null.
- **Alternatives**: relative URL (rejected by requirement: absolute); a boolean `HasImage` (rejected: requirement says URL).

## Decision 7: MVC proxies image bytes
- **Decision**: MVC `ProductsController.Image(int id)` calls a new `CategoryProductApiClient.GetProductImageAsync(id)` and returns `File(bytes, contentType)`. MVC `Product` gains `ImageUrl` (string?) used only as a "has image" flag.
- **Rationale**: in Docker the API address is `http://api:8080/` (spec 016), unreachable from browsers; Constitution V keeps MVC an HTTP client of the API.
- **Alternatives**: expose the API publicly and link directly (rejected by requirement); share the storage volume with MVC (rejected: couples projects).

## Decision 8: MVC upload forwarding
- **Decision**: add a private `SendAsync(HttpMethod, string, HttpContent?)` overload in `CategoryProductApiClient` (the existing JSON overload delegates to it) so `MultipartFormDataContent` flows through the same 404/403/401/429/400 mapping. Verified: `TokenRefreshHandler` buffers request content (`LoadIntoBufferAsync`) and clones via `ReadAsByteArrayAsync` copying content headers (including the multipart boundary), so retry-after-refresh works.
- **Rationale**: reuses `ApiResult` and the existing `AddErrors` helper in `ProductsController`.

## Decision 9: Storage directory under Docker
- **Decision**: compose sets `ProductImages__StoragePath=/data/product-images` and a named volume `productimages:/data/product-images`; the Dockerfile must create and `chown` that directory to `$APP_UID` before `USER $APP_UID` (016 contract runs as non-root). Both edits are conditional on those files existing.
- **Rationale**: a fresh named volume inherits ownership from the image directory; without it the non-root user cannot write.

## Decision 10: Caching
- **Decision**: `Cache-Control: no-cache` plus an ETag from the file name via `PhysicalFile(path, contentType, lastModified: null, entityTag, enableRangeProcessing: false)`.
- **Rationale**: correctness over cache hits; cheap 304s.

## Decision 11: Product delete flow
- **Decision**: in `ProductController.Delete`, read `ImageFileName` before removal, `SaveChangesAsync`, then call storage delete (best effort, logs warning, never throws). `ProductController` takes `IProductImageStorage` and `ILogger` through its constructor (existing constructor takes only the DbContext; no test constructs the controller directly, verified by grep of the test project).
