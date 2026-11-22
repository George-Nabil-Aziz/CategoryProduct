---

description: "Task list for 022-mvc-bugfixes"
---

# Tasks: MVC Bug Fixes

**Input**: Design documents from `/specs/022-mvc-bugfixes/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Requested. Each story writes its failing tests first, then the fix (test-first order inside every phase). Production edits are limited to `CategoryProduct.MVC`; all test edits are in `CategoryProduct.MVC.Tests`.

**Organization**: grouped by user story. The four fixes touch different files except `CategoryProduct.MVC/Services/CategoryProductApiClient.cs` (US2 and US4), so US2 and US4 production edits are sequential; everything else can run in parallel.

## File ownership (for parallel work)

| Story | Production files | Test files |
|---|---|---|
| Foundational | none | `Support/MvcFactory.cs`, `Support/ProductionMvcFactory.cs` |
| US1 error page | `Controllers/HomeController.cs`, `Views/Shared/Error.cshtml`, `Views/Home/*` (delete) | `ErrorPageTests.cs` |
| US2 Users 401 | `Services/CategoryProductApiClient.cs` (GetUsersAsync), `Controllers/UsersController.cs` | `UsersTests.cs`, `SessionExpiryTests.cs` |
| US3 Products 409 | `Controllers/ProductsController.cs` | `ProductsTests.cs`, `ProductImageTests.cs` |
| US4 Logout | `Services/TokenRefreshHandler.cs`, `Services/CategoryProductApiClient.cs` (LogoutAsync) | `AccountTests.cs`, `TokenRefreshHandlerTests.cs`, `ApiUnavailableTests.cs` (only if needed) |
| US5 docs | none | `specs/021-test-coverage/spec.md`, `specs/021-test-coverage/contracts/mvc-test-matrix.md`, `specs/022-mvc-bugfixes/spec.md` |

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency)
- **[Story]**: US1..US5 from spec.md
- All paths are relative to the repository root. `MVC` = `CategoryProduct.MVC`, `MVCT` = `CategoryProduct.MVC.Tests`.

---

## Phase 1: Setup

- [ ] T001 Run `dotnet build CategoryProduct.slnx` and `dotnet test CategoryProduct.slnx` on the untouched branch; record the baseline (expected 490 tests: 278 MVC, 212 API, all green). Do not continue if the baseline is red.
- [ ] T002 [P] Re-verify the facts the plan relies on by reading the files (record any difference in `research.md`): `MVC/Program.cs` registers `UseExceptionHandler("/Home/Error")` outside Development and has no other change needed; `MVC/Controllers/` has no `HomeController`; `MVC/Views/Home/` holds only `Index.cshtml` and `Privacy.cshtml`; `UsersController.Index` ignores `Unauthorized`; `ProductsController.AddErrors` has no Conflict branch; `LogoutAsync` sends no bearer; `MVCT/Support/MvcFactory.cs` is `sealed` with `UseEnvironment("Development")`.

## Phase 2: Foundational (blocking for US1 tests only)

- [ ] T003 [US1] `MVCT/Support/MvcFactory.cs`: remove `sealed`, add `protected virtual string EnvironmentName => "Development";` and use it in `builder.UseEnvironment(EnvironmentName)`. No other change; update the class summary to mention the override. (FR-012)
- [ ] T004 [P] [US1] Create `MVCT/Support/ProductionMvcFactory.cs`: `public sealed class ProductionMvcFactory : MvcFactory { protected override string EnvironmentName => "Production"; }` (namespace `CategoryProduct.MVC.Tests`). Build the test project; run the existing suite quickly (`dotnet test CategoryProduct.MVC.Tests`) to prove no existing test changed behavior.

**Checkpoint**: existing MVC tests still green; `ProductionMvcFactory` compiles.

---

## Phase 3: User Story 1 - Real error page outside Development (Priority: P1)

**Goal**: unhandled exception under Production gives a friendly 500 page, no leak.

**Independent Test**: `dotnet test CategoryProduct.MVC.Tests --filter "FullyQualifiedName~ErrorPageTests"`

- [ ] T005 [US1] Write `MVCT/ErrorPageTests.cs` FIRST (they must fail now: 404 today): `public class ErrorPageTests : MvcTestBase, IClassFixture<ProductionMvcFactory>` with constructor `ErrorPageTests(ProductionMvcFactory factory) : base(factory)`. Cases from `contracts/error-page.md`: GET unhandled exception (500, friendly text, link to `/Categories`, no marker text, no `InvalidOperationException`, no `   at `, no "Development Mode"), Request ID shown, `Cache-Control` contains `no-store`, POST unhandled exception (sign in Admin, token page scripted, `Fake.Throw(Post, "api/category", new InvalidOperationException("secret-marker-9f3a"))`), anonymous `GET /Home/Error` = 500 not a login redirect, signed-in layout still shows `Hello, <name>`, `GET /Home` and `/Home/Index` redirect to `/Categories`. (FR-011, SC-002)
- [ ] T006 [US1] Create `MVC/Controllers/HomeController.cs` per `contracts/error-page.md` (`Index` redirect; `Error` with `[AllowAnonymous]`, `[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]`, status 500, `ErrorViewModel.RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier`, `View("Error", model)`). (FR-001)
- [ ] T007 [P] [US1] Edit `MVC/Views/Shared/Error.cshtml`: heading "Something went wrong", the one-sentence message, Request ID line when `Model.ShowRequestId`, `<a asp-controller="Categories" asp-action="Index" class="btn btn-primary">Back to Categories</a>`; remove the whole "Development Mode" block. (FR-002)
- [ ] T008 [P] [US1] Delete `MVC/Views/Home/Index.cshtml` and `MVC/Views/Home/Privacy.cshtml` (`git rm`-style deletion of the files; do not commit). Confirm `grep -rn "Home/Index\|Home/Privacy\|Privacy" MVC --include=*.cshtml --include=*.cs` finds nothing live. (FR-003)
- [ ] T009 [US1] Run `dotnet test CategoryProduct.MVC.Tests --filter "FullyQualifiedName~ErrorPageTests"`; all green. If the xunit double-fixture assumption (research R7) fails, apply the documented fallback (do not derive from `MvcTestBase`).

**Checkpoint (US1)**: error page tests green.

---

## Phase 4: User Story 2 - Users pages react to expired session (Priority: P1)

**Goal**: Users list and role change handle API 401 like the other controllers.

**Independent Test**: `dotnet test CategoryProduct.MVC.Tests --filter "FullyQualifiedName~UsersTests|FullyQualifiedName~SessionExpiryTests"`

- [ ] T010 [P] [US2] Add tests to `MVCT/UsersTests.cs` per `contracts/users-session-expiry.md` (write first; they fail or do not compile-fail now): Index 401 + refresh 401 -> login redirect with `returnUrl=/Users?page=2&search=al`, one refresh call, cookie cleared; Index 401 + refresh 200 -> retry with new bearer, 200; ChangeRole 401 + refresh 401 -> login redirect with the list `returnUrl`, cookie cleared; ChangeRole 401 + refresh 200 -> retried, redirect `/Users?page=2&search=al`; Index 403 -> `/Account/AccessDenied`; Index 404 -> exception surfaces (`await Assert.ThrowsAnyAsync<HttpRequestException>(...)`, check how `ApiUnavailableTests` or other tests assert thrown exceptions first and copy that pattern). (FR-011, SC-004)
- [ ] T011 [P] [US2] Add a Theory row `Users GET Index` (`/Users?page=2`, page route `api/users`, `ApiJson.Paged(new[] { ApiJson.User(1, "alice") }, 2, 10, totalPages: 2)`) to the `Scenarios()` list in `MVCT/SessionExpiryTests.cs`. Do not add ChangeRole there (research R5).
- [ ] T012 [US2] `MVC/Services/CategoryProductApiClient.cs`: change `GetUsersAsync` to `Task<ApiResult<PagedResult<UserItem>>>` implemented with `GetAsync<PagedResult<UserItem>>(...)` and the identical URL. (FR-004) Build; compile errors point at `UsersController` (fixed next).
- [ ] T013 [US2] `MVC/Controllers/UsersController.cs`: add `using` for `Microsoft.AspNetCore.Authentication` and `...Cookies`; handle `Unauthorized` and `Forbidden` and `!Success` in `Index`; `Unauthorized` in `ChangeRole` with `Url.Action(nameof(Index), new { page, search })`; add `SessionExpiredAsync(string? returnUrl = null)`. Keep every other behavior. (FR-005, FR-006)
- [ ] T014 [US2] Run the filter above; all Users and SessionExpiry tests green including the unchanged 021 ones.

**Checkpoint (US2)**.

---

## Phase 5: User Story 3 - Products show API 409 details (Priority: P1)

**Goal**: 409 detail appears on create, edit, delete, upload and remove image.

**Independent Test**: `dotnet test CategoryProduct.MVC.Tests --filter "FullyQualifiedName~ProductsTests|FullyQualifiedName~ProductImageTests"`

- [ ] T015 [P] [US3] `MVCT/ProductsTests.cs`: rename the three `*_409_rerenders_without_exception` tests to `*_409_shows_detail` and assert the unique detail text through `HtmlHelpers.FieldError(html, "")` (create: "Product name already exists."; edit: "Conflict while updating."; delete: "Product is referenced by orders." plus the Delete page text); add the empty/non-JSON-body test expecting "The request conflicts with existing data." (use `FakeResponse.Raw(409, "not json", "text/plain")` or `FakeResponse.Status(409)`). (FR-011, SC-003)
- [ ] T016 [P] [US3] `MVCT/ProductImageTests.cs`: strengthen `Upload_api_409_rerenders_details_without_exception` (rename to `..._shows_detail`, text "Image conflict on upload."); add `RemoveImage_api_409_shows_detail` (admin, `ScriptProduct(1)`, `DELETE api/product/1/image` -> 409 "Image conflict on remove.", form posted with the token page `/Products/Details/1`, expect 200 and the text).
- [ ] T017 [US3] Run the filter above; the new/renamed tests must FAIL before T018 (proof they detect the defect), then:
- [ ] T018 [US3] `MVC/Controllers/ProductsController.cs`: add the Conflict branch to `AddErrors` per `contracts/products-conflict.md`. (FR-007)
- [ ] T019 [US3] Re-run the filter; all Products and ProductImage tests green.

**Checkpoint (US3)**.

---

## Phase 6: User Story 4 - Logout sends bearer without refreshing (Priority: P2)

**Goal**: logout carries the access token and never triggers a refresh or retry.

**Independent Test**: `dotnet test CategoryProduct.MVC.Tests --filter "FullyQualifiedName~AccountTests|FullyQualifiedName~TokenRefreshHandlerTests|FullyQualifiedName~ApiUnavailableTests"`

- [ ] T020 [P] [US4] `MVCT/TokenRefreshHandlerTests.cs`: add the two skip-option tests from `contracts/logout-bearer.md` (bearer + option + token expiring in 30 s: inner once, refresh zero calls, no sign-in recorded; bearer + option + inner 401: 401 returned, inner once, refresh zero). Build the request with the existing `Req(...)` helper plus `req.Options.Set(TokenRefreshHandler.SkipRefreshOption, true)` (does not compile until T022). (FR-008)
- [ ] T021 [P] [US4] `MVCT/AccountTests.cs`: update `Logout_clears_cookie_and_revokes_refresh_token` (pass `token: "tok-1"` to `SignInUserAsync`, assert `Assert.Equal("Bearer tok-1", call.Authorization)` instead of `Assert.Null`); add: expiring token (30 s) with `api/account/refresh` scripted -> zero refresh calls, one logout with the old bearer and old refresh token, signed out; logout 401 with refresh scripted -> one logout call, zero refresh calls, signed out; empty refresh token -> no logout call, signed out. (FR-009, FR-011, SC-005)
- [ ] T022 [US4] `MVC/Services/TokenRefreshHandler.cs`: add `public static readonly HttpRequestOptionsKey<bool> SkipRefreshOption = new("cp.skip-refresh");` and the first-statement pass-through in `SendAsync` per `contracts/logout-bearer.md`. Nothing else changes. (FR-008)
- [ ] T023 [US4] `MVC/Services/CategoryProductApiClient.cs`: rewrite `LogoutAsync` per `contracts/logout-bearer.md` (bearer through `ApplyBearerToken`, option set, same early return and catch). Do this after T012 (same file). `AccountController.Logout` stays unchanged. (FR-009, FR-010)
- [ ] T024 [US4] Run the filter above; all logout and handler tests green, including the existing no-bearer pass-through handler test and the three existing logout failure tests.

**Checkpoint (US4)**.

---

## Phase 7: User Story 5 - Known Gaps and final verification (Priority: P3)

- [ ] T025 [US5] Update `specs/021-test-coverage/spec.md` Known Gaps: items 1-4 of "Open code defects" start with "FIXED by `022-mvc-bugfixes`." and keep their original text; items 5 and 6 unchanged; add short notes "(changed by `022-mvc-bugfixes`: logout now sends the bearer)" after US2 scenario 4's "(and no bearer)" and in FR-007's "logout", and fix the Edge Cases lines "Unhandled exceptions are not turned into an error page" and "The Users page does not handle API 401" with a "fixed by 022" note. Also update the sentence in Assumptions "Environment `Development`" with a pointer: 022 adds `ProductionMvcFactory`. (FR-014, SC-006 limits this to specs)
- [ ] T026 [P] [US5] `specs/021-test-coverage/contracts/mvc-test-matrix.md`: Logout rows say "no Authorization"; change to "Authorization is the access token" with "(022)", and the Users rows if they mention missing 401 handling.
- [ ] T027 Run `dotnet build CategoryProduct.slnx` (zero errors, no new warnings in touched files).
- [ ] T028 Run `dotnet test CategoryProduct.slnx` (all green; test count greater than the T001 baseline). Run the MVC project twice to catch order or flake issues.
- [ ] T029 `git status` review: only files from the "Source changes" list of `plan.md` changed or were created; no `.env`, no `logs/` folder, no temp files, no change under `CategoryProduct.API`, `docker-compose.yml` or `.github`. Confirm `CategoryProduct.MVC/Program.cs` is untouched.
- [ ] T030 Complete `specs/022-mvc-bugfixes/spec.md` Known Gaps with the final facts (actual test counts, any deviation found while implementing, e.g. the xunit fixture fallback of research R7) and tick the checklist `checklists/requirements.md` notes if anything changed.
- [ ] T031 After pushing (when the user pushes), confirm `build-test`, `docker-build`, `sqlserver-tests` and `compose-smoke` are green and record the run in Known Gaps (same style as 021); `compose-smoke` loads `/` of the MVC, which still returns 200 (`Categories/Index`), so it is a regression check for the new controller's routing.

---

## Dependencies and execution order

- T001 first; T002 any time before T003.
- T003 before T004 and T005. T005 depends on T004 (needs `ProductionMvcFactory`).
- US1 (T005-T009), US2 (T010-T014), US3 (T015-T019) and US4 (T020-T024) are independent of each other; exception: T023 edits the same file as T012, so run them in sequence (any order, not in parallel).
- Inside each story: tests first (T005, T010-T011, T015-T017, T020-T021) then production code, then the run task.
- T025-T026 can start once the behavior is final; T027-T031 last.

## Parallel example

Agent A: T003-T009 (US1). Agent B: T010-T014 (US2). Agent C: T015-T019 (US3) and then T020-T022 (US4 handler and tests). T023 after T012. T025-T031 by whoever finishes last.

## Implementation strategy

MVP = US1 (the only fix that changes visible behavior for every user on any failure). Each story is a self-contained commit-ready unit; run its filter before moving on and run the whole solution once at the end (T028). Keep the unchanged 021 tests green at every checkpoint: they are the regression net for the shared `TokenRefreshHandler` and `CategoryProductApiClient`. Do not change `Program.cs`, the API, compose or CI.
