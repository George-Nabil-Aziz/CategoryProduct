# Feature Specification: MVC Bug Fixes (error page, Users 401, Products 409, logout bearer)

**Feature Branch**: `022-mvc-bugfixes`

**Created**: 2026-11-22

**Status**: Draft

**Input**: User description: "Fix the four MVC defects recorded as 'Open code defects' 1-4 in the Known Gaps of `specs/021-test-coverage/spec.md`: (1) no HomeController behind `UseExceptionHandler(\"/Home/Error\")`, (2) the Users pages do not handle an API 401, (3) `ProductsController.AddErrors` has no Conflict branch, (4) logout sends no bearer token. Each fix gets tests in `CategoryProduct.MVC.Tests`. No API, schema or dependency change."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A real error page outside Development (Priority: P1)

When an unhandled exception happens in the MVC app in a non-Development environment (Docker compose, production), the visitor sees a short, friendly error page with HTTP status 500 and a link back to the categories, instead of a blank 404 and never any exception detail.

**Why this priority**: today `Program.cs` calls `UseExceptionHandler("/Home/Error")` outside Development, but no `HomeController` exists, so the re-executed request is a 404 with an empty body. `Views/Shared/Error.cshtml` is unreachable, and its text ("Development Mode ... ASPNETCORE_ENVIRONMENT") would be wrong for end users anyway.

**Independent Test**: run `ErrorPageTests` (new): the MVC host runs with environment `Production`, a scripted API call throws an unexpected exception, and the response is 500 with the friendly page and no stack trace.

**Acceptance Scenarios**:

1. **Given** environment `Production` and an unhandled exception in any controller action, **When** a page is requested, **Then** the response status is 500, the body is the layout plus the friendly message and a link to `/Categories`, and it contains neither the exception message, the exception type name, nor a stack trace.
2. **Given** the same setup, **Then** the page shows a Request ID (`Activity.Current?.Id ?? HttpContext.TraceIdentifier`), and the response carries `Cache-Control` with `no-store`.
3. **Given** an unhandled exception during a POST (antiforgery-protected form), **Then** the result is the same 500 page (the handler re-executes the path for any HTTP method).
4. **Given** an anonymous visitor (no cookie), **Then** `/Home/Error` renders without a login redirect (`[AllowAnonymous]`), and a signed-in visitor sees the normal layout with their name.
5. **Given** `GET /Home` or `GET /Home/Index`, **Then** the response is a redirect to the Categories index (`/Categories`).
6. **Given** environment `Development` (the default of every other test class), **Then** nothing changes: no exception handler is registered by `Program.cs`.

---

### User Story 2 - Users pages react to an expired session (Priority: P1)

An admin whose access token was rejected by the API (401, and the refresh attempt failed) on `/Users` or on a role change is signed out and sent to the login page with a `returnUrl`, exactly like on Categories, Products, Inventory, Cart and Orders. Today `/Users` throws an unhandled exception and a role change shows "Could not change role.".

**Why this priority**: it is the only API-backed controller without the session-expired handling, and the unhandled exception becomes the 500 page of story 1.

**Independent Test**: run the new 401 tests in `UsersTests` (and the new Users rows of `SessionExpiryTests`).

**Acceptance Scenarios**:

1. **Given** a signed-in Admin and `GET api/users` answering 401 to the bearer and `POST api/account/refresh` answering 401, **When** `/Users?page=2&search=al` is requested, **Then** the response is a redirect to `/Account/Login` with `returnUrl=/Users?page=2&search=al`, the auth cookie is cleared, and exactly one refresh call was made.
2. **Given** the same 401 but a successful refresh, **Then** the original `GET api/users` is retried once with the new bearer and the page renders (existing `TokenRefreshHandler` behavior, now covered for Users).
3. **Given** `PUT api/users/{id}/role` answering 401 (refresh fails), **When** the role-change form is posted from page 2 with search `al`, **Then** the user is signed out and redirected to login with `returnUrl=/Users?page=2&search=al` (the list URL, not the POST-only `/Users/ChangeRole`, which would be a 404 after login).
4. **Given** `GET api/users` answering 403, **Then** the response is a redirect to `/Account/AccessDenied`; **Given** any other non-success status that is not 401/403, **Then** an exception still surfaces as before (5xx and connection failures keep producing the 503 page through `ApiErrorHandler` / `ApiUnavailableFilter`).
5. **Given** the existing Users scenarios (list, search, paging, role change success, 409, 400, 404, access control), **Then** they pass unchanged.

---

### User Story 3 - Products show API 409 details (Priority: P1)

When the API answers 409 on product create, edit, delete, image upload or image removal, the page shows the API detail text in the validation summary, like Categories does. Today the form re-renders with no message at all.

**Why this priority**: the user sees a silent failure; FK/duplicate/stock conflicts are real API responses (for example deleting a product that is referenced by orders).

**Independent Test**: run the Products and ProductImage tests that script a 409.

**Acceptance Scenarios**:

1. **Given** an Admin and `POST api/product` answering 409 with detail "Product name already exists.", **When** the create form is posted, **Then** the page re-renders (200) and the summary contains that detail.
2. **Given** the same for `PUT api/product/{id}` (edit), `DELETE api/product/{id}` (the Delete page re-renders with the message), `POST api/product/{id}/image` and `DELETE api/product/{id}/image` (Details re-renders with the message).
3. **Given** a 409 whose body has no detail or is not JSON, **Then** the generic text "The request conflicts with existing data." is shown (this text comes from `CategoryProductApiClient`, unchanged).
4. **Given** other statuses (400, 401, 403, 404), **Then** behavior is unchanged.

---

### User Story 4 - Logout presents the access token without triggering a refresh (Priority: P2)

On logout the MVC sends the current access token as bearer in addition to the refresh token in the body, without ever refreshing tokens as a side effect. The sign-out always completes.

**Why this priority**: the API logout is anonymous today, so users notice nothing, but a future authenticated logout (or audit that needs the caller) would break. The fix must not introduce a regression: `TokenRefreshHandler` refreshes any request that carries a bearer when the token expires within 60 s or when the API answers 401, and a refresh rotates the refresh token, so a naive "just add the bearer" would revoke the old token, issue a new one, and then post the OLD refresh token to logout (already revoked, so a no-op), leaving the new refresh token valid server side.

**Independent Test**: run the Logout tests in `AccountTests` and the new skip-refresh tests in `TokenRefreshHandlerTests`.

**Acceptance Scenarios**:

1. **Given** a signed-in user with access token `tok-1` and refresh token `ref-1`, **When** Logout is posted, **Then** the API receives `POST api/account/logout` with `Authorization: Bearer tok-1` and body `{"refreshToken":"ref-1"}`, the cookie is cleared and the redirect goes to `/Categories`.
2. **Given** an access token that expires in less than 60 s, **When** Logout is posted, **Then** NO call to `POST api/account/refresh` is made, the logout call carries the (soon-expiring) token, and the user is signed out.
3. **Given** the API answers 401 to the logout call, **Then** exactly one logout call is made (no refresh, no retry) and the user is signed out.
4. **Given** the API is unreachable or answers 5xx to logout, **Then** the user is still signed out and no error page appears (existing behavior kept).
5. **Given** no refresh token in the cookie, **Then** no API call is made (existing behavior kept).
6. **Given** a request without the new marker, **Then** `TokenRefreshHandler` behaves exactly as before (all existing handler tests pass unchanged).

---

### User Story 5 - Known Gaps reflect the fixes (Priority: P3)

`specs/021-test-coverage/spec.md` Known Gaps items 1-4 are marked fixed by 022; items 5 and 6 stay. The 021 text that states the old logout behavior is annotated.

**Independent Test**: grep `022-mvc-bugfixes` in the 021 spec.

**Acceptance Scenarios**:

1. **Given** the 021 spec, **Then** items 1-4 start with "FIXED by `022-mvc-bugfixes`" and keep the original description; items 5 and 6 are unchanged.
2. **Given** the 021 sentences "(and no bearer)" (US2 scenario 4) and FR-007's "none on ... logout", **Then** each carries a short note that 022 changed it.

---

### Edge Cases

- The exception handler middleware re-executes `/Home/Error` with the original method (POST stays POST), so `Error` must accept any verb: no `[HttpGet]`.
- `Error` sets status 500 itself so a direct request to `/Home/Error` also reports 500 (assumption; harmless, nothing links to it).
- If the exception happens after the response has started, the middleware cannot render the page; that is framework behavior and not tested.
- `Response.StatusCode` is reset to 500 by the middleware before re-execution; the action sets it explicitly anyway so it does not depend on that detail.
- The Request ID is shown to help support; it is not sensitive (a trace identifier).
- Logout with an already expired access token still sends it; the API logout is anonymous, so it succeeds.
- Logout request carries the marker option, so `TokenRefreshHandler` neither refreshes before nor retries after, even on 401.
- A Users role change when the user list page and search are both default: `returnUrl` is `/Users?page=1` (the `search` parameter is omitted when null by `Url.Action`).
- `Views/Home/Index.cshtml` and `Views/Home/Privacy.cshtml` are unreferenced template leftovers; `HomeController.Index` does not render a view.
- Tests for the error page run the host as `Production`; `Program.cs` then also calls `UseHsts` only when `Hosting:UseHttpsRedirection` is true, which `MvcFactory` sets to false, so no HTTPS redirect interferes.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A new `CategoryProduct.MVC/Controllers/HomeController.cs` MUST define `Index` (redirects to `Categories`/`Index`) and `Error` (`[AllowAnonymous]`, `[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]`, no HTTP verb restriction, status 500, returns the `Error` view with `ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier }`).
- **FR-002**: `CategoryProduct.MVC/Views/Shared/Error.cshtml` MUST drop the "Development Mode" text and instead show a short friendly message, the Request ID when present, and a link back to Categories; it MUST NOT render exception details.
- **FR-003**: The unreferenced `Views/Home/Index.cshtml` and `Views/Home/Privacy.cshtml` template views MUST be removed (see Assumptions), and no `Program.cs` change is needed (`UseExceptionHandler("/Home/Error")` and the default route stay).
- **FR-004**: `CategoryProductApiClient.GetUsersAsync` MUST return `ApiResult<PagedResult<UserItem>>` using the existing private `GetAsync<T>` helper (same URL, same bearer), so 401, 403, 404, 429 and 400 are reported through the existing flags; 5xx and connection failures still throw via `ApiErrorHandler`.
- **FR-005**: `UsersController.Index` and `UsersController.ChangeRole` MUST handle `result.Unauthorized` through a `SessionExpiredAsync` helper identical in effect to the one in `CategoriesController` (sign out with the cookie scheme, redirect to `Account/Login` with `returnUrl`); `Index` MUST redirect to `Account/AccessDenied` on `Forbidden` and MUST throw `HttpRequestException` for any other non-success result (as `EnsureSuccessStatusCode` did).
- **FR-006**: `ChangeRole` MUST use the Users list URL (`page`, `search`) as `returnUrl`; `Index` MUST use `Request.Path + Request.QueryString` like the other controllers.
- **FR-007**: `ProductsController.AddErrors` MUST add `if (result.Conflict != null) ModelState.AddModelError(string.Empty, result.Conflict);` in the same position and form as `CategoriesController.AddErrors`.
- **FR-008**: `TokenRefreshHandler` MUST expose a public static `HttpRequestOptionsKey<bool>` (name `SkipRefreshOption`) and, when a request sets it to true, MUST pass the request straight to the inner handler: no pre-emptive refresh, no 401 refresh, no retry. Requests that do not set it MUST behave exactly as before.
- **FR-009**: `CategoryProductApiClient.LogoutAsync` MUST send the current access token (from the cookie principal, via the existing `ApplyBearerToken`) as bearer, set `SkipRefreshOption` to true on that request, keep the body `{ refreshToken }`, keep the early return for an empty refresh token and keep swallowing every exception (best effort).
- **FR-010**: `AccountController.Logout` MUST keep the same order (read the refresh token, call `LogoutAsync`, then sign out) and the same redirect.
- **FR-011**: Tests MUST be added or updated in `CategoryProduct.MVC.Tests` for every acceptance scenario above, using the existing `FakeApi`, `MvcTestBase`, `ApiJson` and `HtmlHelpers`. The existing 021 logout test that asserts no bearer MUST be updated to assert the bearer.
- **FR-012**: Running the MVC host as `Production` in tests MUST be possible without changing any other test class: `MvcFactory` gets an overridable environment name (default `Development`) and a new `ProductionMvcFactory` subclass supplies `Production`.
- **FR-013**: No schema change, no migration, no new runtime or test dependency, no secret, no change in `CategoryProduct.API`, `docker-compose.yml` or CI.
- **FR-014**: Known Gaps of `specs/021-test-coverage/spec.md` MUST be updated per User Story 5, and this spec's Known Gaps MUST be completed with the final facts.

### Key Entities

- **HomeController**: new MVC controller (`Index`, `Error`). See `contracts/error-page.md`.
- **ErrorViewModel**: existing model (`RequestId`, `ShowRequestId`); unchanged.
- **ApiResult / ApiResult&lt;T&gt;**: existing result types (`Success`, `NotFound`, `Unauthorized`, `Forbidden`, `TooManyRequests`, `Conflict`, `Errors`); unchanged.
- **SkipRefreshOption**: new request option marking calls `TokenRefreshHandler` must not touch. See `contracts/logout-bearer.md`.
- **ProductionMvcFactory**: test host running the real MVC app as `Production`. See `data-model.md`.

## Success Criteria *(mandatory)*

- **SC-001**: `dotnet build CategoryProduct.slnx` and `dotnet test CategoryProduct.slnx` pass locally with the new tests included (no Docker, SQL Server or network), and the count of tests is higher than the 021 baseline of 490 (278 MVC, 212 API).
- **SC-002**: In `ErrorPageTests`, a request that hits an unhandled exception under `Production` returns 500 with the friendly page, and the body contains none of the exception message, the exception type name or `   at ` stack lines.
- **SC-003**: Every one of the five Products 409 paths (create, edit, delete, upload, remove image) has a test asserting the API detail on the page.
- **SC-004**: A Users 401 (list and role change) has tests proving sign-out plus login redirect with the expected `returnUrl`, and a test with a successful refresh proving the transparent retry.
- **SC-005**: The logout tests prove the bearer is sent, that no `api/account/refresh` call happens for an expiring token or a 401 answer, and that the logout call count is exactly one.
- **SC-006**: Changes outside `CategoryProduct.MVC`, `CategoryProduct.MVC.Tests` and `specs/` are zero.

## Out of Scope

- **The seeded `testuser`** (an API migration seeds a known-password user; see the 021 spec Assumptions): intentionally NOT touched here; it stays an open Known Gap of its original spec.
- Changing the API logout endpoint to require authentication (this feature only prepares the MVC side).
- Making the API ignore or validate the logout bearer.
- Handling 429 on the Users and Products pages (see Known Gaps).
- A custom 404/403 status page (`UseStatusCodePages`), localization, logging dashboards.
- A browser-level check of the error page; tests assert server-rendered HTML only.

## Assumptions

- **Fix 1 test seam is the FakeApi, not a test-only endpoint or filter**: `FakeApi.Throw(HttpMethod, path, Exception)` makes the real `CategoriesController.Index` fail with an unexpected exception (`InvalidOperationException` carrying a unique marker text; `ApiErrorHandler` converts only `HttpRequestException` and timeouts), which exercises the real controller, filters and exception middleware without adding any production surface. A test-only throwing endpoint was rejected (it would need an IStartupFilter or MVC filter registration only for tests). The exception text and type name are the markers the test asserts are absent.
- **`Production` environment via an overridable name**: `MvcFactory` is `sealed` and calls `builder.UseEnvironment("Development")`; it becomes non-sealed with `protected virtual string EnvironmentName => "Development"`, and `ProductionMvcFactory : MvcFactory` overrides it with `"Production"`. `ErrorPageTests` derives from `MvcTestBase` and takes `ProductionMvcFactory` in its constructor plus `IClassFixture<ProductionMvcFactory>`. `WebApplicationFactory` builds its host lazily, so the unused base-class `MvcFactory` fixture that `MvcTestBase` declares never starts a host.
- **`Error` returns 500 and ignores the HTTP verb**: needed for the middleware re-execution of POSTs; a direct `GET /Home/Error` therefore also answers 500 (acceptable, unlinked).
- **Dead template views are deleted** (`Views/Home/Index.cshtml` shows "Welcome Teda"; `Privacy.cshtml` is the template stub): `grep` finds no reference besides `Program.cs`'s exception path; YAGNI. If a reviewer prefers to keep them, `HomeController.Index` still redirects and nothing else changes.
- **`Index` redirects with a temporary redirect** (`RedirectToAction("Index", "Categories")`), the same as `AccountController` does after login and logout.
- **Users `Forbidden` goes to AccessDenied**: before, a 403 from the API threw `HttpRequestException`; a stale admin cookie against a changed API role is the only realistic cause, and the redirect matches what `[Authorize(Roles = "Admin")]` does. Other non-success statuses (404, 429, 400) keep throwing, as before, and now end on the story-1 error page in non-Development.
- **`ChangeRole` 401 uses the list URL as `returnUrl`** (`Url.Action(nameof(Index), new { page, search })`); the other controllers use the POST path, which is only correct when a GET exists at that path (a Known Gap for Cart and Orders POSTs, not fixed here).
- **Marker is an `HttpRequestOptionsKey<bool>`**, not a new `DelegatingHandler` or named client: smallest change that fits the existing handler and keeps the call inside `ApiErrorHandler` (so a 5xx or connection failure is still converted and then swallowed by `LogoutAsync`). Rejected: logging out through the unauthenticated refresh client (`api-refresh`) with a manually added bearer: it skips `ApiErrorHandler` and splits the API surface of `CategoryProductApiClient`.
- **Bearer for logout is whatever the cookie holds**, expired or not; the API logout is anonymous, so an expired token cannot make logout fail.
- **No change to the API**: the existing API `Logout` stays anonymous and idempotent; `Refresh` reuse detection is untouched.
- **Unit tests for the new option** are added to `TokenRefreshHandlerTests` using the existing `StubHandler`/rig pattern.

## Known Gaps

- **CI verified**: the run for commit `e759778` passed all four jobs (`build-test`, `docker-build`, `sqlserver-tests`, `compose-smoke`) on 2026-11-22. Local: 514 tests (302 MVC, 212 API) green on 15 consecutive full-suite runs during review; the single unidentified MVC failure noted below was not reproduced.

- Defects 1-4 are fixed by the code changes of this feature; CI is green (see above, task T031 done).
- **Final local facts**: `dotnet build CategoryProduct.slnx` has 0 warnings and 0 errors. `dotnet test CategoryProduct.slnx` runs 514 tests, all passing: 302 in `CategoryProduct.MVC.Tests` (278 before, 24 net new) and 212 in `CategoryProduct.API.Tests` (unchanged). The five Products 409 tests (create, edit, delete, upload, remove image) failed on 7 cases with the `AddErrors` branch removed, so they detect the defect. Repeated full runs: 4 green, 6 further green, plus 25+ green MVC-only runs; one MVC run in an early repeat batch failed one test that could not be identified or reproduced afterwards (about 1 in 40 runs observed, output was not captured) and is recorded here as an open flakiness note.
- **Deviations found while implementing**: (a) `HomeController.Index` and the error-page link resolve to `/` (default route `Categories/Index`), so the tests assert Location `/` and `<a href="/">Back to Categories</a>` instead of `/Categories`. (b) A Users API 404 under the Development test host ends as the developer exception page (HTTP 500, text "Could not load users."), not as an exception thrown through the test client, so the test asserts status 500 plus that text. (c) The xunit double-fixture assumption (research R7) held: `ErrorPageTests : MvcTestBase, IClassFixture<ProductionMvcFactory>` works, no fallback was needed.
- **Seeded `testuser`**: still seeded by an API migration (see Out of Scope); a candidate for its own follow-up feature.
- **429 on Users and Products**: `UsersController` and `ProductsController` do not translate `TooManyRequests` into a message; a throttled call shows the generic failure text or, for the Users list, an exception page. Not part of the four defects.
- **POST `returnUrl` after session expiry** in `CartController`, `OrdersController` and the `ProductsController` image actions is the POST path (for example `/Cart/Add`), which has no GET and 404s after login. Only `UsersController.ChangeRole` is built correctly here; the others are left as they are.
- **Error page under Development** is unchanged: no handler is registered, so developers still see the raw exception (by design).
- **A direct visit to `/Home/Error` answers 500** with the error page; it is not linked anywhere.
- **No browser-level check**: the error page, the redirect and the logout are verified through server-rendered HTML and recorded API calls only.
- The API logout endpoint ignores the bearer; the bearer is a forward-compatible addition, so nothing server-side proves it is read.
- Items 5 (public product GETs carry no bearer, by design) and 6 (route id mismatch not reachable through HTTP) of the 021 Known Gaps are not defects of this feature and stay as they are.
