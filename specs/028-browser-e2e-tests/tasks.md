---
description: "Task list for 028-browser-e2e-tests"
---

# Tasks: Browser end-to-end tests (Playwright)

**Input**: Design documents from `/specs/028-browser-e2e-tests/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md. Features 026 (payments) and 027 (admin dashboard) are implemented before this feature.

**Tests**: This feature IS tests. Each test class is written against the implemented views; where Docker exists it is run against a local stack, otherwise CI is the verification of record. No fixed sleeps, no retries.

**Organization**: Project and fixtures are the foundation (US1); helpers next; one phase per test class (US2-US5); compose override and CI job (US6); hygiene and final verification (US7).

## File ownership (for parallel work)

| Area | Files |
|---|---|
| Foundation (US1) | `CategoryProduct.E2ETests/{CategoryProduct.E2ETests.csproj,AssemblyInfo.cs,E2EEnvironment.cs,E2EFactAttribute.cs,E2EData.cs,TestImage.cs,E2EFixture.cs,BrowserTestBase.cs}`, `.gitignore` |
| Helpers | `CategoryProduct.E2ETests/Steps.cs` |
| Tests (US2-US5) | `AuthTests.cs`, `CatalogTests.cs`, `OrderFlowTests.cs`, `DashboardTests.cs`, `ErrorPageTests.cs` (one owner per file) |
| CI (US6) | `docker-compose.e2e.yml`, `.github/workflows/ci.yml` (append job `e2e` only) |

`Steps.cs` and `BrowserTestBase.cs` are shared: edited by one agent at a time. Nothing outside `CategoryProduct.E2ETests/`, `docker-compose.e2e.yml`, `.gitignore`, `.github/workflows/ci.yml` and this spec folder is touched.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency)
- **[Story]**: US1..US7 from spec.md
- Paths relative to the repository root.

---

## Phase 1: Setup

- [ ] T001 Record the baseline: `dotnet build CategoryProduct.slnx` and `dotnet test CategoryProduct.slnx` (note the test counts, they must not change); `git status`; confirm 026 and 027 are implemented (the `AddPayments` migration, `Orders/Details` pay form, `Views/Dashboard/Index.cshtml` exist); note the branch.
- [ ] T002 [P] Re-read, AFTER 026 and 027 landed, every MVC view and controller the tests touch and re-verify the texts, ids and selectors in `contracts/e2e-scenarios.md`: `MVC/Views/Account/{Register,Login,ForgotPassword,ForgotPasswordConfirmation,AccessDenied}.cshtml`, `Views/Shared/{_Layout,Error,_ValidationScriptsPartial,_OrderStatusBadge}.cshtml`, `Views/Categories/{Index,Create}.cshtml`, `Views/Products/{Index,Create,Details}.cshtml`, `Views/Cart/Index.cshtml`, `Views/Orders/{Index,Details,Manage}.cshtml` (pay form: select name, button text, `Payment received.`, `Payment declined`, `Paid`/`Refunded` badges), `Views/Inventory/History.cshtml`, `Views/Dashboard/Index.cshtml` (ids `kpi-*`, SVG aria labels, range form field names and messages), `Controllers/{Account,Cart,Orders,Products,Home}Controller.cs` (redirect targets, TempData texts), `API/Program.cs` (rate limiter keys, still 5/min defaults), `docker-compose.yml` (still no `RateLimiting__*` pass-through), `API/Data/BootstrapAdmin.cs`. Adjust the scenario table, spec texts and this task list where something changed (see the final note).

---

## Phase 2: Foundation - project, skip attribute, fixture, base class (US1)

**Independent Test**: `dotnet build CategoryProduct.E2ETests`; `dotnet test CategoryProduct.E2ETests` without `E2E_BASE_URL` skips everything.

- [ ] T003 [US1] Create `CategoryProduct.E2ETests/CategoryProduct.E2ETests.csproj` per `contracts/e2e-environment.md` (net10.0, exact pinned versions, no project references, `<Using Include="Xunit" />`). Resolve the Playwright version with `dotnet package search Microsoft.Playwright --exact-match` (or the NuGet page): the newest stable release at least two weeks old; pin it exactly; note it for Known Gaps. Do NOT add the project to `CategoryProduct.slnx`. (FR-001)
- [ ] T004 [P] [US1] Create `AssemblyInfo.cs` (`DisableTestParallelization`), `E2EEnvironment.cs` (env vars, `IsConfigured`, `SkipReason`, results dir, URL normalization), `E2EFactAttribute.cs` (skip when not configured), `E2EData.cs` (`Name(prefix)`, `Password()` random per run), `TestImage.cs` (valid 1x1 PNG bytes). No credential literal anywhere. (FR-002, FR-003, FR-007, FR-009)
- [ ] T005 [US1] Create `E2EFixture.cs` (`IAsyncLifetime`, collection fixture `e2e`): admin variables check, preflight `GET {base}/` with `HttpClient` (10 s timeout) and the clear failure messages of the behavior matrix, `Playwright.CreateAsync`, Chromium launch (`Headless = !E2E_HEADED`, `SlowMo`), disposal. (FR-003)
- [ ] T006 [US1] Create `BrowserTestBase.cs`: `[Collection("e2e")]`, new context per test (`Locale en-US`, `TimezoneId UTC`, viewport 1280x900, `BaseURL`), default timeouts (action/assertion 15 s, navigation 30 s, `Assertions.SetDefaultExpectTimeout`), console/request-failure/5xx collectors, tracing start, `RunAsync(Func<IPage,Task>)` that on exception writes `<Class>.<Method>.png|zip|txt` into the results dir (no cookies, no bodies, no passwords) and rethrows, on success stops tracing without saving; also `NewContextAsync()` for a second cookie jar (Admin + user in one test). (FR-004, FR-005)
- [ ] T007 [US1] Verify offline behavior: `dotnet build CategoryProduct.E2ETests` (0 errors, no new warnings); `dotnet test CategoryProduct.E2ETests` with `E2E_BASE_URL` unset -> all tests skipped with the reason text, exit code 0 (use a throwaway first test, replaced in later phases); also verify with `E2E_BASE_URL=http://localhost:1` and admin variables set that the failure message is the clear "cannot reach" one (a fixture-only check; no browser is launched before the preflight fails). (US1 scenarios 1-3)
- [ ] T008 [P] [US1] Add `test-results/` to `.gitignore` (only that line; bin/obj are already ignored). (FR-005)

---

## Phase 3: Helpers

- [ ] T009 [US2] Create `Steps.cs` with the helpers of `contracts/e2e-scenarios.md` (register, login, logout, create category, create product returning the id, upload image, add to cart, checkout returning the order id, pay, cancel, an Admin-context helper); every helper ends with a web-first assertion on the visible outcome; a 429 page (rate limit) produces the `Rate limited by the API: start the stack with docker-compose.e2e.yml` error; role/label/text locators only. (FR-006, FR-008)

---

## Phase 4: Authentication tests (US2)

**Independent Test**: `dotnet test CategoryProduct.E2ETests --filter "FullyQualifiedName~AuthTests"` against a stack.

- [ ] T010 [US2] `AuthTests.Register_Login_Logout_Works`: register, `Hello, name`, auth cookie flags (`HttpOnly`, `SameSite=Lax`) via `context.CookiesAsync()`, logout (cookie gone, `Login`/`Register` links), login again. Determine the auth cookie by diffing the cookie names before login and after (do not hard-code the cookie name). (scenarios 1-2)
- [ ] T011 [US2] `AuthTests.Register_ClientSideValidation_BlocksInvalidFormWithoutRequest`: POST counter via `page.Request`, invalid values, messages visible, URL unchanged, counter 0, then valid values succeed (counter 1). (scenario 3)
- [ ] T012 [US2] `AuthTests.Login_WrongPassword_ShowsErrorAndStays`. (scenario 4)
- [ ] T013 [US2] `AuthTests.NonAdmin_AdminPages_AccessDenied_And_Anonymous_RedirectToLogin` for `/Users`, `/Inventory`, `/Dashboard`, `/Orders/Manage`. (scenario 5)
- [ ] T014 [US2] `AuthTests.ForgotPassword_ShowsGenericConfirmation_ForKnownAndUnknownUser` (same `#forgot-confirmation` text for both; no email assertion, A6). (scenario 6)

---

## Phase 5: Catalog tests (US3)

- [ ] T015 [US3] `CatalogTests.Category_Create_ClientValidation_And_Success` (Admin context; no POST on invalid; category listed).
- [ ] T016 [US3] `CatalogTests.Product_Create_ClientValidation_And_Success`.
- [ ] T017 [US3] `CatalogTests.Product_ImageUpload_RendersImage` (`FilePayload` from `TestImage.Png`; `naturalWidth > 0` via `WaitForFunctionAsync`; button `Replace image`).
- [ ] T018 [US3] `CatalogTests.Product_ImageUpload_RejectsNonImage` (text bytes named `.png`; error visible; no `img`).

---

## Phase 6: Order flow tests (US4)

**Independent Test**: `dotnet test CategoryProduct.E2ETests --filter "FullyQualifiedName~OrderFlowTests"`.

- [ ] T019 [US4] `OrderFlowTests.AddToCart_Checkout_OrderAppearsInMyOrders` (Admin creates product qty 5 price 12.50; user adds 2; total 25.00; `Order placed.`; `/Orders` row; empty cart).
- [ ] T020 [US4] `OrderFlowTests.Pay_WithApproveToken_MarksOrderPaid` (`tok_visa`; `Payment received.`; `Paid`; no form).
- [ ] T021 [US4] `OrderFlowTests.Pay_WithDeclineToken_ShowsError_AndOrderStaysUnpaid` (`tok_decline`; `Payment declined`; form still present).
- [ ] T022 [US4] `OrderFlowTests.Cancel_PaidOrder_Refunds_AndRestoresStock` (pay, cancel, `Cancelled` + `Refunded`, Admin `Inventory/History/{id}` shows `Quantity: 5`).
- [ ] T023 [US4] `OrderFlowTests.Admin_Confirms_Order_OnManageOrders` (row located by order id text; `Confirmed`; Confirm button gone). If the Manage list is paged and the order is not on page 1, filter with the `status=Pending` form or open the newest page first; never assume an empty database.

---

## Phase 7: Dashboard and error page (US5)

- [ ] T024 [US5] `DashboardTests.Dashboard_Renders_Kpis_And_Svg_Charts` (cards, two SVGs with `rect`, top products table or empty state, no console errors, invalid range shows a field message and no cards). Follow the implemented 027 view (ids and texts from T002).
- [ ] T025 [US5] `ErrorPageTests.ErrorPage_ShowsGenericMessage_WithoutStackTrace` (`/Home/Error`; `Something went wrong`; no `   at `, `Exception`, `.cs:line`).

---

## Phase 8: Compose override and CI (US6)

- [ ] T026 [US6] Create `docker-compose.e2e.yml` exactly per `contracts/e2e-ci.md` (six `RateLimiting__*` keys for `api`, value 1000, no secrets). `docker-compose.yml` is not edited. Validate syntax without Docker where possible (`python -c "import yaml"` is not required; a YAML lint via the editor or `docker compose config` in CI is the check).
- [ ] T027 [US6] Append job `e2e` to `.github/workflows/ci.yml` per `contracts/e2e-ci.md`: `needs: docker-build`, `timeout-minutes: 25`, random masked `.env` including `BOOTSTRAP_ADMIN_*`, `E2E_*` through `GITHUB_ENV`, compose up with both files, the two wait loops copied from `compose-smoke`, `dotnet build CategoryProduct.E2ETests -c Release`, `pwsh CategoryProduct.E2ETests/bin/Release/net10.0/playwright.ps1 install --with-deps chromium`, `dotnet test CategoryProduct.E2ETests -c Release --no-build`, `actions/upload-artifact` (pin the exact version in use by checking the current latest v4 release at least two weeks old; record it in Known Gaps) with `if: failure()` and path `test-results/` only, logs on failure, teardown with `down -v` and removal of `.env`. Existing jobs untouched. (FR-010, FR-011, FR-014)
- [ ] T028 [US6] Review the workflow diff: only the new job added; `git diff .github/workflows/ci.yml` shows no change to `build-test`, `docker-build`, `sqlserver-tests`, `compose-smoke`; `path:` lines of upload steps contain only `test-results/`; every secret is generated and masked; `E2E_ADMIN_PASSWORD` is never echoed; the skipped-test guard is present (the log must not contain skipped tests: add a step or a `grep -q "Skipped:  0"` style check on the test summary if the logger prints it, otherwise rely on the fixture failing when `E2E_BASE_URL` is unset in CI).

---

## Phase 9: Review and hygiene

- [ ] T029 Stability grep over `CategoryProduct.E2ETests/`: no `Thread.Sleep`, `Task.Delay`, `WaitForTimeoutAsync`, no `Retry`/`[Theory]`-with-retry helpers, no `Skip =` other than the attribute, no `XPath`/`nth=` selectors; tests contain no hard-coded user names, base URL or credentials; each test creates its own data.
- [ ] T030 Secret hygiene: `git status` shows only the allowed files; grep the new files and the workflow diff for passwords/keys/connection strings (none; only generated values); `.gitignore` has `test-results/`; `CategoryProduct.slnx` unchanged (`git diff --stat CategoryProduct.slnx` empty); `docker-compose.yml` unchanged; no production project touched.

---

## Phase 10: Final verification and Known Gaps (US7)

- [ ] T031 Run `dotnet build CategoryProduct.slnx` (0 errors, no new warnings) and `dotnet build CategoryProduct.E2ETests`.
- [ ] T032 Run `dotnet test CategoryProduct.slnx` THREE times in a row; all green each time; the counts equal the T001 baseline (the E2E project is not in the solution).
- [ ] T033 Run `dotnet test CategoryProduct.E2ETests` without `E2E_BASE_URL`: 16 tests skipped, 0 failed, exit code 0 (Docker is not installed on the dev machine; the real run is the CI job).
- [ ] T034 Complete `specs/028-browser-e2e-tests/spec.md` Known Gaps with the final facts (Playwright and `upload-artifact` versions pinned, anything adjusted after T002, what could only be verified in CI) and tick anything left in `checklists/requirements.md`; set the spec Status line.
- [ ] T035 After pushing (when the user pushes), confirm the `e2e` job is green with all 16 tests passed and none skipped, that `build-test`, `docker-build`, `sqlserver-tests`, `compose-smoke` are unchanged and green, then prove the failure path ONCE: temporarily break one assertion, push, confirm the `e2e-failure-artifacts` artifact contains `.png`, `.zip`, `.txt` and no `.env`, then revert; record the run in Known Gaps. LEFT OPEN until the user pushes.

> **Final note on features 026 and 027**: 026 (payments) and 027 (admin dashboard) change or add MVC views and controllers while this feature was specified (`Views/Orders/Details.cshtml`, `Manage.cshtml`, `Index.cshtml`, `Views/Dashboard/*`, `Views/Shared/_Layout.cshtml`, `OrdersController`, `DashboardController`, `Program.cs` of the API, `docker-compose.yml` possibly) and this spec treats them as present according to their specs, not their code. The implementer MUST re-read the MVC views and controllers (task T002) after both have landed, before writing any test, and adapt button texts, select names, badge texts, TempData messages, element ids and the Dashboard field names in `contracts/e2e-scenarios.md` and in this task list. If a needed element cannot be located by role, label, text or an existing id, record it in Known Gaps instead of changing the view.

---

## Dependencies and execution order

- T001 first; T002 before any test code (it may change selectors).
- Phase 2: T003 first; T004 and T008 in parallel after T003; T005 after T004; T006 after T005; T007 after T006.
- T009 after T006 (needs the base class contract).
- Phases 4-7 need T009: the five test files are independent and can be written in parallel by different agents (`AuthTests`, `CatalogTests`, `OrderFlowTests`, `DashboardTests` + `ErrorPageTests`); `Steps.cs` additions are made sequentially.
- Phase 8 (T026-T028) is independent of Phases 4-7 after T003 and may run in parallel with them (different files), but `ci.yml` is edited by one agent only.
- T029-T030 after all code; T031-T035 last.

## Parallel example

Agent A: T003-T009 (foundation and helpers). Then in parallel: Agent B: T010-T014 (Auth) and T024-T025; Agent C: T015-T023 (Catalog and OrderFlow); Agent D: T026-T028 (compose override, CI). Then one agent: T029-T035.

## Implementation strategy

MVP = Phase 2 + T009 + the Auth tests (the offline skip and the browser plumbing proven). Next the order flow (the money path), then catalog, dashboard, error page; the CI job can be added early to start getting real runs because the dev machine has no Docker. Keep every test independent and every wait web-first; fix a flaky test, never retry it.
