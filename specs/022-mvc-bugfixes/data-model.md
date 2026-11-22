# Data Model: MVC Bug Fixes

## Schema impact

**None.** No entity, property, index or relationship changes; no migration. `CategoryProduct.API/Data/ApplicationDbContext.cs` and `CategoryProduct.API/Migrations/**` are untouched. The MVC app has no database. No API DTO or route changes either.

## Production code touched

| File | Change | Behavior impact |
|---|---|---|
| `CategoryProduct.MVC/Controllers/HomeController.cs` | NEW: `Index` (redirect to Categories), `Error` (500, `ErrorViewModel`) | `/Home/Error` stops being a 404; `/Home` redirects |
| `CategoryProduct.MVC/Views/Shared/Error.cshtml` | friendly text, Request ID, link to Categories; "Development Mode" text removed | visible text only |
| `CategoryProduct.MVC/Views/Home/Index.cshtml`, `Privacy.cshtml` | DELETE (unreferenced template views) | none (never rendered) |
| `CategoryProduct.MVC/Services/CategoryProductApiClient.cs` | `GetUsersAsync` returns `ApiResult<PagedResult<UserItem>>` via `GetAsync<T>`; `LogoutAsync` sends bearer and sets `SkipRefreshOption` | Users 401/403 reported instead of thrown; logout carries `Authorization` |
| `CategoryProduct.MVC/Services/TokenRefreshHandler.cs` | `public static readonly HttpRequestOptionsKey<bool> SkipRefreshOption`; early pass-through | none for requests without the option |
| `CategoryProduct.MVC/Controllers/UsersController.cs` | handle `Unauthorized`/`Forbidden`; `SessionExpiredAsync(string? returnUrl = null)` | session-expired redirect on Users |
| `CategoryProduct.MVC/Controllers/ProductsController.cs` | `AddErrors` Conflict branch | 409 detail shown |

## Types (existing, unchanged)

| Type | File | Used for |
|---|---|---|
| `ApiResult`, `ApiResult<T>` | `CategoryProduct.MVC/Models/ApiModels.cs` | `Success`, `NotFound`, `Unauthorized`, `Forbidden`, `TooManyRequests`, `Conflict`, `Errors` |
| `ErrorViewModel` | `CategoryProduct.MVC/Models/ErrorViewModel.cs` | `RequestId`, `ShowRequestId` |
| `PagedResult<T>` (`ApiModels.cs`), `UserItem` (`RegisterViewModel.cs`) | `CategoryProduct.MVC/Models/` | Users list |

## Test infrastructure touched (namespace `CategoryProduct.MVC.Tests`)

| Type | File | Change |
|---|---|---|
| `MvcFactory` | `Support/MvcFactory.cs` | no longer `sealed`; `protected virtual string EnvironmentName => "Development"` used by `UseEnvironment` |
| `ProductionMvcFactory : MvcFactory` | `Support/ProductionMvcFactory.cs` (NEW) | overrides `EnvironmentName` with `"Production"` |
| `ErrorPageTests` | `ErrorPageTests.cs` (NEW) | `MvcTestBase` + `IClassFixture<ProductionMvcFactory>` |

No new `FakeApi`, `ApiJson` or `HtmlHelpers` members are needed (`Throw`, `On`, `Calls`, `Single`, `ApiJson.ConflictProblem`, `HtmlHelpers.FieldError`/`HasText`/`Alert` exist).
