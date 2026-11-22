# Contract: API calls made by `CategoryProductApiClient`

Base address: `Api:BaseUrl` (default `https://localhost:7183/`). Calls are made with a typed `HttpClient`.

## Reads (public, no token)

| Method | HTTP call | Result handling |
|--------|-----------|-----------------|
| `GetCategoriesAsync(page, pageSize = 5)` | `GET api/category?page={page}&pageSize={pageSize}` | Deserialized as `PagedResult<Category>`; non-success throws |
| `GetAllCategoriesAsync()` | Repeated `GET api/category?page={n}&pageSize=50` until `page >= TotalPages` | Concatenated list |
| `GetCategoryAsync(id)` | `GET api/category/{id}` | `404` returns `null`; other failures throw |
| `GetCategoryProductsAsync(categoryId)` | `GET api/category/{categoryId}/products` | Unpaged list; `null` becomes an empty list |
| `GetProductsAsync(page, pageSize = 5)` | `GET api/product?page={page}&pageSize={pageSize}` | `PagedResult<Product>` |
| `GetProductAsync(id)` | `GET api/product/{id}` | `404` returns `null` |

## Writes (`SendAsync`)

| Method | HTTP call | Body |
|--------|-----------|------|
| `CreateCategoryAsync` | `POST api/category` | `CategoryRequest { Name }` |
| `UpdateCategoryAsync(id, c)` | `PUT api/category/{id}` | `CategoryRequest { Name }` |
| `DeleteCategoryAsync(id)` | `DELETE api/category/{id}` | none |
| `CreateProductAsync` | `POST api/product` | `ProductRequest { Name, Price, Quantity, CategoryId }` |
| `UpdateProductAsync(id, p)` | `PUT api/product/{id}` | `ProductRequest` |
| `DeleteProductAsync(id)` | `DELETE api/product/{id}` | none |

## Status mapping for writes (`ApiResult`)

| API status | `ApiResult` |
|------------|-------------|
| 2xx | `Success = true` |
| 404 | `NotFound = true` |
| 401 or 403 | `Unauthorized = true` |
| 400 | `Errors` from the `ValidationProblemDetails` `errors` object (empty if the body cannot be parsed) |
| anything else | `EnsureSuccessStatusCode` throws |

## Authentication

`ApplyBearerToken(string? token = null)` is the single hook for the `Authorization: Bearer` header. At this commit `SendAsync` calls it with no token, so no header is sent; the API rejects writes with 401 (003). 007 supplies the signed-in user's token.

The API-side contracts these calls rely on are in `specs/003-jwt-authentication/contracts/` and `specs/004-api-hardening/contracts/`.
