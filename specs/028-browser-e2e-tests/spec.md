# Feature Specification: Browser end-to-end tests (Playwright)

**Feature Branch**: `028-browser-e2e-tests`

**Created**: 2026-11-22

**Status**: Draft

**Input**: User description: "Add real-browser end-to-end tests that cover what the MVC tests cannot: JavaScript, client-side validation, real cookies, and the real API + MVC + SQL Server running together. A new project `CategoryProduct.E2ETests` (Microsoft.Playwright + xunit, pinned versions) that is NOT in `CategoryProduct.slnx` (same approach as `CategoryProduct.API.SqlServerTests`). It targets the running docker compose stack (base URL from `E2E_BASE_URL`), uses an Admin created through the 023 bootstrap admin variables, skips when `E2E_BASE_URL` is not set, captures screenshots and traces on failure as CI artifacts, and runs in GitHub Actions."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Test project, fixtures and skip behavior (Priority: P1)

A developer can build `CategoryProduct.E2ETests` anywhere, run it without Docker (every test is skipped with a clear reason), and point it at a running stack with environment variables.

**Why this priority**: every scenario depends on it, and it must never break the fast offline `dotnet test CategoryProduct.slnx`. Verified at HEAD: `CategoryProduct.slnx` lists only `API.Tests`, `API`, `MVC.Tests`, `MVC` (`CategoryProduct.API.SqlServerTests` is not in it); CI runs the SQL Server project explicitly (`dotnet test CategoryProduct.API.SqlServerTests -c Release` in job `sqlserver-tests`), and that project disables parallelization in `AssemblyInfo.cs` (`CollectionBehavior(DisableTestParallelization = true)`). There is no Playwright reference anywhere in the repository.

**Independent Test**: `dotnet build CategoryProduct.E2ETests` and `dotnet test CategoryProduct.E2ETests` on a machine without Docker and without `E2E_BASE_URL` (all tests reported as skipped, exit code 0).

**Acceptance Scenarios**:

1. **Given** `E2E_BASE_URL` is not set, **When** the E2E project is tested, **Then** every test is skipped (custom `E2EFactAttribute` sets `Skip` with the message `Set E2E_BASE_URL (and E2E_ADMIN_USERNAME / E2E_ADMIN_PASSWORD) to run browser tests`), no browser is launched, no network call is made, exit code 0.
2. **Given** `E2E_BASE_URL` is set but the site is unreachable, **Then** the tests FAIL (not skip) with one clear message naming the URL and the preflight error (a skip would hide a broken stack in CI).
3. **Given** `E2E_BASE_URL` is set but `E2E_ADMIN_USERNAME` or `E2E_ADMIN_PASSWORD` is missing, **Then** the tests fail with a message naming the missing variable (never printing the password).
4. **Given** `dotnet build CategoryProduct.slnx` and `dotnet test CategoryProduct.slnx`, **Then** the E2E project is not built or run and the test counts are unchanged.
5. **Given** the project file, **Then** `Microsoft.Playwright` and every other package use exact pinned versions (no floating ranges) and the project targets `net10.0` like its siblings.

---

### User Story 2 - Authentication in a real browser (Priority: P1)

Register, login, logout, wrong password, client-side validation, the real auth cookie and the AccessDenied behavior are exercised in Chromium.

**Why this priority**: it is the part MVC integration tests cannot see: `_ValidationScriptsPartial` (jQuery Validate + unobtrusive) is rendered by `Register`, `Login`, `ForgotPassword`, `ChangePassword`, `ResetPassword`, `Profile`, `Categories/Create|Edit` and `Products/Create|Edit` (verified by grep) and runs only in a browser; the cookie is `HttpOnly`, `SameSite=Lax`, `Secure` per `Cookie:SecurePolicy` (compose sets `SameAsRequest`).

**Independent Test**: `dotnet test CategoryProduct.E2ETests --filter "FullyQualifiedName~AuthTests"` against the stack.

**Acceptance Scenarios**:

1. **Given** a unique user name, **When** the visitor registers through the form, **Then** the page shows `Hello, <name>` (the register action signs the user in), then Logout (navbar button, a POST) shows the `Login` and `Register` links and no `Hello`; then logging in again shows `Hello, <name>`.
2. **Given** the real cookie jar (`context.CookiesAsync`), **Then** after login an auth cookie exists with `HttpOnly = true` and `SameSite = Lax`, and after logout that cookie is gone.
3. **Given** the Register form with a 2-character user name, a 3-character password and a different confirmation, **When** the visitor clicks Register, **Then** the field messages (`span[data-valmsg-for]`) become visible, the URL does not change, and NO `POST /Account/Register` request was sent (counted with the page `Request` event).
4. **Given** a wrong password, **When** logging in, **Then** the validation summary shows an error and the visitor stays on `/Account/Login`.
5. **Given** a signed-in non-Admin user, **When** opening `/Users`, `/Inventory`, `/Dashboard` or `/Orders/Manage`, **Then** the browser lands on `/Account/AccessDenied` with the text `You don't have permission to do this.`; **Given** an anonymous visitor, **Then** the same URLs redirect to `/Account/Login?ReturnUrl=...`.
6. **Given** the Forgot password page, **When** an existing user name and, in a second visit, an unknown name are submitted, **Then** both show the same generic confirmation (`#forgot-confirmation`), proving no account enumeration through the UI. Email delivery is NOT verified (see Assumptions A6).

---

### User Story 3 - Admin catalog with image upload (Priority: P1)

An Admin creates a category and a product through the forms, uploads an image and sees it rendered.

**Why this priority**: it exercises multipart upload, the file input, API image storage on the real volume, and the image streaming route through MVC with a real `<img>`.

**Independent Test**: `dotnet test CategoryProduct.E2ETests --filter "FullyQualifiedName~CatalogTests"`.

**Acceptance Scenarios**:

1. **Given** the Category Create form with an empty name, **When** Create is clicked, **Then** the required message appears client-side with no `POST /Categories/Create`; with a unique name the category appears in the list.
2. **Given** the Product Create form, **Then** an empty name or a missing category shows client messages with no request; with valid data (unique name, price, quantity, the category) the product is listed.
3. **Given** the product Details page, **When** a generated 1x1 PNG is uploaded (bytes built in the test, no file in the repository), **Then** the page shows the `<img alt="<product name>">`, and in the browser `naturalWidth > 0` (the image really loaded, not just the tag), and the button now reads `Replace image`.
4. **Given** a non-image file (text bytes named `.png`), **When** uploaded, **Then** an error message is shown and no `<img>` appears.

---

### User Story 4 - Shopping, payment and refund flow (Priority: P1)

A user adds to the cart, checks out, finds the order in My Orders, pays with fake tokens (approve and decline), cancels a paid order and sees the refund and the restored stock; an Admin confirms another order.

**Why this priority**: it is the money path across API, MVC, database and the 026 payments UI, with real cookies and anti-forgery tokens.

**Independent Test**: `dotnet test CategoryProduct.E2ETests --filter "FullyQualifiedName~OrderFlowTests"`.

**Acceptance Scenarios**:

1. **Given** an Admin-created product with quantity 5 and a freshly registered user, **When** the user adds 2 to the cart (Details page form), opens Cart, clicks Checkout, **Then** the order details page shows `Order placed.`, a `Pending` badge and the correct total; `My Orders` lists the order; the Cart is empty.
2. **Given** that Pending, unpaid order, **When** the user selects `tok_visa` and clicks the pay button, **Then** `Payment received.` appears, the page shows a `Paid` badge and no pay form.
3. **Given** another Pending order, **When** the user selects `tok_decline` and pays, **Then** an error alert starting `Payment declined` appears, the order stays unpaid and the pay form is still present.
4. **Given** a Paid, Pending order, **When** the user clicks `Cancel order`, **Then** the status shows `Cancelled` and the payment shows `Refunded`; **And** the Admin's `Inventory > History` page of the product shows the quantity back at 5 (stock restored).
5. **Given** a Pending order, **When** an Admin opens `Manage Orders` and clicks Confirm on that order's row, **Then** the row shows `Confirmed` and the Confirm button is gone.
6. Every test creates its own category, product (unique names) and user, so tests are independent and can run in any order.

---

### User Story 5 - Admin dashboard and error page (Priority: P2)

The dashboard (feature 027) renders its KPI cards and SVG charts in a browser; the generic error page leaks no internals.

**Why this priority**: 027 deliberately avoided a JS chart library so that SVG and cards can be asserted; a real browser proves the page renders without console errors.

**Independent Test**: `dotnet test CategoryProduct.E2ETests --filter "FullyQualifiedName~DashboardTests|FullyQualifiedName~ErrorPageTests"`.

**Acceptance Scenarios**:

1. **Given** the Admin opens `Dashboard`, **Then** the cards `kpi-orders`, `kpi-revenue`, `kpi-aov`, `kpi-new-users`, `kpi-low-stock` are visible, an `svg[aria-label="Orders per day"]` and `svg[aria-label="Revenue per day"]` each contain at least one `rect`, the top products table (or its empty state) is visible, and the browser console reported no error-level messages.
2. **Given** an invalid range (from after to), **When** Show is clicked, **Then** a field message appears next to the date inputs and the cards are not shown.
3. **Given** a signed-in visitor opens `/Home/Error` (a real route; no test-only endpoint is added), **Then** the page shows `Something went wrong` and contains no stack trace text (`   at `, `Exception`, file paths).

---

### User Story 6 - CI job, artifacts and stability (Priority: P1)

GitHub Actions runs the E2E tests on every push against the real compose stack and uploads screenshots and traces when a test fails.

**Why this priority**: the E2E suite only has value if it runs automatically; a flaky or secret-leaking pipeline would be worse than none.

**Independent Test**: the `e2e` job in `.github/workflows/ci.yml` (verified by CI; the dev machine has no Docker).

**Acceptance Scenarios**:

1. **Given** a push, **Then** a new job `e2e` runs (see `contracts/e2e-ci.md`): throwaway random `.env` (never committed, secrets masked), `docker compose up -d --build` with an additive compose override that raises the rate limits for the test run, waits for `/health` and the MVC home page, installs Chromium only, builds and runs `CategoryProduct.E2ETests`, and tears the stack down with `down -v`.
2. **Given** a failing test, **Then** `test-results/` (screenshots `*.png`, Playwright traces `*.zip`, a `*.txt` per failure with URL and console log) is uploaded with a pinned `actions/upload-artifact` version (`if: failure()`), and `.env` is never inside an uploaded path.
3. **Given** the whole suite, **Then** no `Thread.Sleep`, `Task.Delay` or `WaitForTimeoutAsync` exists (a grep task enforces it), retries are off (no retry attribute, no CI re-run step), and waits rely on Playwright auto-waiting and web-first assertions (`Expect(...)`) with a generous default timeout (15 s per assertion/action, 60 s per test).
4. **Given** the job, **Then** the existing jobs `build-test`, `docker-build`, `sqlserver-tests`, `compose-smoke` are unchanged.

---

### User Story 7 - Final verification and Known Gaps (Priority: P3)

Build, three full test runs of the offline solution, E2E skip behavior verified locally, Known Gaps written; the CI run stays open.

**Acceptance Scenarios**:

1. `dotnet build CategoryProduct.slnx` and `dotnet build CategoryProduct.E2ETests` succeed; `dotnet test CategoryProduct.slnx` is green three times with unchanged counts; `dotnet test CategoryProduct.E2ETests` without `E2E_BASE_URL` reports all tests skipped.
2. Known Gaps are filled in; CI verification is left open until the user pushes.

---

### Edge Cases

- Rate limiting is the main trap. API limiters are per client IP and the API only sees the MVC container as the client: `login` 5/min, `register` 5/min, `forgot-password` 5/min, `password` 5/min, `logout` 30/min, `profile` 30/min (verified in `CategoryProduct.API/Program.cs`; keys `RateLimiting:*PermitLimit`). Registering also logs in, so one registration costs one `register` and one `login` permit. `docker-compose.yml` does not forward these keys today, so the suite needs an override file (`docker-compose.e2e.yml`, E2E-only, passes `RateLimiting__*` through). A 429 seen by a test is reported as `rate limited: start the stack with docker-compose.e2e.yml`, not as a mysterious timeout.
- Logout revokes the refresh token of that session (`AccountController.Logout` sends `refresh_token` to the API), so a shared, cached Admin session would break other tests. Every test therefore signs in by itself; tests are not parallelized (assembly `DisableTestParallelization`, same as the SQL Server project).
- The seeded Admin is shared state, but tests only use it; all created data has the unique prefix `e2e-<prefix>-<8 hex>` so reruns against a long-lived stack never collide. Data is not cleaned (the CI stack is thrown away with `down -v`); locally the developer can reset the stack.
- Browser locale is fixed (`en-US`, UTC time zone) so decimal inputs (`12.50`) and jQuery validation behave the same on every machine.
- 026 and 027 are implemented before this feature; if a selector or text changed during their implementation the tests follow the implemented views (T002 re-reads them).
- `Cookie:SecurePolicy=SameAsRequest` in compose lets the cookie work over plain `http://localhost`; `Secure` is therefore not asserted.
- Playwright browsers are downloaded on first use; the offline `dotnet test CategoryProduct.slnx` never triggers a download (the project is not in the solution and tests skip before launching a browser).
- Cart/Checkout use real stock; a product created per test avoids cross-test stock interference.
- The Pickup email outbox lives in a docker volume of the API container; the test process cannot read it, and email is disabled by default (`EMAIL_ENABLED=false`), so no mail assertions exist.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: New project `CategoryProduct.E2ETests` (`net10.0`, xunit, `Microsoft.Playwright`, exact pinned versions, `IsPackable=false`, no reference to the API or MVC projects: Principle V, the tests only speak HTTP/browser), NOT added to `CategoryProduct.slnx`.
- **FR-002**: Configuration only through environment: `E2E_BASE_URL`, `E2E_ADMIN_USERNAME`, `E2E_ADMIN_PASSWORD` (optional `E2E_HEADED=1`, `E2E_SLOWMO_MS` for local debugging, `E2E_RESULTS_DIR` default `test-results`). No credentials in code or tracked files.
- **FR-003**: `E2EFactAttribute` (derives from `FactAttribute`) sets `Skip` when `E2E_BASE_URL` is empty; an assembly fixture performs a preflight `GET {base}/` and fails with a clear message when unreachable or when the admin variables are missing.
- **FR-004**: A base class creates one Chromium browser per test class (headless unless `E2E_HEADED`), a new isolated context per test (`Locale=en-US`, `TimezoneId=UTC`, fixed viewport, no shared cookies), default timeouts 15 s, and tracing (screenshots, snapshots, sources) started per test.
- **FR-005**: On failure only: save a full-page screenshot and the trace zip to `test-results/` named `<TestClass>.<Test>.png|zip`, plus a text file with the final URL, the browser console messages and failed requests; on success the trace is discarded. Implemented with an explicit `RunAsync` wrapper (xunit v2 has no outcome hook).
- **FR-006**: Selectors: roles, labels, visible text and the stable ids/ARIA labels the views already have (`forgot-confirmation`, `kpi-*`, `svg[aria-label]`); no CSS-structure or XPath selectors; no fixed sleeps, no `WaitForTimeoutAsync`, no retries.
- **FR-007**: Unique data per test: `E2EData.Name(prefix)` returns `e2e-<prefix>-<8 hex>` (user names respect `^[A-Za-z0-9._-]+$`, 3-50; product 2-100); the password for generated users is random per run and never logged.
- **FR-008**: The 16 tests listed in `contracts/e2e-scenarios.md` in classes `AuthTests`, `CatalogTests`, `OrderFlowTests`, `DashboardTests`, `ErrorPageTests`; helper class `Steps` (register, login, logout, create category/product, upload image, add to cart, checkout, pay, cancel) shared by the tests.
- **FR-009**: A tiny valid PNG is generated in memory (a base64 constant of a 1x1 PNG decoded at runtime, or built from signature + IHDR + IDAT + IEND with CRC32/Adler32) and uploaded with `SetInputFilesAsync(new FilePayload { ... })`; nothing is written to the repository.
- **FR-010**: `docker-compose.e2e.yml` (new, additive; used only with `-f docker-compose.yml -f docker-compose.e2e.yml`) sets `RateLimiting__LoginPermitLimit`, `RegisterPermitLimit`, `ForgotPasswordPermitLimit`, `PasswordPermitLimit`, `LogoutPermitLimit`, `ProfilePermitLimit` for the `api` service to a high number (1000). The base `docker-compose.yml` is NOT changed.
- **FR-011**: CI job `e2e` per `contracts/e2e-ci.md`; the existing four jobs are not modified; Playwright browsers installed with `pwsh bin/Release/net10.0/playwright.ps1 install --with-deps chromium`; pinned action versions; artifacts only on failure; `.env`, `health.json`, `home.html` are never uploaded and are removed in teardown.
- **FR-012**: No change to production code, views, controllers, models, migrations or the solution file. If a test needs a hook the views lack, it uses a role/text locator; adding `data-testid` attributes is out of scope (Known Gaps if something proves unreachable).
- **FR-013**: `dotnet test CategoryProduct.slnx` remains offline, fast and with unchanged counts.
- **FR-014**: No secrets in the repository; CI values are random per run and masked (`::add-mask::`), matching the `compose-smoke` job.

### Key Entities

- None. No schema change. The suite creates ordinary users, categories, products, orders and payments through the UI.

## Success Criteria *(mandatory)*

- **SC-001**: `dotnet test CategoryProduct.E2ETests` without environment skips all tests and exits 0; with a running stack all 16 tests pass.
- **SC-002**: The browser-only behaviors (client-side validation without a request, real cookie flags, real image load, SVG render, multipart upload, anti-forgery POST flows) are asserted in a real browser.
- **SC-003**: `dotnet test CategoryProduct.slnx` is green three times with unchanged counts and never builds the E2E project.
- **SC-004**: The `e2e` CI job is green on a clean run and a deliberately failing test produces screenshot and trace artifacts (verified once by temporarily breaking an assertion, then reverted).
- **SC-005**: No fixed sleeps, no retries, no committed or uploaded secrets.

## Out of Scope

- Cross-browser runs (Firefox, WebKit), mobile emulation, visual regression, accessibility audits, performance/load tests.
- Reading e-mail (password reset links end-to-end), real SMTP.
- Changes to views to add `data-testid`; any test-only endpoint, test-only feature flag or production code change.
- Running the E2E tests inside `dotnet test CategoryProduct.slnx`, sharding, pull-request-only runs.
- Docker layer caching between jobs (Known Gaps).
- Cleaning test data from a long-lived environment.

## Assumptions

- **A1 (timing)**: feature 026 (payments UI: Pay now form with the fake token `<select name="paymentToken">` containing `tok_visa`, `tok_decline`, the `Paid`/`Refunded` badges and `TempData` messages `Payment received.` / `Payment declined: ...`) and feature 027 (Dashboard page per `specs/027-admin-dashboard/contracts/dashboard-page.md`) are implemented before 028 and are treated as present. The implementer re-reads the MVC views and controllers after both landed and adapts selectors and texts (T002).
- **A2 (framework)**: plain xunit v2 (`xunit` 2.9.3, as the sibling projects) + `Microsoft.Playwright` (the library, not `Microsoft.Playwright.Xunit`), so the base class, skip attribute and failure capture are explicit and the same pinned xunit as the rest of the repo is used. The exact Playwright version is the newest stable one that is at least two weeks old, resolved with `dotnet package search Microsoft.Playwright` at T003 and then pinned without a range.
- **A3 (target)**: the compose stack (`docker compose up`), base URL from `E2E_BASE_URL` (CI: `http://localhost:5256`; the documented local value is the same, the compose default `MVC_HOST_PORT`). When `E2E_BASE_URL` is not set the tests skip even if a stack happens to be running (explicit opt-in).
- **A4 (admin)**: the Admin is the 023 bootstrap admin (`Bootstrap__AdminUserName/AdminPassword` from `BOOTSTRAP_ADMIN_USERNAME/BOOTSTRAP_ADMIN_PASSWORD`; created only when no Admin exists, verified in `BootstrapAdmin.cs`: name 3-50 chars `[A-Za-z0-9._-]`, password >= 8). CI generates both randomly per run, writes them to the `.env` (for compose) and passes them to the test step as masked environment variables `E2E_ADMIN_USERNAME` / `E2E_ADMIN_PASSWORD`.
- **A5 (CI placement)**: a new job `e2e`, `needs: docker-build`, rather than extending `compose-smoke`. Reasons in `research.md` R5.
- **A6 (email)**: not verified end-to-end. The reset mail goes to a Pickup directory in a docker volume of the API container and `Email__Enabled` defaults to false in compose; reading it would need `docker exec`/volume mounting and an email-enabled stack, a different environment from the one the other tests use. The forgot password test asserts the generic UI confirmation only; email content is already covered by API tests (features 024/025). Known Gap.
- **A7 (parallelism)**: test classes run sequentially (`DisableTestParallelization`) because the API limiters are per IP, logout revokes sessions, and a single browser process on a CI runner is simpler to debug; the suite is small (about 16 tests, a few minutes).
- **A8 (rate limits)**: raised only through the E2E compose override, never in the base compose or production defaults; local runs against a stack started without the override can hit 429 and are told to use it (`quickstart.md`).
- **A9 (browser)**: Chromium only, headless in CI, installed with `--with-deps` on `ubuntu-latest`.
- **A10 (error page)**: `/Home/Error` is a real, anonymous-reachable route that renders the production error view with status 500 and no exception; testing it needs no test-only endpoint. Triggering a genuine unhandled exception is not possible without one and is out of scope.
- **A11 (retries)**: none, by policy: a flaky test is fixed (better locator, web-first assertion) or removed, never retried.
- **A12 (stock check)**: the refund test verifies the restored stock on the Admin `Inventory/History/{id}` page (it shows `Quantity:` and the stock movements), because the paged Inventory list could push the product off the first page on a long-lived stack.

## Known Gaps

(To be completed in the final phase: whether any test needed an adjustment, the Playwright and action versions actually pinned, run times, deviations.)

- Email delivery (reset link, receipts) is not verified end-to-end (A6).
- Chromium only; no Firefox/WebKit.
- The compose images are built again in the `e2e` job (no layer cache shared with `docker-build`); acceptable at the current size, remedy: buildx with the GitHub cache.
- No cleanup of test data on long-lived environments.
- CI verification: open until the user pushes.
