---

description: "Task list for 023-security-cleanup"
---

# Tasks: Security Cleanup

**Input**: Design documents from `/specs/023-security-cleanup/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Requested. Each story writes its failing tests first, then the change.

**Organization**: grouped by user story. US1 (migration) and US2 (bootstrap) are both in `CategoryProduct.API` but touch different files; US3 is MVC only. US4 (docs) and the final verification come last.

## File ownership (for parallel work)

| Story | Production files | Test files |
|---|---|---|
| US1 migration | `API/Migrations/<ts>_NeutralizeSeededTestUser*.cs` | `API.SqlServerTests/SeededTestUserNeutralizationSqlServerTests.cs`, `API.Tests/SeededTestUserMarkerTests.cs`, `AdminDemotionSqlServerTests.cs` (comment) |
| US2 bootstrap | `API/Data/BootstrapAdmin.cs`, `API/Program.cs`, `docker-compose.yml`, `.env.example` | `API.Tests/ApiFactory.cs`, `API.Tests/BootstrapAdminTests.cs` |
| US3 429 | `MVC/Services/ApiRateLimit.cs`, `ApiErrorHandler.cs`, `MVC/Program.cs`, `Views/Shared/TooManyRequests.cshtml`, `UsersController.cs`, `ProductsController.cs` | `MVC.Tests/UsersTests.cs`, `ProductsTests.cs`, `ProductImageTests.cs`, `TooManyRequestsTests.cs` |
| US4 docs | none | `specs/008, 013, 016, 020, 021, 022, 023` |

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency)
- **[Story]**: US1..US4 from spec.md
- Paths relative to the repository root. `API` = `CategoryProduct.API`, `APIT` = `CategoryProduct.API.Tests`, `SQLT` = `CategoryProduct.API.SqlServerTests`, `MVC` = `CategoryProduct.MVC`, `MVCT` = `CategoryProduct.MVC.Tests`.

---

## Phase 1: Setup

- [X] T001 Record the baseline: `dotnet build CategoryProduct.slnx` and `dotnet test CategoryProduct.slnx` (expected 514 tests: 302 MVC, 212 API); `git status` clean on branch `023-security-cleanup`.
- [X] T002 [P] Re-verify the spec's code claims still hold (`grep -rn testuser`, `grep -rn EnableRateLimiting CategoryProduct.API`, `Program.cs` order of `DatabaseMigrator`/`app.Run()`); note any difference in spec Assumptions.

---

## Phase 2: User Story 1 - Neutralize the seeded testuser (P1)

**Independent Test**: `dotnet test CategoryProduct.API.Tests --filter "FullyQualifiedName~SeededTestUserMarkerTests"` (local); the SQL Server test on CI.

- [X] T003 [P] [US1] Write `APIT/SeededTestUserMarkerTests.cs` FIRST: `PasswordHasher<User>.VerifyHashedPassword(user, "/w==", "anything")` is `Failed` and does not throw; the seeded hash with a wrong password is `Failed`. (FR-004, FR-017)
- [X] T004 [P] [US1] Write `SQLT/SeededTestUserNeutralizationSqlServerTests.cs` per `contracts/neutralize-testuser-migration.md`: own database, `IMigrator.MigrateAsync("20261122120100_AddCartAndOrders")`, set up cases (seeded hash + tokens + an order; different hash + Admin; no `testuser`), `MigrateAsync()`, assert; plus the login-returns-401 case through `SqlServerApiFactory`. Cases fail/do not compile until T005-T006. (FR-017)
- [X] T005 [US1] Create the migration: `dotnet ef migrations add NeutralizeSeededTestUser --project CategoryProduct.API`. Confirm generated `Up`/`Down` are empty and `ApplicationDbContextModelSnapshot.cs` has no diff; do not edit anything else. (FR-001)
- [X] T006 [US1] In the new migration only: add the `migrationBuilder.Sql` call of the contract to `Up` (hash literal copied from `SeedTestUserForAuth`), and a comment-only `Down`. (FR-002, FR-003)
- [X] T007 [P] [US1] `SQLT/AdminDemotionSqlServerTests.cs`: update the comment ("migrations seed testuser as an Admin") to say the row may exist neutralized; logic unchanged. Grep SQLT for other `Role == "Admin"` count assumptions and confirm none depend on `testuser`.
- [X] T008 [US1] Run `dotnet build CategoryProduct.API.SqlServerTests` (compiles) and `dotnet test CategoryProduct.API.Tests --filter "FullyQualifiedName~SeededTestUserMarkerTests"`; run the SQL Server tests if `SQLSERVER_TEST_CONNECTION` is available, otherwise note "verified on CI" for the final step. Verify `git diff` shows the old migrations and the snapshot untouched. (SC-003)

**Checkpoint (US1)**.

---

## Phase 3: User Story 2 - Bootstrap admin (P1)

**Independent Test**: `dotnet test CategoryProduct.API.Tests --filter "FullyQualifiedName~BootstrapAdminTests"`

- [X] T009 [US2] `APIT/ApiFactory.cs`: add `protected virtual bool SeedAdmin => true;` and skip adding the `tester` Admin when false. Default behavior identical; run `dotnet test CategoryProduct.API.Tests` quickly to confirm.
- [X] T010 [US2] Write `APIT/BootstrapAdminTests.cs` FIRST per `contracts/bootstrap-admin.md` (helper-level with own InMemory provider and a collecting `ILogger`; host-level `NoAdminFactory`; parity theory against `POST api/account/register`; password-absent-from-logs assertion). They fail to compile until T011. (FR-017)
- [X] T011 [US2] Create `API/Data/BootstrapAdmin.cs` (enum + static helper) per the contract; explicit rule checks (not `Validator`), `PasswordHasher<Models.User>`, never throws, no password in any log or message. (FR-005, FR-007..FR-012)
- [X] T012 [US2] `API/Program.cs`: add `await BootstrapAdmin.EnsureIfConfiguredAsync(app.Services, app.Configuration, app.Logger);` immediately after the `DatabaseMigrator` call. (FR-006)
- [X] T013 [P] [US2] `docker-compose.yml`: add the two optional `Bootstrap__*` lines to `api.environment`; `.env.example`: add the commented placeholders per `contracts/bootstrap-config.md`. `docker compose config` is not available here; review the YAML by eye and confirm `.env.example` has no active real value. (FR-013)
- [X] T014 [US2] Run the filter above; all green, including `MigrationStartupTests` and the rest of `CategoryProduct.API.Tests`.

**Checkpoint (US2)**.

---

## Phase 4: User Story 3 - MVC 429 messages (P2)

**Independent Test**: `dotnet test CategoryProduct.MVC.Tests --filter "FullyQualifiedName~UsersTests|FullyQualifiedName~ProductsTests|FullyQualifiedName~ProductImageTests|FullyQualifiedName~TooManyRequestsTests"`

- [X] T015 [P] [US3] `MVCT/UsersTests.cs`: add the two 429 tests of `contracts/too-many-requests-pages.md` (list -> 429 page; role change -> redirect plus alert). Write first.
- [X] T016 [P] [US3] `MVCT/ProductsTests.cs`: add create/edit/delete 429 tests asserting `FieldError(html, "")`; `MVCT/ProductImageTests.cs`: upload and remove-image 429 tests. Write first.
- [X] T017 [P] [US3] Create `MVCT/TooManyRequestsTests.cs`: list, Details and category-lookup reads answering 429 -> 429 page; 404 and 503 unchanged; no exception text in the body. Write first.
- [X] T018 [US3] Run the filter: the new tests must FAIL before the change (proof they detect the gap).
- [X] T019 [US3] Create `MVC/Services/ApiRateLimit.cs` and `TooManyRequestsFilter` in `MVC/Services/ApiErrorHandler.cs`; register it in `MVC/Program.cs`; create `MVC/Views/Shared/TooManyRequests.cshtml`. (FR-015, FR-016)
- [X] T020 [US3] `MVC/Controllers/UsersController.cs` (Index throws the 429 `HttpRequestException` before `!Success`; ChangeRole TempData branch) and `MVC/Controllers/ProductsController.cs` (`AddErrors` branch). (FR-014)
- [X] T021 [US3] Re-run the filter; all green, and run the whole MVC project to prove Account/Cart/Inventory 429 tests are unchanged.

**Checkpoint (US3)**.

---

## Phase 5: Review and secret hygiene

- [X] T022 Grep the diff for secrets: no password, key or hash other than the seeded hash literal in the new migration; `.env.example` placeholders are commented; no `.env`, `logs/` or temp files added. (SC-006, Principle IV)
- [X] T023 `git status` review against the "Source changes" list of plan.md: no change to existing migrations, snapshot, `CategoryProduct.slnx`, CI workflow, or API controllers/DTOs. (FR-018)

---

## Phase 6: User Story 4 - Known Gaps and final verification (P3)

- [X] T024 [P] [US4] Mark testuser items fixed with "FIXED by `023-security-cleanup`" keeping the original text: `specs/016-docker-ci/spec.md` (risk at Assumptions line about `SeedTestUserForAuth` and the Known Gaps item "Migration `SeedTestUserForAuth` seeds its test user into the compose database"), `specs/020-hardening-followups/spec.md` (Known Gaps "Out of scope, still open: migration `SeedTestUserForAuth`..."), `specs/021-test-coverage/spec.md` (Assumption "Compose smoke scope ... known-password user": add a note that the user is neutralized, smoke still does not log in), `specs/013-user-registration-management/spec.md` (Assumption "The first admin is created outside this feature": add the bootstrap pointer), `specs/022-mvc-bugfixes/spec.md` (Out of Scope testuser bullet and Known Gaps "Seeded `testuser`"). Re-run `grep -rn "testuser" specs` to catch others.
- [X] T025 [P] [US4] Mark the 429 gap fixed in `specs/022-mvc-bugfixes/spec.md` (Out of Scope "Handling 429 on the Users and Products pages" and Known Gaps "429 on Users and Products"), noting the API does not rate-limit those endpoints. `grep -rn "429" specs` for any other open 429 gap on Users/Products.
- [X] T026 [P] [US4] Add one short "superseded by `023-security-cleanup`" note to `specs/008-role-authorization/quickstart.md` (the lines that tell the reader to use the seeded `testuser`) and, if needed, `specs/008-role-authorization/spec.md` Assumptions; do not rewrite history text.
- [X] T027 Run `dotnet build CategoryProduct.slnx` (0 errors, no new warnings in touched files) and `dotnet build CategoryProduct.API.SqlServerTests`.
- [X] T028 Run `dotnet test CategoryProduct.slnx` THREE times in a row; all green each time; record the test count (more than the 514 baseline).
- [X] T029 Complete `specs/023-security-cleanup/spec.md` Known Gaps with the final facts (test counts, whether the SQL Server test ran locally or only on CI, list of older specs updated) and tick `checklists/requirements.md`.
- [ ] T030 After pushing (when the user pushes), confirm `build-test`, `docker-build`, `sqlserver-tests` (the new neutralization test) and `compose-smoke` are green and record the run in Known Gaps in the same style as 022.

---

## Dependencies and execution order

- T001 first; T002 any time.
- US1 (T003-T008), US2 (T009-T014), US3 (T015-T021) are independent of each other. Inside a story: tests first, then production code, then the run task. T005 before T006; T009 before T010; T011 before T012.
- T022-T023 after all code. T024-T026 once behavior is final. T027-T030 last.

## Parallel example

Agent A: T003-T008 (migration). Agent B: T009-T014 (bootstrap). Agent C: T015-T021 (MVC). Whoever finishes last does T022-T030.

## Implementation strategy

MVP = US1 + US2 together (removing the known admin without a way to get a new one would strand operators). US3 is independent and lower risk. Keep every existing test green at each checkpoint; they are the regression net.
