# Research: MVC Bug Fixes

All statements below were checked against the code at commit `c3f767c`.

## R1. Why `/Home/Error` is a 404 today

`CategoryProduct.MVC/Program.cs` registers `app.UseExceptionHandler("/Home/Error")` when `!app.Environment.IsDevelopment()`; the only route is `{controller=Categories}/{action=Index}/{id?}`. `CategoryProduct.MVC/Controllers/` has `Account`, `Cart`, `Categories`, `Inventory`, `Orders`, `Products` and `Users` controllers, no `HomeController`. `Views/Home/` holds only `Index.cshtml` ("Welcome Teda" template text) and `Privacy.cshtml` (template stub); `Views/Shared/Error.cshtml` (template, with "Development Mode" paragraphs) is used by nothing. So after an unhandled exception the middleware re-executes `/Home/Error`, MVC finds no endpoint, and the client gets an empty 404.

**Decision**: add `HomeController` with `Index` and `Error`; do not change `Program.cs`.
**Alternatives**: `app.UseExceptionHandler(o => o.Run(...))` writing HTML inline (loses the layout, bypasses MVC views, rejected); point the handler at `/Categories/Error` (wrong owner); keep `/Home/Error` but only add the view (a view needs a controller action).

`ApiUnavailableFilter` (an MVC `IExceptionFilter`) already converts `ApiUnavailableException` to the 503 view, so the new page only receives truly unexpected exceptions and any exception thrown outside MVC (middleware, view rendering).

## R2. `HomeController` details and the dead views

- `Index`: redirect to `Categories`/`Index`. The default route sends `/` to Categories already, so `Index` is only reachable as `/Home` and `/Home/Index`; a redirect avoids two "home" pages.
- `Error`: `[AllowAnonymous]` (there is no global authorization filter today, so it documents intent and survives a future fallback policy), `[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]` (the attribute only writes `Cache-Control: no-store, no-cache` and `Pragma: no-cache`; it needs no response-caching middleware), no `[HttpGet]` (the middleware re-executes with the original method, so a failed POST arrives here as POST).
- Status: the exception handler middleware sets 500 before re-executing the path. The action sets `Response.StatusCode = 500` too; the cost is that a direct visit to `/Home/Error` also answers 500.
- Request ID: `Activity.Current?.Id ?? HttpContext.TraceIdentifier`; `ErrorViewModel.ShowRequestId` already exists.
- View lookup: `View("Error", model)` from `HomeController` searches `Views/Home/Error.cshtml` then `Views/Shared/Error.cshtml`; the shared one is found.
- `Views/Home/Index.cshtml` and `Privacy.cshtml`: `grep` over `*.cshtml`, `*.cs` and `*.json` in `CategoryProduct.MVC` finds no reference to them (the layout links only Categories, Products, Cart, Orders, Users, Inventory, Manage Orders, Account). Deleting them avoids leaving views that look live. Recorded as an assumption in `spec.md`; harmless to keep if preferred.

**Error.cshtml content** (no exception data ever, the view model has none): title "Error", heading "Something went wrong", one sentence "An unexpected error occurred while processing your request. Please try again.", `Request ID` line only when `Model.ShowRequestId`, and a `btn btn-primary` link "Back to Categories" (`asp-controller="Categories" asp-action="Index"`). The "Development Mode" block is removed completely.

## R3. Logout bearer and `TokenRefreshHandler` (the key decision)

What the handler does (`CategoryProduct.MVC/Services/TokenRefreshHandler.cs`):

1. If there is no `HttpContext`, no `Authorization` header on the request, or the user is not authenticated, it passes the request through untouched. Today logout sends no bearer, so it takes this path.
2. Otherwise it buffers the content; if the access token expires within 60 s (claim `access_token_expires_at`), it calls `TryRefreshAsync` and swaps the bearer; it sends; on 401 it calls `TryRefreshAsync` again (once per HTTP context, guarded by `HttpContext.Items["refresh_tried"]`) and retries with the new bearer.
3. `TryRefreshAsync` posts the cookie's `refresh_token` to `api/account/refresh` and, on success, calls `AuthSessionService.SignInAsync`, which re-issues the cookie principal with the new tokens.

What the API does (`CategoryProduct.API/Controllers/AccountController.cs`):

- `Refresh`: unknown hash gives 401; a revoked token that was replaced less than `Jwt:RefreshReuseGraceSeconds` (default 10) ago gives 401 without side effects, otherwise (reuse) the whole family is revoked and 401 is returned; a valid token is revoked, replaced by a new one (`IssueAsync(user, familyId, stored)`) and the new pair returned. So a refresh ROTATES the refresh token.
- `Logout` (`[EnableRateLimiting("logout")]`, no `[Authorize]`): looks the posted refresh token up by hash; only if it exists and `RevokedAtUtc == null` it is revoked; always `204 No Content`.

Failure mode of the naive change ("just add the bearer in `LogoutAsync`"): `AccountController.Logout` (MVC) reads `User.FindFirst("refresh_token")` BEFORE calling `LogoutAsync`. If the handler then refreshes (token within 60 s of expiry, or the API answers 401), the API revokes `ref-1` and issues `ref-2`; the MVC still posts `ref-1` to logout. The API finds `ref-1`, sees it is already revoked and does nothing. The cookie is cleared, but `ref-2` stays valid server side until it expires (the refresh-token lifetime), which defeats logout. A retry of the logout after a refresh could also fall into the reuse branch if timing exceeded the grace window.

**Decision**: attach the bearer for this one call and mark the request so the handler skips all refresh logic.

- `TokenRefreshHandler` gets `public static readonly HttpRequestOptionsKey<bool> SkipRefreshOption = new("cp.skip-refresh");` and `SendAsync` starts with `if (request.Options.TryGetValue(SkipRefreshOption, out var skip) && skip) return await base.SendAsync(request, ct);`.
- `CategoryProductApiClient.LogoutAsync` builds an `HttpRequestMessage`, calls the existing private `ApplyBearerToken(request)` (reads the current `access_token` claim), sets the option and sends it through the existing `_http` pipeline, inside the existing `try/catch (Exception)`.
- Expired token: still sent. The API logout is anonymous, so no 401 can occur from the token; and if it did occur, the skip option means no refresh and no retry, and the MVC signs the user out anyway.
- `ApiErrorHandler` stays in the pipeline (outer handler), so a 5xx or connection failure is converted to `ApiUnavailableException` and swallowed by `LogoutAsync`'s catch, exactly as in 021 (`Logout_when_api_500_still_signs_out`).

**Alternatives rejected**:

- Send logout through the named refresh client (`api-refresh`, no handlers) with a manually added bearer: bypasses `ApiErrorHandler`, adds a second way to call the API from `CategoryProductApiClient`, and `IHttpClientFactory` is not injected there.
- A boolean parameter or a second method on `TokenRefreshHandler`: handlers only see `HttpRequestMessage`; `HttpRequestOptions` is the supported per-request channel.
- Detect the logout URL inside the handler (`request.RequestUri.AbsolutePath.EndsWith("account/logout")`): hidden coupling by string, rejected.
- Refresh first, then post the NEW refresh token: two API calls, and a failed refresh would still need a fallback; more moving parts than skipping.
- Do nothing (keep no bearer): leaves defect 4 open; the spec asks for the bearer.

**Why the bearer at all**: the API logout ignores it today; sending it keeps the MVC correct if the endpoint becomes `[Authorize]` and lets API logs/audit correlate the caller. If that happens later, an expired access token would then fail logout; revisit then (Known Gaps of `spec.md`).

## R4. Existing tests that interact with this change

- `AccountTests.Logout_clears_cookie_and_revokes_refresh_token` currently asserts `Assert.Null(call.Authorization)` (line 232). It becomes `Assert.Equal("Bearer <token passed to SignInUserAsync>", call.Authorization)` (the test must pass an explicit `token:` to `SignInUserAsync`, whose default token is generated by `ApiJson.Login`).
- `AccountTests.Logout_when_api_unreachable_still_signs_out`, `Logout_when_api_500_still_signs_out` and `ApiUnavailableTests.Logout_with_api_down_still_signs_out` stay valid unchanged (they do not assert the header).
- `TokenRefreshHandlerTests` has `No_bearer_passes_through_untouched` and the bearer/refresh cases; they do not set the option and must pass unchanged (spec scenario 4.6).
- 021 documents: `specs/021-test-coverage/spec.md` US2 scenario 4 ("(and no bearer)"), FR-007 ("none on ... logout") and `contracts/mvc-test-matrix.md` (Logout rows "no Authorization") describe the old behavior and get a short "changed by 022" note (tasks T025 and T026).

## R5. Users 401: client and controller

Current code: `UsersController.Index` calls `_api.GetUsersAsync(...)`, which builds the request by hand, applies the bearer, sends and calls `response.EnsureSuccessStatusCode()`, so a 401 (after `TokenRefreshHandler` already failed to refresh) throws `HttpRequestException` and surfaces as an unhandled exception. `ChangeRole` uses `ChangeUserRoleAsync` (`SendAsync(HttpMethod.Put, ...)` returning `ApiResult` with `Unauthorized`), but the controller never checks `Unauthorized`, so it shows "Could not change role.".

The private helper `GetAsync<T>` already does the same request (GET with bearer) and maps statuses via `ToResultAsync<T>` (Success/NotFound/Forbidden/TooManyRequests/Unauthorized/Conflict/BadRequest; other failures call `EnsureSuccessStatusCode`). Cart and Orders use it. `GetUsersAsync` therefore becomes a one-liner over `GetAsync<PagedResult<UserItem>>` with the identical URL (`api/users?page={page}&pageSize={pageSize}&search={escaped}`), which keeps the existing tests' query assertion (`?page=1&pageSize=10&search=`) valid.

`CategoriesController.SessionExpiredAsync` signs out with `CookieAuthenticationDefaults.AuthenticationScheme` and redirects to `Account`/`Login` with `returnUrl = Request.Path + Request.QueryString`. `UsersController` gets the same helper plus an optional `returnUrl` argument for `ChangeRole`, whose request path (`/Users/ChangeRole`) has no GET. `Url.Action(nameof(Index), new { page, search })` yields `/Users?page=2&search=al` (the test asserts the exact value; `search` is omitted when null).

`SessionExpiryTests` is a Theory whose assertion is `AssertLoginRedirect(resp, s.Url)` (return URL equals the requested URL): a `Users GET Index` row (`/Users?page=2`, route `api/users`) fits and is added; a `ChangeRole` row does not fit (its returnUrl is the list URL, not the POST URL), so `ChangeRole` gets dedicated tests in `UsersTests`.

Behavior kept: Index non-401 failure statuses still throw `HttpRequestException` (message "API returned ... for GET api/users"); 5xx and connection errors are converted by `ApiErrorHandler` first and shown as the 503 page. New: 403 redirects to AccessDenied.

## R6. Products 409

`CategoryProductApiClient.SendAsync(...)` already maps 409 to `ApiResult.Conflict` (detail text from the `ConflictProblem` body, or "The request conflicts with existing data."). `CategoriesController.AddErrors` writes `result.Conflict` to the model-level error; `ProductsController.AddErrors` handles Unauthorized, Forbidden and the `Errors` dictionary only. All five Products call sites (`Create`, `Edit`, `DeleteConfirmed`, `UploadImage`, `RemoveImage`) call `AddErrors(result)` for non-success, non-404, non-401 results, so one added line fixes all of them. Views: `Create`/`Edit`/`Delete` render `asp-validation-summary="ModelOnly"`, `Details` renders `asp-validation-summary="All"`; both show a model-level (empty key) error. Existing 021 tests `Create_post_api_409_rerenders_without_exception`, `Edit_post_api_409_...`, `Delete_post_api_409_...` (ProductsTests) and `Upload_api_409_...` (ProductImageTests) only assert status 200 and are strengthened; there is no RemoveImage 409 test yet, so one is added. `HtmlHelpers.FieldError(html, "")` reads the summary like the Categories tests do.

## R7. Running as `Production` in tests

`MvcFactory` is `sealed`, has `builder.UseEnvironment("Development")` and its summary says one instance per test class. Making it overridable (`protected virtual string EnvironmentName`) changes no existing test: they all use `MvcFactory` directly and get `Development`. `ProductionMvcFactory` reuses every setting (fake handler, temp Data Protection and log paths, `Hosting:UseHttpsRedirection=false`, `Cookie:SecurePolicy=SameAsRequest`).

Production-only effects verified in `Program.cs`: `UseExceptionHandler("/Home/Error")` is registered; `UseHsts` only when `Hosting:UseHttpsRedirection` is true (it is false in tests). `appsettings.json` has no environment-specific values that matter, and there is no `appsettings.Production.json`. `appsettings.Development.json` is simply not loaded; the test overrides come from `UseSetting`.

`MvcTestBase` is declared `IClassFixture<MvcFactory>`; a derived class that also declares `IClassFixture<ProductionMvcFactory>` gets both fixtures constructed by xunit, but `WebApplicationFactory` creates its host lazily on first use, so the unused `MvcFactory` instance starts nothing (it only owns the `FakeApi` object and temp paths it deletes in `Dispose`). Fallback if xunit ever complains: make `ErrorPageTests` not derive from `MvcTestBase` and copy the three helpers it needs.

## R8. How the test provokes an unhandled exception

`FakeApi.Throw(HttpMethod, path, Exception)` registers a route whose handler throws the given exception from the primary handler. `ApiErrorHandler` converts only `HttpRequestException` and non-cancelled `TaskCanceledException`; any other exception type passes through `CategoryProductApiClient` into `CategoriesController.Index` (`GetCategoriesAsync` uses `GetFromJsonAsync`, no catch), through MVC (`ApiUnavailableFilter` only handles `ApiUnavailableException`) and reaches `ExceptionHandlerMiddleware`. Using `new InvalidOperationException("secret-marker-9f3a")` gives a unique string to prove it is not leaked, and the type name `InvalidOperationException` plus the namespace text `CategoryProduct.MVC` with `   at ` frames are what a stack trace would contain.

**Alternatives rejected**: a test-only endpoint mapped through `IStartupFilter` or a test-only MVC filter registered in `ConfigureServices` (more test code, touches the pipeline order under test); calling `/Home/Error` directly only (proves the view, not the middleware wiring that was the real defect).

The POST variant: sign in as Admin, script `GET api/category` normally for the Create page token, then script `POST api/category` to throw; post the Create form; expect the same 500 page.

## R9. Decisions with no alternatives worth recording

- `HomeController.Index` returns a redirect rather than 404, so old bookmarks `/Home` keep working.
- Tests assert HTML by `HtmlHelpers.HasText` and `Contains` on decoded HTML; no new HTML-parser dependency (same as 021).
- No CI change: `build-test` already runs `dotnet test CategoryProduct.slnx`, which includes the MVC test project.
