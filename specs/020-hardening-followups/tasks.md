---

description: "Task list for 020-hardening-followups"
---

# Tasks: Hardening Follow-ups

**Input**: Design documents from `/specs/020-hardening-followups/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Requested. Within each story, test tasks come first and must fail (or not compile) before the implementation tasks.

**Organization**: grouped by user story. Parallel ownership (no shared files between groups):

| Group | Stories | Files touched |
|---|---|---|
| A | US1 | `CategoryProduct.API.SqlServerTests/**` (new), `.github/workflows/ci.yml` |
| B | US2 | `CategoryProduct.API/Controllers/AccountController.cs`, `CategoryProduct.API/appsettings.json`, `CategoryProduct.API.Tests/ApiFactory.cs`, `CategoryProduct.API.Tests/RefreshGraceTests.cs` (new) |
| C | US3 | `CategoryProduct.API/Controllers/ProductController.cs`, `CategoryProduct.API.Tests/ProductImageConcurrencyTests.cs` (new) |
| D | US4 | `CategoryProduct.MVC/Program.cs`, `CategoryProduct.MVC/Dockerfile`, `docker-compose.yml` |
| Polish | all | the five older `spec.md` files, this folder |

Groups A-D can be implemented by different agents at the same time. Do NOT add the new project to `CategoryProduct.slnx`.

## Format: `[ID] [P?] [Story] Description`

## Phase 1: Setup

- [X] T001 [US1] Create `CategoryProduct.API.SqlServerTests/CategoryProduct.API.SqlServerTests.csproj` (net10.0, ImplicitUsings, Nullable, `IsPackable=false`; packages coverlet.collector 6.0.4, Microsoft.AspNetCore.Mvc.Testing 10.0.10, Microsoft.NET.Test.Sdk 17.14.1, xunit 2.9.3, xunit.runner.visualstudio 3.1.4; `Using Include="Xunit"`; ProjectReference `..\CategoryProduct.API\CategoryProduct.API.csproj`). No InMemory package. Do not edit `CategoryProduct.slnx`.
- [X] T002 [P] [US1] Create `CategoryProduct.API.SqlServerTests/AssemblyInfo.cs` with `[assembly: CollectionBehavior(DisableTestParallelization = true)]`.

## Phase 2: Foundational

- [X] T003 [US1] Create `CategoryProduct.API.SqlServerTests/SqlServerApiFactory.cs` per `contracts/ci-and-config.md` section 2: read `SQLSERVER_TEST_CONNECTION` (throw `InvalidOperationException` with the documented message if missing/blank), unique database name `CategoryProductTest_<guid N>`, test-only `Jwt:Key/Issuer/Audience` and rate-limit settings (1000), remove API `DbContextOptions` descriptors (same filter as `CategoryProduct.API.Tests/ApiFactory.cs`), `AddDbContext` with `UseSqlServer`, run `Database.MigrateAsync()` on a temporary provider before host build, `Dispose(bool)` calls `EnsureDeletedAsync`. Helpers: `CreateUserAsync(name, role)` (PasswordHasher), `LoginAsync(name)`, `WithDbAsync`, `SeedProductAsync(quantity)`, `ConnectionString`, `DatabaseName`.
- [X] T004 [US1] Add a smoke test `CategoryProduct.API.SqlServerTests/MigrationTests.cs::Factory_StartsAndServesHealth` (GET `/health` -> 200) to prove T003 works.

**Checkpoint**: US1 foundation ready. Groups B, C, D have no foundational dependency and can start immediately.

## Phase 3: User Story 1 - SQL Server tests in CI (P1)

### Tests (all in `CategoryProduct.API.SqlServerTests/`)

- [X] T005 [P] [US1] `MigrationTests.cs`: on a new empty database created by the test (own `ApplicationDbContext`, `MigrateAsync`, dropped in `finally`), assert `GetPendingMigrationsAsync()` empty, `GetAppliedMigrationsAsync()` count equals `GetMigrations().Count()`, and `Database.HasPendingModelChanges()` is false.
- [X] T006 [P] [US1] `CheckoutSqlServerTests.cs::InsufficientStock_ChangesNothing`: cart line quantity above stock, `POST api/orders` -> 409; assert 0 orders, 0 stock movements for the product, cart lines intact, product quantity unchanged.
- [X] T007 [P] [US1] `CheckoutSqlServerTests.cs::ParallelCheckoutOfSameCart_CreatesExactlyOneOrder`: N=8 parallel `POST api/orders` released through a gate; exactly one 201, others in {400, 409}; one order, one `Sale` movement, quantity deducted once, cart empty.
- [X] T008 [P] [US1] `StockMovementSqlServerTests.cs::ParallelMovements_FinalQuantityEqualsInitialPlusSuccesses`: N=10 parallel `POST api/product/{id}/stock-movements` with positive changes (distinct values); only 201 and 409 allowed; final quantity == initial + sum of 201 changes; movement count == number of 201; ordered by Id the `QuantityAfter` chain is consistent.
- [X] T009 [P] [US1] `AdminDemotionSqlServerTests.cs::ParallelDemotionOfOnlyTwoAdmins_LeavesAtLeastOneAdmin`: delete seeded `testuser` first (it is an Admin), create exactly two admins, two parallel `PUT api/users/{id}/role` `{ "role": "User" }` (each demoting the other or self, both authenticated as admins); assert admin count >= 1 and at most one 200; write statuses to test output.
- [X] T010 [P] [US1] `RefreshTokenSqlServerTests.cs::ParallelRefreshWithSameToken_AtMostOne200`: login, N=8 parallel refresh with one token; count of 200 <= 1 and all others 401.
- [X] T011 [P] [US1] `ProductDeleteSqlServerTests.cs`: (a) product referenced by an order item: `DELETE api/product/{id}` as admin -> 200; order and item remain, `OrderItems.ProductId` is NULL; (b) cart lines of that product removed (cascade); (c) its `StockMovements` removed (cascade, 018 gap).
- [X] T012 [P] [US1] `CartConstraintSqlServerTests.cs::DuplicateCartRow_IsRejectedByUniqueIndex`: insert two `CartItem` rows for the same user and product through `ApplicationDbContext`; second save throws `DbUpdateException` whose inner `SqlException.Number` is 2601 or 2627. Also assert concurrent `POST api/cart/items` for the same pair end with one row.

### Implementation

- [X] T013 [US1] Add the `sqlserver-tests` job and workflow-level `SQLSERVER_CI_SA_PASSWORD` to `.github/workflows/ci.yml` exactly as in `contracts/ci-and-config.md` section 1; leave `build-test` and `docker-build` untouched (diff must be additive).
- [X] T014 [US1] Run `dotnet build CategoryProduct.API.SqlServerTests` (compiles locally; tests cannot run without SQL Server) and `dotnet test CategoryProduct.slnx` (still green, project not included). Record in the final summary that the SQL tests and CI job were not executed locally.

## Phase 4: User Story 2 - Refresh grace window (P2)

### Tests

- [X] T015 [P] [US2] Add `protected virtual int RefreshReuseGraceSeconds => 0;` to `CategoryProduct.API.Tests/ApiFactory.cs` and `builder.UseSetting("Jwt:RefreshReuseGraceSeconds", RefreshReuseGraceSeconds.ToString());` in `ConfigureWebHost` so existing refresh tests keep their immediate-reuse behavior.
- [X] T016 [US2] Create `CategoryProduct.API.Tests/RefreshGraceTests.cs` with a `GraceFactory : ApiFactory` (grace 10, plus a second factory or setting for default-absent case) and tests, seeding `RevokedAtUtc` through `ApplicationDbContext` from `factory.Services`: (a) rotated token inside grace -> 401, revoked/row counts unchanged, new token still 200; (b) `RevokedAtUtc` 11+ s ago -> 401 and family revoked (new token then 401); (c) grace 0 -> immediate reuse revokes; (d) logout-revoked token reused immediately -> family revoked; (e) grace path adds no `RefreshToken` row; (f) key absent -> behaves as 10 s (use `UseSetting` with empty/removed key); (g) negative value behaves as 0. Run them and confirm they fail.

### Implementation

- [X] T017 [US2] In `CategoryProduct.API/Controllers/AccountController.cs` inject `IConfiguration`, read `Jwt:RefreshReuseGraceSeconds` (default 10), and in the `stored.RevokedAtUtc != null` branch return `Unauthorized()` before revoking when `grace > 0 && stored.ReplacedByTokenHash != null && now - stored.RevokedAtUtc.Value < TimeSpan.FromSeconds(grace)` (per `contracts/refresh-grace.md`).
- [X] T018 [P] [US2] Add `"RefreshReuseGraceSeconds": 10` to the `Jwt` section of `CategoryProduct.API/appsettings.json`.
- [X] T019 [US2] Run `dotnet test CategoryProduct.API.Tests`: grace tests and all existing refresh tests green.

## Phase 5: User Story 3 - Image endpoints 409 (P2)

### Tests

- [X] T020 [US3] Create `CategoryProduct.API.Tests/ProductImageConcurrencyTests.cs` (fixture `InventoryTests.RacingFactory`, admin client like `ProductImageTests`): (a) arm interceptor, `POST api/product/{id}/image` with a valid tiny PNG -> 409 `ProblemDetails`, `ImageFileName` unchanged in the database, and no new file under `factory.ImageStoragePath`; (b) product with an image, arm, `DELETE api/product/{id}/image` -> 409, reference and file kept; (c) disarmed requests still return 200 and 204. Confirm (a) and (b) currently fail with 500.

### Implementation

- [X] T021 [US3] In `CategoryProduct.API/Controllers/ProductController.cs` `UploadImage`: add `catch (DbUpdateConcurrencyException) { _storage.Delete(newFileName); return ConcurrencyConflict(); }` before the existing generic `catch { delete; throw; }`.
- [X] T022 [US3] In the same file `DeleteImage`: wrap `SaveChangesAsync` in `try/catch (DbUpdateConcurrencyException) { return ConcurrencyConflict(); }`, keeping `_storage.Delete(fileName)` after a successful save.
- [X] T023 [US3] Run `dotnet test CategoryProduct.API.Tests` (all green).

## Phase 6: User Story 4 - MVC Data Protection keys (P3)

- [X] T024 [P] [US4] In `CategoryProduct.MVC/Program.cs` add the conditional `AddDataProtection().SetApplicationName("CategoryProduct.MVC").PersistKeysToFileSystem(new DirectoryInfo(path))` driven by `DataProtection:KeysPath` (no call when unset). No MVC test project exists; no new package.
- [X] T025 [P] [US4] In `CategoryProduct.MVC/Dockerfile` add `RUN mkdir -p /data/dp-keys && chown $APP_UID /data/dp-keys` before `USER $APP_UID`.
- [X] T026 [P] [US4] In `docker-compose.yml` add `DataProtection__KeysPath: /data/dp-keys` and `volumes: - mvckeys:/data/dp-keys` to `mvc`, and `mvckeys:` under top-level `volumes`.
- [X] T027 [US4] Run `dotnet build CategoryProduct.slnx`; Docker verification is manual (quickstart) and cannot be done here.

## Phase 7: Polish and cross-cutting

- [X] T028 [P] Update `specs/015-auth-hardening/spec.md` Known Gaps: mark "No grace window..." and "The concurrency token only protects..." as closed by `020-hardening-followups` US2; mark the SQL Server concurrency/transaction verification and parallel demotion gaps as covered by 020 US1 tests (pending the first CI run).
- [X] T029 [P] Update `specs/016-docker-ci/spec.md` Known Gaps: Data Protection keys item closed by 020 US4 (unverified on Docker); note the new `sqlserver-tests` job in 020 is also unverified until the first run.
- [X] T030 [P] Update `specs/017-product-images/spec.md` Known Gaps: note image-endpoint concurrency now returns 409 (020 US3); keep the other gaps.
- [X] T031 [P] Update `specs/018-inventory/spec.md` Known Gaps: the image endpoints 500-instead-of-409 item closed by 020 US3; the cascade, `SetNull` and concurrent-write item covered by 020 US1 tests (pending CI).
- [X] T032 [P] Update `specs/019-cart-orders/spec.md` Known Gaps: same-cart double checkout, atomicity/rollback, unique cart index, `SetNull` and cascade items covered by 020 US1 tests (pending CI); T049 stays open until the CI run is green.
- [X] T033 Run `dotnet ef migrations has-pending-model-changes --project CategoryProduct.API` (no changes) and `dotnet test CategoryProduct.slnx`; confirm `git diff` shows no change to `CategoryProduct.slnx` and no new migration.
- [ ] T034 After the first push, check the `sqlserver-tests` job result on GitHub, fix timing/quoting issues, then update spec 020 Known Gaps and the five older specs accordingly.

## Dependencies and execution order

- Phase 1 -> Phase 2 -> Phase 3 (US1) is sequential within group A; T005-T012 are [P] once T003 exists; T013 is independent of the test files.
- Groups B, C and D do not depend on A or on each other and may run concurrently (disjoint files, see the table above).
- Within B: T015 and T016 before T017; T018 any time.
- Phase 7 after all groups; T034 only after a real CI run.
