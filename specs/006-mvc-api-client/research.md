# Research: MVC Consumes the API

## Decision 1: Typed HttpClient wrapper

**Decision**: One class, `Services/CategoryProductApiClient`, registered with `builder.Services.AddHttpClient<CategoryProductApiClient>(c => c.BaseAddress = new Uri(builder.Configuration["Api:BaseUrl"]!))`. It exposes list/get/create/update/delete methods for Category and Product and returns an `ApiResult` for writes.

**Rationale**:
- One place for URLs, JSON handling and (later) auth headers, instead of `HttpClient` calls scattered over controllers.
- `AddHttpClient<T>` gives correct `HttpClient` lifetime management with no extra package.
- The API's DTO shapes are mirrored by MVC's own records (`PagedResult<T>`, `CategoryRequest`, `ProductRequest`), satisfying Principle V (no shared types or references).

**Alternatives considered**:
- **Generated client (NSwag/OpenAPI)**: adds a code generator and a build step for 10 endpoints. Rejected (Principle III).
- **Raw `HttpClient` in each controller**: duplicates URLs and error handling. Rejected.

## Decision 2: Remove, not keep, the MVC database layer

**Decision**: Delete `Data/ApplicationDbContext.cs`, remove the `Microsoft.EntityFrameworkCore.SqlServer` and `Microsoft.EntityFrameworkCore.Tools` packages, and replace `ConnectionStrings` with `Api:BaseUrl` in `appsettings.json`.

**Rationale**: Makes the API the single owner of the database (Principle V) and ends the duplicated `DbContext` that Principle II warned about. Leaving it in would allow accidental bypasses of API rules (validation, auth, CategoryId check).

**Alternatives considered**: Keep EF for reads and use the API for writes only. Rejected: two sources of truth and still a database dependency in MVC.

## Decision 3: Plain models and view-model resolution of category names

**Decision**: MVC `Category` and `Product` become plain classes (DataAnnotations only; `Category.Products` populated on Details; `Product.CategoryName` resolved by the controller). `ApiModels.cs` holds `PagedResult<T>`, `CategoryRequest`, `ProductRequest` and `ApiResult`.

**Rationale**: The API returns only `CategoryId` for a product, so controllers resolve names (all categories for Index, one lookup for Details/Delete). This keeps the API contract unchanged.

**Alternatives considered**: Add `CategoryName` to the API's `ProductResponseDto`. Rejected for this feature: it would change the API contract (002) for a UI convenience.

## Decision 4: Drop the product-count column rather than add N API calls

**Decision**: Remove the Products-count column from the Categories Index and the product list from the Category Delete page.

**Rationale**: The API category DTO has no count; one extra request per row (or a new API field) was out of scope. This is a documented behavior change.

**Alternatives considered**: Call `GET api/category/{id}/products` per row. Rejected: N+1 HTTP calls per page.

## Decision 5: Single bearer-token hook

**Decision**: `ApplyBearerToken(string? token = null)` is the only place an `Authorization` header is set; at this commit it is called with no token from `SendAsync`, so writes go out unauthenticated. 007 replaces it with a per-request header.

**Rationale**: Isolates where auth is added so 007 changes one method. API 401/403 responses are mapped to `ApiResult.Unauthorized` and shown as "You must be signed in to make changes."

**Alternatives considered**: Implement login in the same change. Rejected to keep the data-access swap reviewable on its own.
