# Contract: MVC 429 handling (Users and Products)

Text everywhere: `Too many requests. Try again in a minute.`

## New pieces (`CategoryProduct.MVC`)

```csharp
// Services/ApiRateLimit.cs
public static class ApiRateLimit { public const string Message = "Too many requests. Try again in a minute."; }

// Services/ApiErrorHandler.cs (next to ApiUnavailableFilter)
public class TooManyRequestsFilter : IExceptionFilter
{
    // HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } ->
    // ViewResult "TooManyRequests", StatusCode 429, ViewData["Message"] = ApiRateLimit.Message; ExceptionHandled = true
}
// Program.cs: o.Filters.Add<TooManyRequestsFilter>() beside ApiUnavailableFilter
```

`Views/Shared/TooManyRequests.cshtml`: title "Too many requests", heading, `<div class="alert alert-warning">@ViewData["Message"]</div>`, Retry button like `ServiceUnavailable.cshtml`. No exception text.

## Controllers

```text
UsersController.Index:
  result.Unauthorized -> SessionExpiredAsync()          (existing)
  result.Forbidden    -> AccessDenied                    (existing)
  result.TooManyRequests -> throw new HttpRequestException("Too many requests.", null, HttpStatusCode.TooManyRequests)   NEW, before !Success
  !result.Success     -> throw "Could not load users."   (existing)

UsersController.ChangeRole:
  ... Success / Conflict / Errors / NotFound (existing order)
  else if (result.TooManyRequests) TempData["Error"] = ApiRateLimit.Message;   NEW, before the generic "Could not change role."

ProductsController.AddErrors:
  if (result.TooManyRequests) ModelState.AddModelError(string.Empty, ApiRateLimit.Message);   NEW (after Conflict branch)
```

Covered call sites of `AddErrors`: Create, Edit, DeleteConfirmed, UploadImage, RemoveImage. GET reads (Index list, Details/Edit/Delete `GetProductAsync`, `GetAllCategoriesAsync`, `GetCategoryAsync`, `Image`) already throw `HttpRequestException` with `StatusCode` 429 and are handled by the filter without code changes.

## Tests

| Where | Case | Expect |
|---|---|---|
| `UsersTests` | `GET api/users` 429 | 429, message, Retry link, no stack text |
| `UsersTests` | `PUT api/users/1/role` 429 (page 2, search `al`) | redirect to `/Users?page=2&search=al`; list alert-danger = message |
| `ProductsTests` | create, edit, delete `...` 429 (3 tests) | 200 re-render; `FieldError(html, "")` = message |
| `ProductImageTests` | upload 429, remove image 429 | Details re-rendered with the message |
| `TooManyRequestsTests` | `GET api/product` list, `GET api/product/{id}` (Details), `GET api/category` for Create form | 429 page |
| `TooManyRequestsTests` | 404 on Details still NotFound; 503 still service page | unchanged |
| `TooManyRequestsTests` | page has no `HttpRequestException` text | |
| existing | Account 429, Cart/Inventory 429 tests | unchanged and green |
