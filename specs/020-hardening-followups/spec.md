# Feature Specification: Hardening Follow-ups

**Feature Branch**: `020-hardening-followups`

**Created**: 2026-11-22

**Status**: Draft

**Input**: User description: "Close the Known Gaps left by 015-auth-hardening, 016-docker-ci, 017-product-images, 018-inventory and 019-cart-orders: run real concurrency and constraint tests against SQL Server in CI, add a refresh-token grace window, make the image endpoints return 409 on a concurrent product change, and persist MVC Data Protection keys in Docker."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - SQL Server integration tests run in CI (Priority: P1)

A maintainer pushes a change and GitHub Actions runs a separate test project against a real SQL Server container. The tests check the behaviors EF InMemory cannot reproduce: migrations applying cleanly, transactional rollback, real concurrency, cascade and `SetNull` foreign keys, and the unique cart index.

**Why this priority**: 015, 018 and 019 each list SQL Server behavior as "verified manually" or "not verified". This is the only story that turns those claims into automated evidence.

**Independent Test**: With `SQLSERVER_TEST_CONNECTION` pointing at any SQL Server instance, `dotnet test CategoryProduct.API.SqlServerTests` passes. In CI the new `sqlserver-tests` job passes. `dotnet test CategoryProduct.slnx` still needs no database and does not include the new project.

**Acceptance Scenarios**:

1. **Given** an empty database, **When** migrations are applied, **Then** every migration in the API assembly is applied, `GetPendingMigrations` is empty and the model has no pending changes.
2. **Given** a cart whose line exceeds stock, **When** the user checks out, **Then** the response is 409 and there is no order, no stock movement, the cart is intact and product quantities are unchanged.
3. **Given** a stocked product and one cart, **When** N checkouts of that cart run in parallel, **Then** exactly one returns 201, exactly one order exists, stock is deducted once and the cart is empty.
4. **Given** one product, **When** N stock movements are posted in parallel, **Then** the final quantity equals the initial quantity plus the sum of the changes that returned 201 (409 responses contribute nothing) and the movement history forms a consistent chain.
5. **Given** exactly two admins, **When** both are demoted in parallel, **Then** at least one admin remains and at most one request returns 200.
6. **Given** one refresh token, **When** it is presented N times in parallel, **Then** at most one request returns 200.
7. **Given** a product referenced by an order and by cart lines, **When** the product is deleted, **Then** the order and its items remain with `OrderItems.ProductId` NULL, and the cart lines are gone.
8. **Given** a cart line for a user and product, **When** a second row for the same pair is inserted, **Then** the database rejects it with a unique-index violation.
9. **Given** `SQLSERVER_TEST_CONNECTION` is not set, **When** the tests run, **Then** they fail with a message that names the variable and says how to set it.

---

### User Story 2 - Refresh grace window for racing clients (Priority: P2)

When a client sends a refresh token that was just rotated (for example two browser tabs refresh at once and the loser reads after the winner committed), the request is rejected with 401 but the session family is not revoked, so the winner's new token keeps working.

**Why this priority**: 015 records that a late loser is treated as token theft and kills the winner's new token. This is a real user-visible logout bug, but it needs no new infrastructure.

**Independent Test**: Integration tests on EF InMemory with `RevokedAtUtc` seeded relative to now.

**Acceptance Scenarios**:

1. **Given** a token rotated less than `Jwt:RefreshReuseGraceSeconds` ago, **When** the old token is presented, **Then** the response is 401, no token is issued, no row is revoked or added, and the new token still refreshes with 200.
2. **Given** a token rotated more than the grace ago, **When** the old token is presented, **Then** the family is revoked and the response is 401 (today's behavior).
3. **Given** `Jwt:RefreshReuseGraceSeconds` is 0, **When** a just-rotated token is presented, **Then** the family is revoked (grace disabled).
4. **Given** a token revoked by logout (no `ReplacedByTokenHash`), **When** it is presented immediately, **Then** the family is revoked regardless of the grace.
5. **Given** the key is absent, **Then** the effective grace is 10 seconds.

---

### User Story 3 - Image endpoints report concurrent changes as 409 (Priority: P2)

If a stock change or another edit commits between an image endpoint's read and save, the admin gets a 409 conflict instead of a 500, and no orphan file is left behind.

**Why this priority**: 018 records this as a found-in-review gap. Small, local change.

**Independent Test**: Integration tests with the existing `SaveChangesInterceptor` racing pattern (`InventoryTests.RacingFactory`).

**Acceptance Scenarios**:

1. **Given** a product changes between read and save, **When** an admin uploads an image, **Then** the response is 409 `ProblemDetails`, the product keeps its previous image reference and the newly written file no longer exists on disk.
2. **Given** the same race, **When** an admin deletes the image, **Then** the response is 409 `ProblemDetails` and the image reference and file are kept.
3. **Given** no race, **Then** both endpoints behave exactly as before (200 and 204).

---

### User Story 4 - MVC sign-in survives container recreation (Priority: P3)

When the `mvc` container is recreated, users stay signed in because Data Protection keys live on a named volume.

**Why this priority**: 016 gap; an inconvenience (users sign in again), not a data risk.

**Independent Test**: Build passes; with compose, sign in, run `docker compose up -d --force-recreate mvc`, reload: still signed in (manual, see Known Gaps).

**Acceptance Scenarios**:

1. **Given** `DataProtection:KeysPath` is set, **When** MVC starts, **Then** keys are persisted to that directory and the application name is `CategoryProduct.MVC`.
2. **Given** the key is not set, **Then** behavior is exactly the framework default.
3. **Given** compose, **Then** `mvc` has `DataProtection__KeysPath=/data/dp-keys` backed by the named volume `mvckeys`, and the image creates that directory owned by `$APP_UID`.

---

### Edge Cases

- `SQLSERVER_TEST_CONNECTION` already contains an `Initial Catalog`/`Database`: the factory overrides it with its own unique database name.
- A test run killed before dispose leaks its database: harmless on the ephemeral CI container; locally it must be dropped by hand (quickstart).
- The seeded migration user `testuser` is an Admin after migrations (`AddUserRole` runs `UPDATE Users SET Role = 'Admin' WHERE UserName = 'testuser'`): the admin-demotion test must remove it first so exactly two admins exist.
- Parallel checkout: the loser may get 400 (empty cart after its retry) or 409; both are accepted, 201 must occur exactly once.
- Parallel movements: after three optimistic attempts the API returns 409; those do not count toward the expected total.
- Parallel demotions: a deadlock victim is retried once by `UsersController`; a second deadlock yields 500, which the test reports but does not treat as an invariant break (see Assumptions).
- Grace: a `RevokedAtUtc` in the future (clock change) is treated as inside the window.
- Negative `RefreshReuseGraceSeconds` is treated as 0.
- Upload race: `DbUpdateConcurrencyException` also occurs if the product was deleted meanwhile; the response is the same 409.
- `DataProtection:KeysPath` pointing at an unwritable directory fails on first key use (framework behavior); not handled specially.

## Requirements *(mandatory)*

### Functional Requirements

**SQL Server tests (US1)**

- **FR-001**: A new project `CategoryProduct.API.SqlServerTests` MUST exist with the same package versions as `CategoryProduct.API.Tests` (without the InMemory provider) and a reference to `CategoryProduct.API`.
- **FR-002**: The project MUST NOT be listed in `CategoryProduct.slnx`.
- **FR-003**: `SqlServerApiFactory : WebApplicationFactory<Program>` MUST read `SQLSERVER_TEST_CONNECTION`, throw an `InvalidOperationException` with an actionable message if it is missing or blank, and use a unique database name per factory instance.
- **FR-004**: The factory MUST apply migrations with `Database.MigrateAsync()` before the host serves requests and MUST drop the database (`EnsureDeletedAsync`) on dispose.
- **FR-005**: The factory MUST set test-only values for `Jwt:Key`, `Jwt:Issuer`, `Jwt:Audience` and the four rate-limit permit limits, as `ApiFactory` does.
- **FR-006**: The project MUST contain tests for acceptance scenarios 1-8 using real threads and real transactions (no interceptors).
- **FR-007**: `.github/workflows/ci.yml` MUST gain a `sqlserver-tests` job with a `sqlserver` service (`mcr.microsoft.com/mssql/server:2022-latest`, `ACCEPT_EULA=Y`, SA password, port 1433, `sqlcmd -C` health check) that runs `dotnet test CategoryProduct.API.SqlServerTests` with `SQLSERVER_TEST_CONNECTION` set and `TrustServerCertificate=True`.
- **FR-008**: The existing `build-test` and `docker-build` jobs MUST be unchanged.

**Refresh grace (US2)**

- **FR-009**: `POST api/account/refresh` MUST read `Jwt:RefreshReuseGraceSeconds` (default 10; values <= 0 disable).
- **FR-010**: When the presented token is revoked, has `ReplacedByTokenHash != null` and `now - RevokedAtUtc < grace`, the endpoint MUST return 401 without revoking any token, saving anything or issuing any token.
- **FR-011**: In every other revoked-token case the current reuse behavior (revoke the active tokens of the user's family, 401) MUST remain.
- **FR-012**: The grace-path response MUST equal the existing 401 (`Unauthorized()`, empty body).

**Image concurrency (US3)**

- **FR-013**: `UploadImage` MUST catch `DbUpdateConcurrencyException` from its save, delete the newly written file and return the existing `ConcurrencyConflict()` 409 `ProblemDetails`.
- **FR-014**: `DeleteImage` MUST catch `DbUpdateConcurrencyException`, keep the stored file and return the same 409.
- **FR-015**: Other exceptions in `UploadImage` MUST still delete the new file and propagate.

**Data Protection (US4)**

- **FR-016**: `CategoryProduct.MVC/Program.cs` MUST, only when `DataProtection:KeysPath` is non-empty, call `AddDataProtection().SetApplicationName("CategoryProduct.MVC").PersistKeysToFileSystem(new DirectoryInfo(path))`.
- **FR-017**: `docker-compose.yml` MUST set `DataProtection__KeysPath: /data/dp-keys` on `mvc`, mount the named volume `mvckeys` there and declare it under `volumes`.
- **FR-018**: `CategoryProduct.MVC/Dockerfile` MUST create `/data/dp-keys` and `chown` it to `$APP_UID` before `USER $APP_UID`.

### Key Entities

No new entities. Touched: `RefreshToken` (read only: `RevokedAtUtc`, `ReplacedByTokenHash`, `FamilyId`), `Product` (`ImageFileName`, `ConcurrencyStamp`), `CartItem`, `Order`, `OrderItem`, `StockMovement`, `User`.

## Success Criteria *(mandatory)*

- **SC-001**: The `sqlserver-tests` CI job is green, including all eight scenarios.
- **SC-002**: `dotnet test CategoryProduct.slnx` stays green with no database and no env var, and still excludes the SQL Server project.
- **SC-003**: All existing API tests stay green; new InMemory tests cover FR-009..FR-015.
- **SC-004**: `dotnet ef migrations has-pending-model-changes --project CategoryProduct.API` reports no changes (no schema change).
- **SC-005**: MVC builds with and without `DataProtection:KeysPath`; `docker compose config` lists the `mvckeys` volume.

## Assumptions

- **No schema change**: no model or migration changes (checked against `Data/ApplicationDbContext.cs` and the Migrations folder); Constitution II is satisfied trivially.
- **SA password in CI is not a secret**: the SQL Server service runs in an ephemeral container on the runner with no external exposure, so the workflow carries an obviously test-only value (`Ci-Only_Sa-Pass-1234`). Rejected alternative: `${{ secrets.X || 'fallback' }}`, which still commits the literal and adds a secret to maintain. GitHub does not expose the workflow `env` context inside `services.<id>.env`, so the literal appears once in the service definition and once in the workflow-level `env` used by the test step, with a "keep in sync" comment.
- **Fail, not skip**, when `SQLSERVER_TEST_CONNECTION` is missing: a silent skip would hide a broken CI job. The project is outside the solution so local runs never hit this.
- **Test parallelization** is disabled across test classes in the SQL project (`CollectionBehavior(DisableTestParallelization = true)`); each test spawns its own threads. Rationale: limits load on one container and avoids cross-test deadlock noise.
- **Admin-demotion test** deletes the seeded `testuser` row first, then creates two admins. It asserts the invariants (>= 1 admin, <= 1 success) and prints statuses rather than demanding exactly 200 + 409, because a second deadlock after the controller's single retry would produce 500.
- **Refresh test** asserts "at most one 200", not "exactly one", so it holds regardless of US2 (stories stay independent).
- **Grace default 10 s, 0 disables, negative = 0**, set explicitly in `appsettings.json` under `Jwt`. Only rotation-revoked tokens (`ReplacedByTokenHash != null`) qualify; logout-revoked tokens and legacy rows without a successor do not.
- **Security trade-off**: inside the window a replayed (possibly stolen) old token is not punished with family revocation, but it never yields a token. The window is bounded and can be disabled.
- **Existing refresh tests** reuse a rotated token immediately and expect family revocation (`ReusedRefreshToken_Returns401_AndRevokesFamily`, `ReuseRevokesOnlyOwnFamily_OtherSessionSurvives`, possibly others). `ApiFactory` gets a virtual `RefreshReuseGraceSeconds` defaulting to 0 so they stay unchanged; only the new grace tests use a factory returning 10.
- **Upload conflict** keeps the old image reference (the save failed) and deletes only the new file; delete-image conflict keeps everything.
- **Data Protection**: keys are not encrypted at rest (no `ProtectKeysWith...`); the named volume is the access boundary. Unset key means framework default.
- **No new logging or options class** for the grace window (Constitution III); it is read with `IConfiguration` like the other `Jwt:` keys.
- `EnsureDeletedAsync` clears SqlClient pooled connections, so they do not block the drop.
- Existing behavior relied on: insufficient stock at checkout returns 409, an empty cart returns 400 (`OrdersController.Checkout`).

## Known Gaps

- **Not runnable locally**: this machine has no Docker and no SQL Server, so `CategoryProduct.API.SqlServerTests` cannot be executed here. **Verified on CI**: the first GitHub Actions run for commit `d1d6ce2` passed all three jobs (`build-test`, `docker-build`, `sqlserver-tests`) on 2026-11-22, with no tuning needed. Parallel-race tests depend on real timing; if one becomes intermittent on CI, investigate before retrying.
- The MVC Data Protection change has no automated test and was not run in Docker (`021-test-coverage` adds an MVC test project, but it does not cover key persistence; `compose-smoke` only checks start-up); verify with the quickstart on a Docker host.
- Data Protection keys are stored unencrypted on the volume.
- Out of scope, still open: migration `SeedTestUserForAuth` seeds a user with a known password into every database (and `AddUserRole` makes it Admin); no browser verification of MVC pages (017, 018, 019) or of `TokenRefreshHandler` after a lost race. FIXED by `023-security-cleanup`: migration `NeutralizeSeededTestUser` neutralizes the seeded user when it still has the seeded hash (the AddUserRole promotion is undone for it).
- The grace window does not hand the losing client the winner's new token; the client must obtain it from its own state or sign in again.
- Image files remain local to one API instance and crash orphans are not collected (017 gaps unchanged).
