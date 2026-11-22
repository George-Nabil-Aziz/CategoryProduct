# Contract: Users pages and API 401

## Client

```csharp
// before
public async Task<PagedResult<UserItem>> GetUsersAsync(int page, int pageSize = 10, string? search = null)
// after
public Task<ApiResult<PagedResult<UserItem>>> GetUsersAsync(int page, int pageSize = 10, string? search = null) =>
    GetAsync<PagedResult<UserItem>>(
        $"api/users?page={page}&pageSize={pageSize}&search={Uri.EscapeDataString(search ?? "")}");
```

The request is identical (GET, same URL, `Authorization: Bearer <access_token>` through `GetAsync<T>`/`ApplyBearerToken`). `ToResultAsync<T>` maps: 2xx `Success` + `Value`; 404 `NotFound`; 403 `Forbidden`; 429 `TooManyRequests`; 401 `Unauthorized`; 409 `Conflict`; 400 `Errors`; other failures call `EnsureSuccessStatusCode` (throws). 5xx and connection failures never reach it (`ApiErrorHandler` throws `ApiUnavailableException`, shown as the 503 page).

## Controller (`UsersController`, `[Authorize(Roles = "Admin")]` unchanged)

```text
Index(page, search):
  result = GetUsersAsync(Math.Max(page,1), search: search)
  result.Unauthorized -> SessionExpiredAsync()                      // returnUrl = Request.Path + Request.QueryString
  result.Forbidden    -> RedirectToAction("AccessDenied", "Account")
  !result.Success     -> throw new HttpRequestException(...)        // as EnsureSuccessStatusCode did
  else                -> same ViewBag (Search, CurrentPage, TotalPages) and View(result.Value!.Items)

ChangeRole(id, role, page, search):
  result = ChangeUserRoleAsync(id, role)
  result.Unauthorized -> SessionExpiredAsync(Url.Action(nameof(Index), new { page, search }))
  otherwise the existing chain (Success / Conflict / Errors / NotFound / "Could not change role.")

SessionExpiredAsync(string? returnUrl = null):
  await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme)
  return RedirectToAction("Login", "Account", new { returnUrl = returnUrl ?? Request.Path + Request.QueryString })
```

Same sign-out and redirect effect as `CategoriesController.SessionExpiredAsync`. The `Forbidden` and `!Success` handling may be inline or a small private helper; the behavior is normative, the layout is not.

## Tests

| Where | Case | Expect |
|---|---|---|
| `UsersTests` | Admin, `GET api/users` 401 to the bearer, `POST api/account/refresh` 401, `GET /Users?page=2&search=al` | redirect to `/Account/Login` with `returnUrl` = `/Users?page=2&search=al`; refresh called once; cookie cleared (same client anonymous: `GET /Users` redirects to login and makes no API call) |
| `UsersTests` | `GET api/users` 401 to `Bearer <old>`, refresh 200 with new tokens | 200 page; two `api/users` calls (old, then new bearer); one refresh |
| `UsersTests` | Admin, `PUT api/users/1/role` 401, refresh 401, form posted with `page=2`, `search=al` | redirect to login with `returnUrl` = `/Users?page=2&search=al`; cookie cleared; one refresh |
| `UsersTests` | `PUT api/users/1/role` 401 to the old bearer, refresh 200 | retried once with the new bearer; redirect to `/Users?page=2&search=al` (no login redirect) |
| `UsersTests` | `GET api/users` 403 | redirect to `/Account/AccessDenied` |
| `UsersTests` | `GET api/users` 404 | an exception surfaces (`HttpRequestException` through the test client), as before |
| `SessionExpiryTests` | new Theory row `Users GET Index` (`/Users?page=2`, route `api/users`) | same two Theory assertions as the other rows (its `returnUrl` equals the requested URL; a `ChangeRole` row does not fit the Theory because its `returnUrl` is the list URL) |
| existing `UsersTests` | list, search, paging, role change ok/409/400/404, access control | unchanged and green |
