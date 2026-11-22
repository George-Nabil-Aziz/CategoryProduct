# Feature Specification: Test Coverage (MVC tests, compose smoke, API gaps)

**Feature Branch**: `021-test-coverage`

**Created**: 2026-11-22

**Status**: Draft

**Input**: User description: "Close the Known Gaps of specs 013-020 that are about missing tests: the MVC app has no test project (and TokenRefreshHandler, AuthSessionService, logout, Register/Users/Inventory/Cart/Orders pages, image proxy were never exercised), docker compose has never been run in automation, and a few API paths (refresh edge cases, RefreshTokenCleanupWorker) have no test."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - MVC test project and fake API infrastructure (Priority: P1)

A developer runs `dotnet test CategoryProduct.slnx` and, besides the API tests, a new `CategoryProduct.MVC.Tests` project runs. It boots the real MVC app in memory (`WebApplicationFactory`) with the API replaced by a scriptable in-process fake, so every MVC controller, view, filter and HTTP handler can be driven with real cookies, real antiforgery tokens and real Razor rendering, without a running API or database.

**Why this priority**: every other MVC test depends on it, and `build-test` in CI picks it up only if it is part of `CategoryProduct.slnx`.

**Independent Test**: build the project and run its infrastructure self-tests: the factory boots, `GET /Account/Login` returns the form with an antiforgery token, a FakeApi route answers, an unscripted API call fails loudly, requests are recorded, and a sign-in through the real login form yields an authenticated cookie.

**Acceptance Scenarios**:

1. **Given** the repo, **When** `dotnet test CategoryProduct.slnx` runs, **Then** the MVC test project is built and its tests run in the same invocation as the API tests.
2. **Given** a test scripts `POST api/account/login`, **When** the helper signs in through `/Account/Login`, **Then** the MVC returns a redirect, sets the auth cookie and the next page shows the user name and (for Admin) the admin nav links.
3. **Given** the MVC calls an API route nobody scripted, **Then** the test fails with a message naming the method and path (no silent 404).
4. **Given** a test asserts on recorded requests, **Then** it can read method, path, query, `Authorization` header and body (JSON or multipart) of each API call the MVC made, in order.

---

### User Story 2 - Account, anonymous access and session behavior (Priority: P1)

Login, registration, logout, access-denied, redirects for anonymous users, token refresh and session expiry are verified end to end through the MVC, plus unit tests of `TokenRefreshHandler`, `ApiErrorHandler` and `ApiUnavailableFilter`.

**Why this priority**: this is the riskiest MVC code (it handles tokens) and Known Gaps 013, 014, 015 and 020 name it as untested.

**Independent Test**: run the Account, AnonymousAccess, SessionExpiry, ApiUnavailable and handler test classes only.

**Acceptance Scenarios**:

1. **Given** valid credentials scripted for a User, **When** the login form is posted, **Then** the response redirects to `/Categories` (or a local `returnUrl`), an external `returnUrl` is ignored, and no API token appears in the HTML.
2. **Given** the API answers 401 / 429 to login, **Then** the login page re-renders with "Invalid username or password." / "Too many login attempts. Try again in a minute." and no auth cookie is set.
3. **Given** a valid registration, **Then** the MVC calls register then login and signs the user in; **Given** 409 **Then** "Username already taken." is shown; **Given** 429 **Then** the too-many-attempts message is shown; invalid input (mismatched passwords) is re-rendered without calling the API.
4. **Given** a signed-in user, **When** Logout is posted, **Then** the API receives `POST api/account/logout` with the refresh token (and no bearer), the cookie is cleared and the redirect goes to `/Categories`; **Given** the logout call throws, **Then** the user is still signed out.
5. **Given** an anonymous visitor, **When** requesting any `[Authorize]` page (Cart, Orders, Orders/Manage, Inventory, Users, any Create/Edit/Delete), **Then** the response is a redirect to `/Account/Login?ReturnUrl=...`; **Given** a signed-in non-admin, **Then** admin-only pages redirect to `/Account/AccessDenied`, which renders.
6. **Given** an access token that expires in less than 60 seconds, **When** an authenticated API-backed page is requested, **Then** the MVC first calls `POST api/account/refresh`, then the page call carries the new bearer, and the auth cookie is re-issued with the new tokens.
7. **Given** the API answers 401 to a bearer call, **Then** the MVC refreshes once and retries the original call exactly once with the new token (same method, URL and body); **Given** the refresh also fails, **Then** the original 401 is surfaced, the user is signed out and redirected to login with a `returnUrl` (for every controller that handles `Unauthorized`).
8. **Given** the API is unreachable or returns 5xx, **Then** pages show the "Service unavailable" page with HTTP 503 instead of an exception.

---

### User Story 3 - Catalog pages: Categories, Products, images, Users (Priority: P1)

Public browsing, admin CRUD, the Edit page's read-only quantity, the image proxy, image upload/remove with API validation errors, and the Admin Users page are verified with happy and key error paths.

**Why this priority**: closes the 013 (Register/Users) and 017 (MVC image pages) gaps.

**Independent Test**: run the Categories, Products, ProductImages and Users test classes.

**Acceptance Scenarios**:

1. **Given** anonymous access, **When** listing/searching/paging Categories and Products and opening Details, **Then** pages render the scripted data, the query string sent to the API carries `page`, `search`, `categoryId`, price and sort parameters, public GETs carry no `Authorization`, and Create/Edit/Delete links and Add-to-cart forms are hidden; a signed-in User sees Add-to-cart; an Admin sees Create/Edit/Delete.
2. **Given** an Admin posts valid Create/Edit/Delete, **Then** the API receives the call with the bearer token and the MVC redirects to Index; **Given** the API returns 400 / 404 / 409 / 403, **Then** the form re-renders with the field or summary error (404 gives a Not Found response).
3. **Given** the Product Edit page, **Then** the Quantity input is `readonly` with the Inventory hint, the posted body keeps the unchanged quantity, and an API 400 on key `Quantity` ("Use stock movements to change quantity.") is displayed next to that field.
4. **Given** the image proxy `GET /Products/Image/{id}`, **Then** it returns the API bytes with the API content type, `X-Content-Type-Options: nosniff` and `Cache-Control: no-cache`, and 404 when the API returns 404; pages never contain the API host (`fake-api`).
5. **Given** an Admin uploads a file on Product Details, **Then** the API receives a bearer-authenticated multipart POST with the file name, content type and bytes; **Given** the API answers 400 (type/size) **Then** Details re-renders with the validation message; **Given** no file is chosen **Then** "Please choose an image file." is shown without an API call; Remove calls `DELETE api/product/{id}/image`; a non-admin is redirected to AccessDenied for both.
6. **Given** an Admin, **When** opening Users, **Then** the list, search and paging render; role change posts `PUT api/users/{id}/role` with the selected role and shows "Role updated."; 409 shows the API detail (last admin); a non-admin cannot reach the page.

---

### User Story 4 - Inventory, Cart, Orders and Order management (Priority: P1)

The 018 and 019 MVC pages are verified: admin-only Inventory with low-stock banner, history and movement form, and the signed-in cart/checkout/order lifecycle including admin management.

**Why this priority**: closes the 018 and 019 "MVC not run / no test project" gaps.

**Independent Test**: run the Inventory, Cart, Orders and ManageOrders test classes.

**Acceptance Scenarios**:

1. **Given** an Admin, **Then** Inventory lists products with a Low stock badge, the banner "N product(s) low on stock" appears only when the low-stock total is above 0, history pages and shows movements, and Add movement posts `quantityChange`, `reason`, `note` and redirects back with "Stock movement recorded."; API 409 / 400 messages appear as the error alert; a User is denied.
2. **Given** a signed-in User, **Then** the cart page shows lines, totals and the "Only N in stock" badge, shows "Your cart is empty." when empty, and Add / Update / Remove / Clear call the matching API routes with the bearer and flash the success or error message (including a 409 detail) on the next page; `returnUrl` is honored only when local.
3. **Given** Checkout succeeds, **Then** the MVC redirects to `/Orders/{id}` with "Order placed."; **Given** 409 (insufficient stock) **Then** the cart page shows the API detail; **Given** 400 (empty cart) **Then** "Your cart is empty." is shown.
4. **Given** "My orders", **Then** it pages and links to details; order Details shows items, status badge and the Cancel button only when Status is Pending; Cancel posts to the API and flashes "Order cancelled."; a missing order (API 404) gives Not Found.
5. **Given** an Admin on Manage Orders, **Then** the status filter (valid values only, case-insensitive; an unknown value means no filter) is sent to the API, Confirm and Cancel return to the same page and filter, an invalid transition (API 409) shows its detail, and non-admins are denied.

---

### User Story 5 - Compose smoke test in CI (Priority: P1)

A new CI job `compose-smoke` builds and starts the real stack (`docker compose up -d --build`) with throwaway generated secrets, waits for the API `/health` (Healthy, database check) and the MVC home page, prints logs on failure, and always tears the stack down.

**Why this priority**: 016 states Dockerfiles and compose were never run; `docker-build` only builds images.

**Independent Test**: push a branch and check that `compose-smoke` passes. Locally (Docker host): follow `quickstart.md`.

**Acceptance Scenarios**:

1. **Given** a clean runner, **When** the job runs, **Then** it creates `.env` from runtime-generated values, brings the stack up, `GET http://localhost:5028/health` returns 200 with `"status":"Healthy"` and a `database` check, and `GET http://localhost:5256/` returns 200 with MVC HTML (which proves MVC reached the API).
2. **Given** the stack never becomes healthy within the timeout, **Then** the job fails after printing `docker compose ps` and `docker compose logs`.
3. **Given** success or failure, **Then** `docker compose down -v` runs and no generated secret value is printed in the log.
4. **Given** the existing jobs, **Then** their steps are unchanged (`build-test` runs the new tests only because the project is in the solution).

---

### User Story 6 - Small API gaps (Priority: P2)

`RefreshTokenCleanupWorker` is tested without real waiting, and cheap InMemory-testable refresh/user gaps from 013/014 get tests, with no API behavior change.

**Independent Test**: run the new API test classes only.

**Acceptance Scenarios**:

1. **Given** the worker with a controllable clock, **When** the clock advances by the configured interval, **Then** old tokens are purged and nothing is purged before the interval; interval `0` or negative falls back to 60 minutes; a failing purge does not stop the worker; cancellation stops it cleanly.
2. **Given** an expired refresh token, a refresh token whose user was deleted, a body without `refreshToken`, and refresh calls above the permit limit, **Then** the API returns 401, 401, the real documented status, and 429 respectively; `GET api/users` without a token returns 401.

---

### User Story 7 - Known Gaps point at the new tests (Priority: P3)

The Known Gaps of specs 013, 014, 016, 017, 018, 019 and 020 mark the items covered here as `COVERED by 021-test-coverage` (pending the first CI run), in the same style 020 used.

**Independent Test**: grep the seven spec files for `021-test-coverage`.

**Acceptance Scenarios**:

1. **Given** each listed spec, **Then** every Known Gap item covered by this feature carries the 021 pointer and items still open stay as they were.

---

### Edge Cases

- A test class shares one `MvcFactory` (class fixture); each test gets a fresh `HttpClient` (new cookie jar) and the fake is reset, so tests never leak cookies or routes.
- Tests in different classes run in parallel; each class fixture has its own `FakeApi`, so there is no shared static state.
- The 60-second refresh window uses the real clock; tests use expiry values far from the boundary (30 s ahead vs 15 min ahead).
- HTML-encoded text: `You don't have permission...` renders as `&#x27;`; the helper decodes before asserting.
- A form post without an antiforgery token returns 400 (verified once per controller family, not per action).
- `Orders/Manage` filter value `bogus` must not be forwarded to the API.
- Redirect `Location` headers can be relative; assertions compare path and query, not host.
- Unhandled exceptions are not turned into an error page (see Known Gaps); tests never rely on that.
- The Users page does not handle API 401 (see Known Gaps); session-expiry tests cover the controllers that do.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A project `CategoryProduct.MVC.Tests` MUST exist (xunit, same package versions as `CategoryProduct.API.Tests`, `Microsoft.AspNetCore.Mvc.Testing` 10.0.10, no InMemory package), reference only `CategoryProduct.MVC`, and be listed in `CategoryProduct.slnx` so `dotnet test CategoryProduct.slnx` and CI `build-test` run it.
- **FR-002**: `CategoryProduct.MVC/Program.cs` MUST end with `public partial class Program { }` (the only production change in the MVC project) so `WebApplicationFactory<Program>` can use it.
- **FR-003**: `MvcFactory` MUST set `Api:BaseUrl=http://fake-api/`, `Hosting:UseHttpsRedirection=false`, `Cookie:SecurePolicy=SameAsRequest`, a temporary `DataProtection:KeysPath`, a temporary Serilog file path, and environment `Development`, and MUST register the `FakeApi` handler as primary handler for every `HttpClient` (typed `CategoryProductApiClient` and the named `TokenRefreshHandler.RefreshClientName`), keeping the real `ApiErrorHandler` and `TokenRefreshHandler` pipeline.
- **FR-004**: `FakeApi` MUST follow `contracts/fake-api.md`: route table by method + path, per-route static or sequenced or computed responses, simulated network failure, recording of every request, and a loud failure for unscripted calls.
- **FR-005**: Helpers MUST provide: GET page and read body, antiforgery token extraction, form POST with cookies and no auto-redirect, sign-in through the real `/Account/Login` for User and Admin, and minimal HTML assertions (string/regex over decoded HTML; no new HTML-parser dependency).
- **FR-006**: Tests MUST cover every action of `AccountController`, `CategoriesController`, `ProductsController`, `InventoryController`, `CartController`, `OrdersController`, `UsersController` for at least its happy path and each distinct error branch listed in `contracts/mvc-test-matrix.md`.
- **FR-007**: Tests MUST assert what the MVC sent to the API (bearer on every write and on Users, Inventory, Cart and Orders calls; none on public GETs and on login/register/logout/refresh; JSON bodies; multipart for image upload; unchanged quantity on product PUT).
- **FR-008**: Unit tests MUST cover `TokenRefreshHandler` (refresh before expiry within 60 s, single refresh and single retry on 401, failed refresh returns the original 401, unauthenticated requests are untouched, no second refresh in one request, cookie principal updated), `ApiErrorHandler` (HttpRequestException and non-cancelled timeout become `ApiUnavailableException`, 5xx becomes `ApiUnavailableException`, caller cancellation is not converted, 4xx passes through) and `ApiUnavailableFilter` (503 view with message).
- **FR-009**: No test MAY depend on network access, a database, Docker, or ordering between test classes; no fixed sleeps (poll an in-process condition with a timeout instead).
- **FR-010**: CI MUST gain job `compose-smoke` per `contracts/ci-compose-smoke.md`; existing jobs MUST NOT change.
- **FR-011**: The `.env` for compose in CI MUST be generated at runtime with random values, never committed, masked in logs, and removed with the stack at the end.
- **FR-012**: `RefreshTokenCleanupWorker` MUST get a test that does not wait for a real interval. The minimal justified change is an optional `TimeProvider` constructor parameter (default `TimeProvider.System`) used for the `PeriodicTimer` and the purge timestamp; behavior with the default MUST be identical.
- **FR-013**: New API tests MUST cover: refresh with an expired token, refresh for a deleted user, refresh with a missing `refreshToken` body (assert the real status), refresh rate limit 429, `GET api/users` without token 401; they MUST run on InMemory and not change API behavior.
- **FR-014**: Known Gaps of specs 013, 014, 016, 017, 018, 019 and 020 MUST be updated per `tasks.md` Phase 8.
- **FR-015**: No schema change, no migration, no new runtime dependency in the shipped apps.

### Key Entities

- **MvcFactory**: test host of the real MVC app with the fake API wired in. See `data-model.md`.
- **FakeApi**: scriptable `HttpMessageHandler` with request log. See `contracts/fake-api.md`.
- **RecordedRequest**: sequence number, method, path, query, Authorization, content type, body text and bytes.
- **MvcTestBase**: per-test base class (fresh client, reset fake, helpers).
- **ApiJson**: builders for the API JSON shapes the MVC deserializes (login result, paged result, category, product, cart, order, user, problem details).

## Success Criteria *(mandatory)*

- **SC-001**: `dotnet test CategoryProduct.slnx -c Release` passes locally and in `build-test` with the MVC tests included, without Docker, SQL Server or network.
- **SC-002**: Every public action of the seven MVC controllers is hit by at least one test (checked against `contracts/mvc-test-matrix.md`); the MVC suite has at least 120 tests and runs in under 90 seconds on a CI runner.
- **SC-003**: The first CI run of the branch shows `build-test`, `docker-build`, `sqlserver-tests` and `compose-smoke` green (recorded in Known Gaps like 020 did).
- **SC-004**: The seven older specs name `021-test-coverage` for every gap this feature closes.
- **SC-005**: Changes outside tests, specs and CI are limited to `public partial class Program { }` in MVC `Program.cs` and (if the worker approach is used) the optional `TimeProvider` parameter in `RefreshTokenCleanupWorker.cs`.

## Assumptions

- **Fake at the HTTP boundary, not at `CategoryProductApiClient`**: faking the HTTP handler keeps the real `ApiErrorHandler`, `TokenRefreshHandler`, JSON (de)serialization and status mapping under test; mocking the client would skip exactly the code the Known Gaps name.
- **`public partial class Program { }` in the MVC project**: same convention as the API (last line of `CategoryProduct.API/Program.cs`). The MVC test project does not reference the API project, so the two global `Program` types never meet in one compilation. `InternalsVisibleTo` was rejected as less conventional.
- **No HTML parser package**: forms are generated by tag helpers (`<input name="__RequestVerificationToken" type="hidden" value="...">`); a regex plus `WebUtility.HtmlDecode` is enough. AngleSharp would add a dependency for a handful of assertions (YAGNI). If a view's markup makes a string assertion fragile, add AngleSharp then and record it in `research.md`.
- **Environment `Development`** (the `WebApplicationFactory` default): `Program.cs` registers the exception handler only outside Development, and its target `/Home/Error` has no controller (Known Gap), so another environment would hide failures behind a 404.
- **One factory per test class, fresh client + reset fake per test**: balances speed (host starts once per class) and isolation.
- **Real clock**: `TokenRefreshHandler` reads `DateTime.UtcNow`; changing it to a `TimeProvider` is out of scope. Tests choose expiry 30 s (inside the 60 s window) or 15 min (outside) ahead.
- **Tokens are opaque strings** to the MVC (it never parses a JWT); fake logins return arbitrary strings with realistic `expiresAtUtc` values, which is the "JWT-like expiry" the handler actually uses.
- **Compose smoke scope**: health + MVC home only. It does not log in (the migration seeds a known-password user; using it would write that Known Gap into CI) and does not run browser flows.
- **Throwaway compose secrets**: `SA_PASSWORD` is `Ci-<hex>-Aa1` style (meets SQL Server complexity and avoids `; = $`, quotes and spaces as `.env.example` requires), `JWT_KEY` is 64 hex chars; both from `openssl rand`, masked with `::add-mask::`. The workflow-level literal used by `sqlserver-tests` is untouched.
- **Ports**: compose defaults 5028 (API) and 5256 (MVC) are free on `ubuntu-latest`.
- **Worker**: preferred approach is the optional `TimeProvider` parameter with `Microsoft.Extensions.TimeProvider.Testing` (`FakeTimeProvider`) added to `CategoryProduct.API.Tests` only; if that package cannot be restored, a short hand-written `TimeProvider` with a manual `ITimer` is the fallback. If the production change is rejected, the fallback test only proves start/stop and the interval gap stays documented.
- **Tests do not pin known bugs**: the missing `HomeController` and the Users 401 handling are documented as Known Gaps, not asserted.

## Known Gaps

- **No real browser or JavaScript**: only server-rendered HTML is tested. Client-side validation (`_ValidationScriptsPartial`), Bootstrap behavior, the `javascript:location.reload()` Retry link and file-picker UX are not covered. Tests post what a browser would post.
- **Compose smoke is verified only by its first CI run**: this machine has no Docker. Treat `compose-smoke` as unverified until `build-test`, `docker-build`, `sqlserver-tests` and `compose-smoke` are all green on GitHub; record the run in this section afterwards.
- **Found while writing this spec (not fixed here)**: (1) there is no `HomeController`, so `UseExceptionHandler("/Home/Error")` (non-Development) leads to a 404 and `Views/Home/*` and `Views/Shared/Error.cshtml` are unreachable; (2) `UsersController` does not handle API 401 (`GetUsersAsync` uses `EnsureSuccessStatusCode`, so an expired session on `/Users` throws instead of redirecting to login; `ChangeRole` shows "Could not change role."); (3) `ProductsController.AddErrors` has no `Conflict` branch (unlike Categories), so an API 409 on product create/edit/delete/image shows no message; tests assert only that the page re-renders; (4) `ProductsController.Image` is public by design (the API image GET is public).
- **Time-based paths** (`ExpiresSoon` at exactly 60 s) are tested with values on either side of the window, not at the boundary.
- **Request size limit (413)** on `UploadImage` is not enforced by `TestServer` (same as 017) and stays a manual check.
- Test data is hand-written JSON mirroring API DTOs; a DTO change in the API is not detected by MVC tests (no shared contract package, by constitution V).
- MVC visual layout and accessibility are not tested.
