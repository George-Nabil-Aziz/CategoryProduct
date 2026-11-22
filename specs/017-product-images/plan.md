# Implementation Plan: Product Images

**Branch**: `017-product-images` | **Date**: 2026-11-22 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/017-product-images/spec.md`

## Summary

Give each product one optional image (jpg, png, webp, max 2 MB). The API stores the file on disk under `ProductImages:StoragePath` using a server-generated name, records only that name in a new `Product.ImageFileName` column, exposes Admin-only `POST`/`DELETE api/product/{id}/image` and an anonymous `GET api/product/{id}/image`, and adds an absolute `imageUrl` to every product response. File logic (path safety, magic-byte sniffing, save/delete) lives in a small `IProductImageStorage` service. MVC proxies the bytes through `Products/Image/{id}` (the browser never sees the API host), shows the image on Details and as a list thumbnail, and gives Admins an upload form and a remove button.

## Technical Context

**Language/Version**: C# / .NET 10 (existing projects).

**Primary Dependencies**: ASP.NET Core, EF Core 10 SQL Server (existing). No new NuGet package.

**Storage**: SQL Server (one nullable column via a generated migration) plus the local file system for image bytes.

**Testing**: xUnit integration tests in `CategoryProduct.API.Tests` using `WebApplicationFactory` (`ApiFactory`, EF InMemory). `ApiFactory` gains a unique temp directory for `ProductImages:StoragePath`. MVC has no test project; MVC behavior is verified through `quickstart.md`.

**Target Platform**: Windows dev and Linux containers (spec 016).

**Constraints**: additive API change only; client names never used as paths; non-root container needs a writable volume directory.

**Scale/Scope**: 3 new API actions, 1 model property, 1 migration, 1 service (interface + class), 1 options class, 1 mapping extension, 1 DTO change, 1 exception-handler change; MVC: 3 actions, 3 client methods, 2 views, 1 model property; 1 new test file plus a small `ApiFactory` change.

## Constitution Check

| Principle | Check | Status |
|---|---|---|
| I. Consistent REST API Design | Image endpoints are sub-resources of the existing `api/product` route using standard verbs (`POST` upload, `DELETE` remove, `GET` read) and standard status codes (200/204/400/401/403/404/413). No new list endpoint, so no pagination concern. `DELETE` image returns 204 while product/category `DELETE` return 200 (recorded in spec Assumptions). | PASS |
| II. Schema Changes Only Through EF Core Migrations | `ImageFileName` added via `dotnet ef migrations add AddProductImage --project CategoryProduct.API`, committed with the model change, never hand-edited, no `database update`. API and MVC still do not share a DbContext or models (MVC `Product` gets its own `ImageUrl`). | PASS |
| III. Simplicity & YAGNI | Controllers keep using `ApplicationDbContext` directly. One new abstraction, `IProductImageStorage`, justified as a genuine cross-cutting concern (file-system and path-safety logic used by five code paths, and tests must redirect it to a temp directory). No repository, no image-processing library, no thumbnails. | PASS (justified, see Complexity Tracking) |
| IV. Secrets & Credential Hygiene | No secrets involved. `ProductImages` values are non-secret configuration committed in `appsettings.json`. Storage directory is git-ignored so uploaded files are never committed. | PASS |
| V. API/MVC Separation of Concerns | MVC talks to the API only over HTTP through `CategoryProductApiClient`; it does not reference API types or share the storage directory; it proxies bytes instead of linking to the API host. | PASS |

Re-check after design: unchanged, all PASS.

## Project Structure

### Documentation

```text
specs/017-product-images/
  spec.md, plan.md, research.md, data-model.md, quickstart.md, tasks.md
  contracts/api-endpoints.md, contracts/mvc-routes.md
  checklists/requirements.md
```

### Source code changes

```text
CategoryProduct.API/
  Models/Product.cs                              (modify: ImageFileName)
  Data/ApplicationDbContext.cs                   (modify: HasMaxLength(100))
  Migrations/<timestamp>_AddProductImage*.cs     (generated)
  Dtos/ProductResponseDto.cs                     (modify: ImageUrl = null)
  Services/ProductImageOptions.cs                (new)
  Services/IProductImageStorage.cs               (new)
  Services/ProductImageStorage.cs                (new, includes format sniffing)
  Infrastructure/ProductMappingExtensions.cs     (new: ToResponse)
  Infrastructure/GlobalExceptionHandler.cs       (modify: BadHttpRequestException status)
  Controllers/ProductController.cs               (modify: new actions, mapping, delete cleanup)
  Controllers/CategoryController.cs              (modify: GetAllProducts mapping)
  Program.cs                                     (modify: add options + storage registration only)
  appsettings.json                               (modify: ProductImages section)
CategoryProduct.API.Tests/
  ApiFactory.cs                                  (modify: temp storage dir, cleanup)
  ProductImageTests.cs                           (new)
CategoryProduct.MVC/
  Models/Product.cs                              (modify: ImageUrl)
  Services/CategoryProductApiClient.cs           (modify: 3 methods + HttpContent SendAsync overload)
  Controllers/ProductsController.cs              (modify: Image, UploadImage, RemoveImage; Details shows errors)
  Views/Products/Details.cshtml, Index.cshtml    (modify)
.gitignore                                       (modify)
docker-compose.yml, CategoryProduct.API/Dockerfile  (modify only if present; created by 016)
```

**Structure Decision**: existing two-project layout plus the existing test project; no new projects.

## Design notes

- **Upload flow** (`ProductController.UploadImage`): find product (404) -> validate `file` (null/empty, size, extension, content type) -> read first 12 bytes and check signature vs extension -> `storage.SaveAsync(productId, stream, ext)` returns new file name -> set `ImageFileName`, `SaveChangesAsync` (on exception delete new file and rethrow) -> `storage.Delete(oldName)` -> 200 `ToResponse`.
- **Storage service**: resolves directory once (`Path.IsPathRooted` else `Path.Combine(env.ContentRootPath, path)`), `Directory.CreateDirectory`; `Delete` and `TryGetPath` reject any name containing a directory separator or `..` and verify the full path stays inside the directory; `Delete` swallows and logs `IOException`/`UnauthorizedAccessException`.
- **Request size**: `[RequestSizeLimit(2_359_296)]` on `UploadImage`; `GlobalExceptionHandler` returns `ex.StatusCode` (413) with ProblemDetails for `BadHttpRequestException`, still 500 for everything else.
- **GET image**: `PhysicalFile(path, contentType, null, new EntityTagHeaderValue($"\"{fileName}\""), false)` with `Cache-Control: no-cache` and `X-Content-Type-Options: nosniff` headers; content type from the extension map.
- **Mapping**: `ToResponse` uses `url.ActionLink("GetImage", "Product", new { id = p.Id })` when `ImageFileName != null`.
- **MVC**: Details re-render on 400 reloads the product and category name, copies `AddErrors` messages into `ModelState`. `RemoveImage` and `UploadImage` follow the `Edit`/`DeleteConfirmed` patterns (`SessionExpiredAsync` on 401).

## Complexity Tracking

| Deviation | Why needed | Simpler alternative rejected because |
|---|---|---|
| `IProductImageStorage` service (Constitution III) | File-system and path-safety logic is shared by upload, replace, remove, product delete and GET; tests must substitute a temp directory | Inline `File.*` in the controller duplicates security-sensitive path checks in five places |
| Touch `GlobalExceptionHandler` | It maps every exception to 500, which would turn an oversized upload into a 500 | Leaving it makes the request-size limit return the wrong status |
