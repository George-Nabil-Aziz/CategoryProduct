# Feature Specification: Product Images

**Feature Branch**: `017-product-images`

**Created**: 2026-11-22

**Status**: Draft

**Input**: User description: "Allow each product to have one image (jpg, png or webp, max 2 MB). Only an Admin can upload or remove it. Images are stored on disk under a configurable path, the API returns an image URL in product responses, and the MVC site displays the image and gives Admins an upload form."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Admin uploads, replaces and removes a product image through the API (Priority: P1)

An Admin sends an image file for a product and the product now has that image. Sending another file replaces it (the old file is deleted from disk). The Admin can also remove the image, after which the product has none. Anyone who is not an Admin cannot do any of this.

**Why this priority**: Without a safe upload path there is nothing to display; all the file-handling risk (type spoofing, size, path traversal) lives here.

**Independent Test**: With an Admin token, `POST api/product/{id}/image` with a tiny valid PNG returns 200 and a `ProductResponseDto` whose `imageUrl` is non-null; repeat with a different file and confirm the first file is gone from the storage directory; `DELETE api/product/{id}/image` returns 204 and `imageUrl` becomes null.

**Acceptance Scenarios**:

1. **Given** an existing product without an image and an Admin token, **When** a valid jpg, png or webp of at most 2 MB is posted as multipart field `file`, **Then** the response is 200 with the product (including a non-null `imageUrl`) and exactly one file for that product exists in the storage directory.
2. **Given** a product that already has an image, **When** a new valid image is posted, **Then** the product points to the new file and the previous file is deleted from disk.
3. **Given** a product with an image, **When** an Admin calls `DELETE api/product/{id}/image`, **Then** the response is 204, the file is deleted, and the product's `imageUrl` is null.
4. **Given** a product without an image, **When** an Admin calls `DELETE api/product/{id}/image`, **Then** the response is 204 (idempotent, nothing to delete).
5. **Given** an Admin token, **When** the file is missing or empty, has an extension other than `.jpg`/`.jpeg`/`.png`/`.webp`, declares a content type other than `image/jpeg`/`image/png`/`image/webp`, has content whose first bytes do not match the format its extension claims (for example an `.exe` or `.html` renamed to `.png`), or is larger than the configured maximum, **Then** the response is 400 with a validation problem keyed `file` and nothing changes on disk or in the database.
6. **Given** an unknown product id, **When** upload or removal is requested by an Admin, **Then** the response is 404 and no file is written.
7. **Given** no token, **Then** upload and removal return 401; **Given** a non-Admin token, **Then** they return 403; in both cases nothing changes.

---

### User Story 2 - Anyone can see a product's image (Priority: P1)

Product responses include an `imageUrl` pointing at an endpoint that serves the image bytes with the right content type. Like product reads today, fetching the image requires no login.

**Why this priority**: The image is only useful if clients can discover and fetch it.

**Independent Test**: Upload an image, then `GET api/product/{id}` and confirm `imageUrl` is an absolute URL ending in `/api/product/{id}/image`; `GET` that URL anonymously and confirm the bytes and `Content-Type` match what was uploaded.

**Acceptance Scenarios**:

1. **Given** a product with an image, **When** `GET api/product/{id}`, `GET api/product` (list) or `GET api/category/{catId}/products` returns it, **Then** each item carries `imageUrl` as an absolute URL built from the current request (scheme and host the caller used).
2. **Given** a product without an image, **Then** `imageUrl` is `null` in every response shape.
3. **Given** a product with a png/jpg/webp image, **When** `GET api/product/{id}/image` is called without authentication, **Then** the response is 200 with the exact stored bytes and content type `image/png`, `image/jpeg` or `image/webp` respectively.
4. **Given** an unknown product, a product with no image, or a database row whose file is missing on disk, **When** `GET api/product/{id}/image` is called, **Then** the response is 404.
5. **Given** existing consumers that read only `id`, `name`, `price`, `quantity`, `categoryId`, **Then** they keep working: the only change to the response shape is one additional nullable property.

---

### User Story 3 - Deleting a product cleans up its image (Priority: P2)

When an Admin deletes a product, the product's image file is removed from disk so storage does not fill with orphans.

**Why this priority**: Correctness of storage hygiene; the product itself is still deleted even if file cleanup fails.

**Independent Test**: Upload an image, delete the product, confirm the file no longer exists in the storage directory.

**Acceptance Scenarios**:

1. **Given** a product with an image, **When** an Admin deletes the product, **Then** the product is deleted (response unchanged, 200) and its image file is removed.
2. **Given** the file cannot be deleted (locked, already missing, permission), **When** the product is deleted, **Then** the deletion still succeeds and a warning is logged.

---

### User Story 4 - MVC shows the image and lets Admins manage it (Priority: P2)

On the product Details page (and as a thumbnail in the product list) visitors see the image when there is one. An Admin sees an upload form and a "Remove image" button on the Details page. API validation errors (wrong type, too large, spoofed content) are shown on the page.

**Why this priority**: This is the user-facing result; it depends on stories 1 and 2.

**Independent Test**: Sign in as Admin in the MVC site, open a product's Details, choose a png, submit, and see it displayed; sign out and confirm the image still shows but the form does not.

**Acceptance Scenarios**:

1. **Given** a product with an image, **When** any visitor opens Details or the product list, **Then** an `<img>` is shown whose `src` is an MVC URL (`/Products/Image/{id}`), never the API host.
2. **Given** `GET /Products/Image/{id}` for a product with an image, **Then** MVC fetches the bytes from the API with its API client and returns them with the API's content type; for no image or unknown product it returns 404.
3. **Given** an Admin on Details, **Then** a form (`enctype="multipart/form-data"`, file input with `accept=".jpg,.jpeg,.png,.webp"`, anti-forgery token) and, when an image exists, a "Remove image" button are shown; non-Admins and anonymous visitors see neither.
4. **Given** the API rejects an upload with 400, **Then** the Details page is re-rendered with the API's messages shown and no redirect.
5. **Given** a non-Admin or anonymous request posts to the MVC upload or remove actions, **Then** MVC redirects to login / access denied exactly as for other admin-only actions (spec 008) and nothing reaches the API.
6. **Given** the API replies 401 to an upload, **Then** the user is signed out and sent to login (same as other write actions).

---

### Edge Cases

- Concurrent uploads for one product: last writer wins; because every stored name is unique (`{productId}-{Guid:N}.{ext}`), no file is ever overwritten in place, and a superseded file is deleted best effort.
- Client filename like `../../x.png` or with unusual characters: ignored entirely; only the extension is read (and only after checking it is in the allow-list).
- Uppercase extension (`PHOTO.PNG`) or `.jpeg`: accepted (compared case-insensitively; `.jpeg` is stored as `.jpg`).
- Content type header absent while extension and bytes are valid: accepted (magic bytes are authoritative). A present header that is not an allowed image type (including `application/octet-stream`) is rejected. The header never overrides the sniffed format.
- WebP check is `RIFF` at offset 0 and `WEBP` at offset 8; a RIFF file that is a WAV is rejected.
- Body grossly larger than the file limit: rejected by the request size limit before the whole body is read (413, see FR-012); files between `MaxBytes` and the request limit get a 400 with a clear message.
- Storage directory missing: created automatically. Not writable: upload returns 500 through the existing global handler (logged); other endpoints still work.
- Database save fails after the new file was written: the new file is deleted and the old image is untouched.
- The `ImageFileName` stored in the database is never trusted as a path: it is only ever combined with the storage directory after confirming it contains no directory separators.
- Product updates (`PUT api/product/{id}`) never touch the image.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Each product MUST have at most one image. The product record stores only the server-generated file name (`ImageFileName`, nullable, max 100 characters); image bytes MUST NOT be stored in the database.
- **FR-002**: `POST api/product/{id}/image` MUST accept a multipart form with a single file in field `file`, require the Admin role, and return 200 with the updated `ProductResponseDto`.
- **FR-003**: Upload MUST reject with 400 (validation problem on key `file`): a missing or zero-length file; an extension not in {`.jpg`, `.jpeg`, `.png`, `.webp`} (case-insensitive); a present content type not in {`image/jpeg`, `image/png`, `image/webp`}; content whose leading bytes are not a JPEG (`FF D8 FF`), PNG (`89 50 4E 47 0D 0A 1A 0A`) or WebP (`52 49 46 46`, any 4 size bytes, `57 45 42 50`) signature or that disagrees with the extension's format; and a file larger than `ProductImages:MaxBytes`.
- **FR-004**: Upload MUST return 404 for an unknown product, 401 without a valid token and 403 for a non-Admin, in each case before any file is written.
- **FR-005**: The stored file name MUST be generated by the server as `{productId}-{Guid:N}.{ext}` with `ext` in {`jpg`, `png`, `webp`}; the client-supplied file name MUST NOT be used for any path or stored value.
- **FR-006**: Uploading to a product that already has an image MUST replace it and delete the previous file (best effort, failures logged, never failing the request).
- **FR-007**: `DELETE api/product/{id}/image` MUST require the Admin role, delete the file (best effort), clear `ImageFileName`, and return 204; 404 for an unknown product; 204 when there is no image.
- **FR-008**: `GET api/product/{id}/image` MUST be anonymous (same access as `GET api/product/{id}`), stream the file with the content type implied by its extension, and return 404 when the product, its image, or the file on disk does not exist. It MUST send `X-Content-Type-Options: nosniff` and an `ETag` derived from the stored file name.
- **FR-009**: `ProductResponseDto` MUST gain `ImageUrl` (string?, default null) as the last positional parameter. Every place that builds the DTO (`ProductController` list/get/post/put/upload, `CategoryController.GetAllProducts`) MUST populate it: an absolute URL to the GET image endpoint built from the current request, or null when the product has no image.
- **FR-010**: Deleting a product MUST also delete its image file (best effort, logged on failure); the product deletion result is unchanged.
- **FR-011**: Image storage MUST be configured by section `ProductImages` with `StoragePath` (default `App_Data/product-images`; relative paths resolved against the content root; directory created if missing) and `MaxBytes` (default `2097152`). Neither value is a secret. The default storage directory MUST be git-ignored.
- **FR-012**: The upload action MUST carry a request size limit slightly above the default `MaxBytes` (multipart overhead) so grossly oversized bodies are rejected early with 413; the API's global exception handler MUST translate the framework's oversized-body exception (`BadHttpRequestException`) to its own status code (413) instead of 500.
- **FR-013**: All file-system operations MUST live behind `IProductImageStorage` so tests can point it at a temporary directory.
- **FR-014**: MVC MUST NOT put the API's address in any `src`/`href` rendered to the browser. It MUST expose `GET Products/Image/{id}` which fetches the bytes through `CategoryProductApiClient` and returns them with the API's content type (404 if none).
- **FR-015**: MVC Details MUST show the image when present; the product list MUST show a thumbnail; Admins MUST see an upload form (`accept=".jpg,.jpeg,.png,.webp"`, anti-forgery) and a remove button; others MUST NOT. MVC actions for upload and remove MUST be `[Authorize(Roles = "Admin")]`, `[ValidateAntiForgeryToken]`, and show API 400 messages on the page.
- **FR-016**: No existing product, category, auth or health behavior may change other than the additive `imageUrl` property and the 413 mapping.
- **FR-017**: If `docker-compose.yml` exists at implementation time, the `api` service MUST persist images in a named volume mounted at the configured storage path so images survive container re-creation.

### Key Entities

- **Product** (modified): gains `ImageFileName` (string?, max 100). Null means no image.
- **Stored image file**: a file in the configured directory named by `ImageFileName`; owned by exactly one product.
- **ProductImages options**: `StoragePath`, `MaxBytes`.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An Admin can attach a valid image to a product in one request and see it in the MVC site on the next page load.
- **SC-002**: 100% of uploads whose bytes are not a real JPEG/PNG/WebP, whose extension is not allowed, or that exceed 2 MB are rejected with 400 and leave storage and database unchanged (covered by integration tests).
- **SC-003**: After any sequence of upload, replace, remove and product-delete operations in a test, the storage directory contains no file that is not referenced by a product.
- **SC-004**: Every existing automated test still passes unchanged, and the whole suite passes with the new image tests added.
- **SC-005**: In the MVC site no rendered page contains the API base address (verified by inspection of `src` attributes in the quickstart).

## Assumptions

- **Dependencies**: 015 (auth hardening) and 016 (docker/CI) are implemented first; this feature touches `Program.cs` only with additive registrations, and if `docker-compose.yml` and `CategoryProduct.API/Dockerfile` exist they receive small additive edits (FR-017, tasks T032 and T033).
- **Product fields**: `Product.ImageFileName` (`string?`, `HasMaxLength(100)`), added with `dotnet ef migrations add AddProductImage --project CategoryProduct.API`; the migration is generated, never hand-edited, and `dotnet ef database update` is never run by this work (Constitution II).
- **Config**: section `ProductImages` with `StoragePath` and `MaxBytes` as in FR-011. Verified: `appsettings.json` has no such section today and the API already reads configuration via `builder.Configuration`.
- **Routing**: `AddRouting(o => o.LowercaseUrls = true)` was added in `CategoryProduct.API/Program.cs` so `Url.ActionLink` produces `/api/product/{id}/image`; it lowercases all generated URLs (e.g. Location headers); routing stays case-insensitive.
- **StoragePath resolution**: a relative `StoragePath` is resolved against `IHostEnvironment.ContentRootPath` (not the process working directory); an absolute path is used as-is.
- **Docker**: inside a container the process runs as non-root and `/app` is not writable, so compose sets `ProductImages__StoragePath=/data/product-images` backed by a volume; the image build must create and own that directory.
- **Status codes**: upload returns 200 with the product (matching existing `POST`/`PUT` product actions, which return 200 with the DTO). Image removal returns 204 as requested, which differs from product/category `DELETE` (those return plain 200 `Ok()`); this is accepted because the image delete has no body to return.
- **400 shape**: validation failures use `ValidationProblem(ModelState)` with key `file`, the same shape existing product endpoints use, so `CategoryProductApiClient.SendAsync` already parses it.
- **Abstraction**: an `IProductImageStorage` service is introduced although the Constitution (III) prefers controllers using the DbContext directly. Justification: the file system is a genuine cross-cutting concern with several consumers (upload, replace, remove, product delete, GET) and security-sensitive path handling that belongs in one place, and tests must redirect it to a temp directory. No repository layer is added for the database.
- **Caching**: `GET` image responses use `Cache-Control: no-cache` with an ETag so a replaced image shows up immediately while unchanged ones return 304.
- **Authentication scope**: MVC `Products/Image/{id}` is anonymous, like the product pages themselves.
- **Out of scope**: multiple images per product, thumbnails/resizing, image metadata stripping, antivirus scanning, CDN/blob storage, drag-and-drop UI, image on create/edit forms.

## Known Gaps

- Magic-byte checks prove the file starts like an image, not that it is a fully valid, decodable or harmless image; no re-encoding or EXIF stripping is performed.
- The `[RequestSizeLimit]` attribute value is a compile-time constant (2,359,296 bytes, about 2.25 MiB). If `MaxBytes` is configured above roughly 2.1 MB the attribute must be raised too; the 400 size check still enforces `MaxBytes` for anything under the constant.
- The in-memory `TestServer` used by the integration tests may not enforce the request size limit, so the 413 path is verified manually (see quickstart) and by a unit-level check of the exception handler mapping.
- Files are local to one API instance; running several API replicas needs shared storage (out of scope).
- Orphan files caused by a crash between writing a file and saving the database row are not garbage-collected.
- COVERED by `021-test-coverage` (pending the first CI run). Original gap: the MVC pages (Details upload/remove, Index thumbnails, `Products/Image/{id}` proxy) are now verified for server-rendered behavior by `ProductImageTests`; the Docker volume is exercised only by the `compose-smoke` start-up (not persistence across a container recreate), and the 413 path stays manual. Original text: the MVC pages and the Docker volume persistence were not verified in a browser or in Docker during implementation (quickstart steps 8 and 9, task T034); they compile and the API side is covered by integration tests.
- Image endpoint concurrency: `POST`/`DELETE api/product/{id}/image` now return `409` on a concurrency conflict instead of `500` (`020-hardening-followups` US3); the other gaps above remain.
