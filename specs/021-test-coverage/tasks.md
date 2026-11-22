---

description: "Task list for 021-test-coverage"
---

# Tasks: Test Coverage

**Input**: Design documents from `/specs/021-test-coverage/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: This feature IS tests. Test classes are the deliverable; the only production edits are two tiny seams (T003, T039) and CI/spec files.

**Organization**: grouped by implementation group so up to 3 agents can work in parallel after G1 finishes. Groups own disjoint files (table below); no group edits a file owned by another.

## Group and file ownership

| Group | Stories | Owns (create/edit) | Starts after |
|---|---|---|---|
| **G1** Infrastructure (blocking) | US1 | `CategoryProduct.MVC.Tests/CategoryProduct.MVC.Tests.csproj`, `CategoryProduct.slnx`, `CategoryProduct.MVC/Program.cs`, `CategoryProduct.MVC.Tests/Support/FakeApi.cs`, `Support/MvcFactory.cs`, `Support/MvcTestBase.cs`, `Support/HtmlHelpers.cs`, `Support/ApiJson.cs`, `CategoryProduct.MVC.Tests/InfrastructureTests.cs` | nothing |
| **G2** Account + catalog | US2 (account/anonymous part), US3 | `CategoryProduct.MVC.Tests/AccountTests.cs`, `AnonymousAccessTests.cs`, `CategoriesTests.cs`, `ProductsTests.cs`, `ProductImageTests.cs`, `UsersTests.cs` | G1 checkpoint |
| **G3** Inventory, cart, orders, session, handlers | US2 (session/handler part), US4 | `CategoryProduct.MVC.Tests/InventoryTests.cs`, `CartTests.cs`, `OrdersTests.cs`, `ManageOrdersTests.cs`, `SessionExpiryTests.cs`, `ApiUnavailableTests.cs`, `TokenRefreshHandlerTests.cs`, `ApiErrorHandlerTests.cs`, `AuthSessionServiceTests.cs`, `Support/RecordingAuthenticationService.cs`, `Support/StubHttpClientFactory.cs`, `Support/StubHandler.cs` | G1 checkpoint (handler unit tests T030-T032 need only T001-T003 and can start earlier) |
| **G4** CI + API gaps + docs | US5, US6, US7 | `.github/workflows/ci.yml`, `CategoryProduct.API/Services/RefreshTokenCleanupWorker.cs`, `CategoryProduct.API.Tests/RefreshTokenCleanupWorkerTests.cs`, `RefreshEdgeCaseTests.cs`, `ApiFactory.cs`, `CategoryProduct.API.Tests.csproj`, `specs/013..020/*/spec.md`, `specs/021-test-coverage/spec.md` (Known Gaps update) | nothing (independent of G1-G3) |

G4 can start immediately in parallel with G1. G2 and G3 start together once G1's checkpoint (T010) is green. Do not add `CategoryProduct.API.SqlServerTests` to the solution. Run `dotnet test CategoryProduct.slnx` only once all groups are merged; during parallel work each group runs its own filter (`dotnet test CategoryProduct.MVC.Tests --filter "FullyQualifiedName~<Class>"`).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency inside its group)
- **[Story]**: US1..US7 from spec.md

---

## Phase 1: Setup (G1)

- [X] T001 [US1] Create `CategoryProduct.MVC.Tests/CategoryProduct.MVC.Tests.csproj`: `Microsoft.NET.Sdk`, net10.0, `ImplicitUsings`, `Nullable`, `IsPackable=false`; packages `coverlet.collector` 6.0.4, `Microsoft.AspNetCore.Mvc.Testing` 10.0.10, `Microsoft.NET.Test.Sdk` 17.14.1, `xunit` 2.9.3, `xunit.runner.visualstudio` 3.1.4 (copy the item group from `CategoryProduct.API.Tests.csproj`, drop `Microsoft.EntityFrameworkCore.InMemory`); `<Using Include="Xunit" />`; `ProjectReference` to `..\CategoryProduct.MVC\CategoryProduct.MVC.csproj` only (never the API).
- [X] T002 [US1] Add `<Project Path="CategoryProduct.MVC.Tests/CategoryProduct.MVC.Tests.csproj" />` to `CategoryProduct.slnx`. Verify `dotnet build CategoryProduct.slnx` and that `dotnet test CategoryProduct.slnx --list-tests` lists both test projects. (FR-001)
- [X] T003 [US1] Append `public partial class Program { }` as the last line of `CategoryProduct.MVC/Program.cs`. Confirm the MVC project still builds and the test project sees `Program` unambiguously (FR-002, research R3).

## Phase 2: Foundational (G1, blocking)

**CRITICAL**: G2 and G3 cannot start before T010 is green.

- [X] T004 [US1] `Support/FakeApi.cs` per `contracts/fake-api.md`: `FakeApi`, `FakeResponse`, `RecordedRequest`, `FakeApiUnmatchedException`; `On`, `OnSequence`, `OnQuery`, `Throw`, `Hang`, `Reset`, `Requests`, `Calls`, `Single`; thread-safe recording; body read in the handler; no-op `Dispose(bool)`.
- [X] T005 [P] [US1] `Support/ApiJson.cs`: builders listed in `data-model.md` (login with `expiresInSeconds`, paged, category, product, stock movement, cart/cart line, order/order line/summary, user, validation problem, conflict problem). camelCase via `JsonSerializerDefaults.Web`.
- [X] T006 [P] [US1] `Support/HtmlHelpers.cs`: `ExtractAntiforgeryToken`, `Decode`, `HasText`, `Alert(html, kind)`, `FormAction`, `FieldError(html, field)` (research R9).
- [X] T007 [US1] `Support/MvcFactory.cs`: `WebApplicationFactory<Program>` with settings from FR-003 / `data-model.md`, `ConfigureHttpClientDefaults` primary handler = the one `FakeApi` (research R2), temp-dir cleanup in `Dispose`. If `MapStaticAssets` fails at startup, apply the mitigation in research R4 and record the result there.
- [X] T008 [US1] `Support/MvcTestBase.cs`: `IClassFixture<MvcFactory>`; constructor resets `Fake` and creates `Client` (`AllowAutoRedirect=false`, `HandleCookies=true`); helpers `GetAsync`, `GetHtmlAsync`, `PostFormAsync(url, fields, tokenPageUrl?)` (fetches the token from `tokenPageUrl ?? url`, adds `__RequestVerificationToken`), `PostMultipartAsync(url, fields, fileName, contentType, bytes, tokenPageUrl)`, `ScriptLogin(role, expiresInSeconds, token, refresh)`, `SignInUserAsync(name = "alice")`, `SignInAdminAsync(name = "root")` (real login form, asserts 302 + auth cookie), `FollowAsync(response)` (GET the `Location`), `AssertRedirect(response, pathAndQuery)`, `AssertLoginRedirect(response, returnPath)`, `NewAnonymousClient()`. Expose `Fake`, `Client`, `Factory`.
- [X] T009 [US1] `InfrastructureTests.cs` (self-tests, contracts/mvc-test-matrix.md last row): factory boots (`GET /Account/Login` 200 with token); FakeApi route/sequence/query/throw behaviors; unscripted call throws `FakeApiUnmatchedException` naming method and path; recording of Authorization and JSON/multipart body; `SignInAdminAsync` then `GET /Users` renders (script `api/users`) and sends Bearer; second test in the same class starts with an empty request log and no cookies.
- [X] T010 [US1] Run `dotnet test CategoryProduct.MVC.Tests`, fix infrastructure issues, commit-ready. **Checkpoint**: G2 and G3 may start.

---

## Phase 3: User Story 2 - Account, anonymous access (Priority: P1) (G2)

**Goal**: `AccountController` and authorization redirects covered (`contracts/mvc-test-matrix.md` AccountTests, AnonymousAccessTests).

- [X] T011 [P] [US2] `AccountTests.cs`: every row of the AccountTests table (login ok User/Admin, local vs external `returnUrl`, 401, 429, empty fields without API call, missing antiforgery token 400, register ok / login-after-register fails / 409 / 429 / 400 field and non-field keys / client-invalid input, logout ok / throws / 500, AccessDenied, cookie expiry follows refresh token expiry).
- [X] T012 [P] [US2] `AnonymousAccessTests.cs`: the Theory tables of AnonymousAccessTests (anonymous GET and POST redirects with `ReturnUrl`, non-admin AccessDenied for GET and POST, public pages not redirecting); assert `Fake.Requests` is empty for all denied cases.

## Phase 4: User Story 3 - Catalog pages (Priority: P1) (G2)

**Goal**: Categories, Products, images, Users (`contracts/mvc-test-matrix.md`).

- [X] T013 [P] [US3] `CategoriesTests.cs`: all CategoriesTests rows (index query/paging/search/clamp, admin-only links, details, create/edit/delete happy + 400/409/403/404/401, invalid model without API call, bearer on writes and none on public GETs).
- [X] T014 [P] [US3] `ProductsTests.cs`: all ProductsTests rows incl. query string with invariant decimals, category names resolved from `api/category`, hidden/visible controls by role, create/edit/delete, Edit `readonly` Quantity + hint + posted quantity + 400 on `Quantity` displayed, 409 re-render without asserting message.
- [X] T015 [P] [US3] `ProductImageTests.cs`: all ProductImageTests rows (proxy bytes + headers + 404 + 503, upload multipart assertions on `BodyText`/`BodyBytes`, no-file message without API call, API 400 message, remove, non-admin denied, no `fake-api` host in any rendered page).
- [X] T016 [P] [US3] `UsersTests.cs`: all UsersTests rows (list, search, paging links, role change success/409/400/404/403 flash messages via `FollowAsync`, non-admin denied).

**Checkpoint (G2)**: `dotnet test CategoryProduct.MVC.Tests --filter "FullyQualifiedName~AccountTests|FullyQualifiedName~AnonymousAccessTests|FullyQualifiedName~CategoriesTests|FullyQualifiedName~ProductsTests|FullyQualifiedName~ProductImageTests|FullyQualifiedName~UsersTests"` green.

---

## Phase 5: User Story 4 - Inventory, cart, orders (Priority: P1) (G3)

- [X] T017 [P] [US4] `InventoryTests.cs`: all InventoryTests rows (banner, badges, paging, history table, add movement 201/409/400/403/429/404/401, user denied, 401 -> login redirect).
- [X] T018 [P] [US4] `CartTests.cs`: all CartTests rows (index, empty, stock badge, add with local/external `returnUrl`, update, remove, clear, checkout success -> `/Orders/{id}` + "Order placed.", 409 detail, 400 -> "Your cart is empty.", 401, anonymous POST).
- [X] T019 [P] [US4] `OrdersTests.cs`: all OrdersTests rows (my orders paging, details Pending vs others, removed product marker, admin view extras, 404, cancel ok/409/404, `returnTo=manage` for admin vs user).
- [X] T020 [P] [US4] `ManageOrdersTests.cs`: all ManageOrdersTests rows (filter normalization `pending` -> `Pending`, `bogus` -> empty, paging keeps filter, confirm/cancel return to page+filter, 409/400 messages, denial).

## Phase 6: User Story 2 - Session and handlers (Priority: P1) (G3)

- [X] T030 [P] [US2] `Support/StubHandler.cs` (delegate-based `HttpMessageHandler` with call counter and captured requests/bodies), `Support/StubHttpClientFactory.cs`, `Support/RecordingAuthenticationService.cs` (records `SignInAsync` principal/properties and `SignOutAsync`).
- [X] T031 [US2] `TokenRefreshHandlerTests.cs`: all handler cases in the matrix (`DefaultHttpContext` with claims and `RequestServices` containing the recording authentication service; `InnerHandler = StubHandler`; refresh through `StubHttpClientFactory` for `TokenRefreshHandler.RefreshClientName`). Include the retry-body equality check (same bytes and content type) and "second refresh never attempted in the same `HttpContext`".
- [X] T032 [P] [US2] `ApiErrorHandlerTests.cs` (incl. `ApiUnavailableFilter`) and `AuthSessionServiceTests.cs` per the matrix.
- [X] T033 [P] [US2] `SessionExpiryTests.cs`: Theory over the listed actions (401 + failed refresh -> login redirect with `returnUrl`, one refresh call, cookie cleared; 401 + successful refresh -> retry succeeds with new bearer and new cookie); plus the pre-emptive refresh scenario (login with `expiresInSeconds: 30`, first page call preceded by exactly one `POST api/account/refresh`, page call carries the new token) and the no-refresh scenario (`expiresInSeconds: 900`).
- [X] T034 [P] [US2] `ApiUnavailableTests.cs`: all ApiUnavailableTests rows (`Throw`, 500/502, `Hang`, authenticated cart 500, logout best-effort, login 503).

**Checkpoint (G3)**: `dotnet test CategoryProduct.MVC.Tests --filter "FullyQualifiedName~InventoryTests|FullyQualifiedName~CartTests|FullyQualifiedName~OrdersTests|FullyQualifiedName~ManageOrdersTests|FullyQualifiedName~SessionExpiryTests|FullyQualifiedName~ApiUnavailableTests|FullyQualifiedName~TokenRefreshHandlerTests|FullyQualifiedName~ApiErrorHandlerTests|FullyQualifiedName~AuthSessionServiceTests"` green.

---

## Phase 7: User Stories 5 and 6 - CI compose job and API gaps (G4, independent of G1-G3)

### CI (US5)

- [X] T035 [US5] Add job `compose-smoke` to `.github/workflows/ci.yml` exactly per `contracts/ci-compose-smoke.md` (behavior checklist 1-11). Do not touch the other jobs or the workflow-level `env`. Validate YAML syntax by review (no local Docker; no YAML linter assumed).
- [X] T036 [US5] Re-read the job against the checklist: masked values, no `cat .env`, `if: failure()` logs, `if: always()` teardown, `timeout-minutes`, ports equal `.env` values.

### API worker and edge cases (US6)

- [X] T037 [P] [US6] Write `CategoryProduct.API.Tests/RefreshTokenCleanupWorkerTests.cs` FIRST (must not compile or must fail): interval advance purges old and keeps recent tokens; nothing before the interval; repeats on the next interval; interval `0` and `-5` fall back to 60 minutes; a throwing first tick does not stop the worker; `StopAsync` completes; cancel before first tick purges nothing. Use `FakeTimeProvider` and a polling helper (<= 5 s, 10 ms steps), per research R6.
- [X] T038 [US6] Add `Microsoft.Extensions.TimeProvider.Testing` (10.x, `dotnet add package`, pick a version released at least two weeks ago) to `CategoryProduct.API.Tests.csproj`.
- [X] T039 [US6] Change `CategoryProduct.API/Services/RefreshTokenCleanupWorker.cs`: optional ctor parameter `TimeProvider? timeProvider = null`, `_time = timeProvider ?? TimeProvider.System`, `new PeriodicTimer(interval, _time)`, `PurgeAsync(_time.GetUtcNow().UtcDateTime, ...)`. No other change. Confirm the host still starts (`HealthTests`, all existing tests green). If this production edit is rejected, replace T037 with a start/stop-only test and keep the gap documented (spec Assumptions).
- [X] T040 [P] [US6] `CategoryProduct.API.Tests/ApiFactory.cs`: add `protected virtual int RefreshPermitLimit => 1000;` and use it for `RateLimiting:RefreshPermitLimit` (replaces the literal `"1000"`).
- [X] T041 [US6] `CategoryProduct.API.Tests/RefreshEdgeCaseTests.cs`: expired refresh token -> 401 (seed a token with `ExpiresAtUtc` in the past via the factory's DbContext); refresh for a deleted user -> 401; `{}` body -> assert the real status (expected 400 from `[ApiController]`; fix the assertion to what the code returns and name it); refresh above `RefreshPermitLimit` (subclass factory, limit 2) -> third call 429; `GET api/users` without token -> 401. Existing tests must stay green.

**Checkpoint (G4)**: `dotnet test CategoryProduct.API.Tests` green; `ci.yml` reviewed.

---

## Phase 8: User Story 7 and Polish (G4, after Phase 7; all other groups merged for the final run)

- [X] T042 [US7] Update Known Gaps (same style as 020: `COVERED by \`021-test-coverage\` (pending the first CI run). Original gap: ...`; keep unrelated items as they are):
  - `specs/013-user-registration-management/spec.md`: MVC Register and Users pages no tests -> COVERED (AccountTests, UsersTests); `GET api/users` 401 -> COVERED (RefreshEdgeCaseTests); add note that Users 401 handling in MVC is a new Known Gap (021).
  - `specs/014-refresh-tokens/spec.md`: MVC `TokenRefreshHandler`, `AuthSessionService`, logout no tests -> COVERED; expired-token, deleted-user refresh, `429` on refresh -> COVERED; missing `refreshToken` body status -> COVERED (state the status found); reuse-after-logout already covered by 015.
  - `specs/016-docker-ci/spec.md`: "Dockerfiles, compose, workflow NOT built or run" -> compose run in CI by `compose-smoke` (pending first run); `api`/`mvc` have no healthcheck stays.
  - `specs/017-product-images/spec.md`: MVC image pages (Details upload/remove, Index thumbnails, `Products/Image/{id}` proxy) not verified -> COVERED for server-rendered behavior (ProductImageTests); Docker volume persistence now exercised only by compose-smoke start-up (not persistence across recreate); 413 stays manual.
  - `specs/018-inventory/spec.md`: "MVC Inventory pages were not run in a browser" -> COVERED server-side by InventoryTests; browser stays open.
  - `specs/019-cart-orders/spec.md`: "The MVC app has no test project" -> COVERED (CartTests, OrdersTests, ManageOrdersTests); T049 note unchanged.
  - `specs/020-hardening-followups/spec.md`: MVC Data Protection no test -> COVERED partially (MvcFactory sets `DataProtection:KeysPath`, so the branch executes; key persistence across restarts and Docker run are covered only by compose-smoke start-up); "MVC pages / TokenRefreshHandler after a lost race not verified in a browser" -> handler logic covered by unit tests, browser still open; `RefreshTokenCleanupWorker` mention if present.
- [X] T043 [US7] Update `specs/021-test-coverage/spec.md` Known Gaps with the final facts: actual test count, whether `MapStaticAssets` needed a mitigation (research R4), the real status for the empty refresh body, and (after the first push) the CI run result for all four jobs.
- [X] T044 Run the full local check: `dotnet build CategoryProduct.slnx`, `dotnet test CategoryProduct.slnx` (both test projects green, no Docker/SQL needed), `git status` shows no `.env`, no `logs/` under the test project, no temp files.
- [X] T045 After pushing, confirm `build-test`, `docker-build`, `sqlserver-tests`, `compose-smoke` green; if `compose-smoke` needs tuning (grep patterns, timeouts, JSON spacing) adjust `ci.yml` and `contracts/ci-compose-smoke.md` together and record it.

---

## Dependencies and execution order

- G1 (T001-T010) blocks G2 and G3 (except T030-T032, which need only the project from T001-T003 and `Program` from T003).
- G4 is independent of G1-G3 (T035-T041), but T042-T045 should run last.
- Inside G2 and G3 all test files are independent (marked [P]); T031 depends on T030.
- T037 before T039 (test first); T038 before T037 compiles; T040 before T041.

## Parallel example

Agent A: T001-T010 (G1). Agent B: T035-T041 (G4). When G1 is green: Agent C: T011-T016 (G2) and Agent A: T017-T020, T030-T034 (G3). T042-T045 by whoever finishes last.

## Implementation strategy

MVP = G1 + AccountTests + one controller test class (proves the approach); then fan out. Keep each test independent of ordering. If a view's markup breaks a regex helper, fix the helper in G1's `HtmlHelpers.cs` only through the G1 owner (or add a local private helper in the test file) to avoid file conflicts.
