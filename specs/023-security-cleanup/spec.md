# Feature Specification: Security Cleanup (seeded testuser, bootstrap admin, MVC 429 messages)

**Feature Branch**: `023-security-cleanup`

**Created**: 2026-11-22

**Status**: Draft

**Input**: User description: "Close two open Known Gaps: (A) the migrations `SeedTestUserForAuth` and `AddUserRole` leave a known-password `testuser` with the Admin role in every migrated database; make sure no deployed database keeps a usable known-password admin, without editing existing migrations, and add a safe way to create the first Admin. (B) The MVC Users and Products pages do not translate an API 429 into a message (Known Gap of `022-mvc-bugfixes`); show a friendly 'Too many requests. Try again in a minute.' message."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - No usable known-password admin in any database (Priority: P1)

After the new migration runs (by `Database:MigrateOnStartup`, `dotnet ef database update` or a generated script), nobody can sign in as `testuser` with the password behind the hash committed in the seed migration, and `testuser` is no longer an Admin, on every database where that account still has the original seeded password hash. A database where an operator already changed that password (the hash is different) is left exactly as it was.

**Why this priority**: the hash of an Admin account is in source control (and the account name and its Admin promotion are in two migrations and several specs); every deployed database that ran the migrations has a publicly guessable admin login. Constitution Principle IV (NON-NEGOTIABLE) treats this class of leak as a hard gate.

**Independent Test**: new SQL Server test `SeededTestUserNeutralizationSqlServerTests` (runs in the CI job `sqlserver-tests`): migrate to the migration before the new one, check `testuser` is an Admin with the seeded hash and a live refresh token, migrate to latest, check the hash no longer verifies, the role is `User`, and the refresh tokens are revoked. It cannot run on InMemory (the change is SQL inside a migration); see Assumptions.

**Acceptance Scenarios**:

1. **Given** a database at the previous migration where `testuser` has the original seeded hash and role `Admin`, **When** the new migration `NeutralizeSeededTestUser` is applied, **Then** `Users.PasswordHash` of `testuser` is the unusable marker `/w==`, `Role` is `User`, every unrevoked `RefreshTokens` row of that user has `RevokedAtUtc` set, and no row is deleted.
2. **Given** the neutralized row, **When** `POST api/account/login` is called for `testuser` with any password, **Then** the response is 401 (not 500).
3. **Given** a database where `testuser` has a different `PasswordHash` (an operator changed it), whatever its role, **When** the migration is applied, **Then** the `Users` row and its refresh tokens are unchanged.
4. **Given** a database without a `testuser` row, **When** the migration is applied, **Then** it completes and changes nothing.
5. **Given** a `testuser` with orders, cart items, stock movements or refresh tokens, **When** the migration is applied, **Then** it succeeds (no foreign-key error), because the row is updated, not deleted.
6. **Given** an empty database, **When** all migrations are applied in order, **Then** they complete, `testuser` exists as a non-admin account that cannot sign in, and `db.Database.HasPendingModelChanges()` is false.
7. **Given** the migration's `Down`, **When** it is reverted, **Then** it does nothing (the old password is never restored).

---

### User Story 2 - A safe way to get the first Admin (Priority: P1)

An operator sets two configuration values (`Bootstrap:AdminUserName`, `Bootstrap:AdminPassword`; in compose through environment variables) before a start. When the API starts and no user has the Admin role, it creates that user as Admin with the normal password hashing, applying the same username and password rules as registration, and logs the creation without the password. In every other case it does nothing.

**Why this priority**: story 1 removes the only Admin of a fresh or existing database. Without a replacement, an operator must edit the `Users` table by hand (the 008 quickstart's only documented way), and a fresh compose stack would have no way to reach the Users page.

**Independent Test**: new `BootstrapAdminTests` in `CategoryProduct.API.Tests` (InMemory, no network): helper-level cases plus a host-level case that logs in with the bootstrapped account.

**Acceptance Scenarios**:

1. **Given** both settings are set and valid, and no Admin exists, **When** the API starts, **Then** exactly one user with that name and role `Admin` exists, `POST api/account/login` with those credentials returns 200 with role `Admin`, the stored hash is a `PasswordHasher<User>` hash (not the plaintext), and an Information log line names the user but not the password.
2. **Given** both settings are set and at least one Admin exists, **Then** no user is created or changed (outcome "skipped, admin exists").
3. **Given** neither or only one setting is set (or a value is empty/whitespace), **Then** nothing is created; with exactly one set a Warning says both are required.
4. **Given** a password shorter than 8 characters, or a username outside `^[A-Za-z0-9._-]+$` / 3-50 characters, **Then** no user is created, an Error log says which rule failed (never the password), and the API still starts.
5. **Given** a user with the configured name (any letter case) already exists as a non-admin and no Admin exists, **Then** that user is NOT promoted and its password is not changed; a Warning says the name is taken.
6. **Given** `Database:MigrateOnStartup` is true on SQL Server, **Then** the bootstrap runs after the migrations finished (an old database loses `testuser` as Admin first and gets its bootstrap Admin in the same start).
7. **Given** the InMemory provider (all API tests), **Then** the bootstrap behaves the same; `ApiFactory` seeds an Admin (`tester`), so it is skipped unless a test uses a factory without an Admin.
8. **Given** two API instances start at the same time, **Then** at most one user is created (unique index on `UserName`); the loser logs and continues.
9. **Given** the compose file and `.env.example`, **Then** both settings are optional passthroughs with empty defaults / placeholders, and no usable value is committed.

---

### User Story 3 - MVC shows a friendly message on API 429 (Priority: P2)

When the API (or a gateway in front of it) answers 429, the Users and Products pages show "Too many requests. Try again in a minute." instead of a silent re-render, a wrong failure text, or an error page.

**Why this priority**: recorded Known Gap of 022. Scope note verified in code: today the API only rate-limits `api/account/register`, `login`, `refresh` and `logout` (`[EnableRateLimiting]` on exactly those four actions in `AccountController`); `api/users` and `api/product` are not rate limited. The handling is therefore defensive (a reverse proxy or gateway, a future policy) and stays P2.

**Independent Test**: new 429 tests in `UsersTests`, `ProductsTests`, `ProductImageTests` and a new `TooManyRequestsTests` for the page used by GET reads.

**Acceptance Scenarios**:

1. **Given** an Admin and `GET api/users` answering 429, **When** `/Users` is requested, **Then** the response is the friendly page with status 429, with the message and a Retry link, not an unhandled exception.
2. **Given** `PUT api/users/{id}/role` answering 429, **Then** the user is redirected to the Users list and the list shows "Too many requests. Try again in a minute." in the error alert (instead of "Could not change role.").
3. **Given** `POST api/product`, `PUT api/product/{id}`, `DELETE api/product/{id}`, `POST api/product/{id}/image` or `DELETE api/product/{id}/image` answering 429, **Then** the page re-renders (200, same view as for a 409) with the message in the validation summary (today: no message).
4. **Given** a Products page read that throws today (`GET api/product` list, `GET api/product/{id}` on Details/Edit/Delete, `GET api/category` used for category lists and names, the image proxy) answering 429, **Then** the friendly 429 page is shown with status 429, not the 500 error page.
5. **Given** a failure other than 429, **Then** behavior is unchanged (404 -> NotFound, 5xx or unreachable -> the 503 page).
6. **Given** the existing texts of Account ("Too many login attempts. Try again in a minute.", "Too many attempts. Try again in a minute.") and Cart, Orders, Inventory ("Too many requests. Please try again later."), **Then** they and their tests are unchanged.

---

### User Story 4 - Known Gaps reflect the changes (Priority: P3)

Older specs that list the seeded `testuser` or the missing 429 handling as open are annotated as fixed by `023-security-cleanup`, and this spec's Known Gaps carry the final facts (test counts, CI run).

**Independent Test**: `grep -rn "023-security-cleanup" specs` lists every touched spec; no spec still presents the testuser as open without a pointer.

**Acceptance Scenarios**:

1. **Given** the lines naming the seeded user as a risk, gap or out-of-scope item in `013`, `016`, `020`, `021` and `022`, **Then** each carries "FIXED by `023-security-cleanup`" and keeps its original text.
2. **Given** the 022 Known Gap "429 on Users and Products", **Then** it is marked fixed by 023, with the note that the API never rate-limits those endpoints.
3. **Given** the `008` documents that describe promoting `testuser` (history of a past migration), **Then** they get at most one short "superseded by 023" note; the historical design text is not rewritten.

---

### Edge Cases

- An operator who changed the `testuser` password keeps that account and its Admin role: it is a deliberate takeover, and touching it could lock out a production admin.
- An old database where `testuser` was the only Admin ends with zero Admins after the migration; the next start with the bootstrap settings creates one. Without the settings the API logs a Warning at startup (FR-011) so the situation is visible.
- Existing access tokens issued to `testuser` stay valid until they expire (`Jwt:ExpiryMinutes`, default 60; `TokenService.CreateAccessToken`); the API has no revocation list. Refresh tokens are revoked so the session cannot be extended.
- Revoking a refresh token also rotates its `ConcurrencyStamp`, because `RefreshToken.Revoke` does so and the column is a concurrency token.
- `UserName` comparison in SQL Server uses the column collation (case-insensitive by default); the bootstrap uses `ToLower()` like `AccountController.Register`.
- Bootstrap with the username `testuser` is rejected as "taken" (scenario 5 of story 2): the neutralized row still exists.
- `Bootstrap:AdminPassword` stays in the process environment; the quickstart tells the operator to remove it after the first start (harmless to leave: an Admin exists then, so it is ignored).
- A 429 on `api/account/refresh` during a Users/Products call is swallowed by `TokenRefreshHandler` (refresh fails, the 401 is returned) and ends as "session expired", not as a 429 message.
- The 429 filter only sees exceptions raised inside MVC actions.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A new EF Core migration `NeutralizeSeededTestUser` MUST be created with `dotnet ef migrations add NeutralizeSeededTestUser --project CategoryProduct.API` (generating the `.cs`, the `.Designer.cs` and an unchanged `ApplicationDbContextModelSnapshot.cs`); only its `Up` is extended with `migrationBuilder.Sql`. No existing migration or snapshot content is edited (constitution Principle II).
- **FR-002**: `Up` MUST, only when the `Users` row named `testuser` has `PasswordHash` equal to the hash inserted by `SeedTestUserForAuth`: (a) set `RevokedAtUtc = SYSUTCDATETIME()` and `ConcurrencyStamp = NEWID()` on every `RefreshTokens` row of that user with `RevokedAtUtc IS NULL`, then (b) set `PasswordHash = '/w=='` and `Role = 'User'`. Statement (a) runs first because it keys on the still-matching hash. EF runs the migration in one transaction on SQL Server.
- **FR-003**: `Up` MUST NOT delete rows, MUST NOT change any other user, and MUST be a no-op when no such row exists or the hash differs. `Down` MUST be empty with a comment explaining why.
- **FR-004**: The marker MUST make `PasswordHasher<User>.VerifyHashedPassword` return `Failed` without throwing for any password. Verified with a scratch program on this machine's SDK: `/w==` returns `Failed`; the empty string returns `Failed`; the non-base64 value `!` throws `FormatException` (which would turn login into a 500 and must not be used).
- **FR-005**: The API MUST gain a static `BootstrapAdmin` helper in `CategoryProduct.API/Data/BootstrapAdmin.cs` (same style as `DatabaseMigrator`, which lives in the same folder) exposing `EnsureIfConfiguredAsync(IServiceProvider, IConfiguration, ILogger)` that returns an outcome enum (`NotConfigured`, `Created`, `SkippedAdminExists`, `SkippedUserNameTaken`, `Rejected`, `Failed`).
- **FR-006**: `Program.cs` MUST call it exactly once, immediately after `await DatabaseMigrator.MigrateIfEnabledAsync(...)` and before `app.Run()`.
- **FR-007**: It MUST read `Bootstrap:AdminUserName` and `Bootstrap:AdminPassword`; if either is null, empty or whitespace it MUST do nothing (a Warning only when exactly one is set).
- **FR-008**: It MUST apply the same rules as `RegisterRequestDto`: username trimmed, 3-50 characters, `^[A-Za-z0-9._-]+$`; password at least 8 characters (not trimmed). It cannot reuse `Validator.TryValidateObject` on the DTO: verified in a scratch program that a positional record with the same attributes returned success for `("ab", "short")`, because the attributes sit on constructor parameters. It implements the checks itself, and a parity test runs a table of samples against both `POST api/account/register` and the helper.
- **FR-009**: It MUST create the user only when no `Users` row has `Role == "Admin"` and no row has the same name ignoring case; the user gets `Role = "Admin"` and `PasswordHash` from `new PasswordHasher<User>().HashPassword(user, password)` (the same hasher `AccountController` uses). It MUST NOT promote or modify any existing user.
- **FR-010**: It MUST NOT log the password, put it in the outcome, or put it in an exception message. It logs the user name and the outcome.
- **FR-011**: When it runs with no settings and finds no Admin, it SHOULD log one Warning ("No Admin user exists. Set Bootstrap:AdminUserName and Bootstrap:AdminPassword to create one."). Reading failures here are logged at Debug and ignored (the table may not exist when migrations are not applied).
- **FR-012**: It MUST catch `DbUpdateException` on save (unique-index race) and any other exception, log, and return `SkippedUserNameTaken` / `Failed` without throwing, so the API still starts.
- **FR-013**: `.env.example` MUST gain commented placeholders `BOOTSTRAP_ADMIN_USERNAME` and `BOOTSTRAP_ADMIN_PASSWORD`; `docker-compose.yml` MUST pass them to the `api` service as `Bootstrap__AdminUserName: ${BOOTSTRAP_ADMIN_USERNAME:-}` and `Bootstrap__AdminPassword: ${BOOTSTRAP_ADMIN_PASSWORD:-}` (optional: no `:?`). No tracked file contains a usable password.
- **FR-014**: `CategoryProduct.MVC` MUST translate an API 429 with the single text `Too many requests. Try again in a minute.`: `ProductsController.AddErrors` adds it to the model state (empty key) when `result.TooManyRequests`; `UsersController.ChangeRole` puts it in `TempData["Error"]` (before the generic "Could not change role."); `UsersController.Index` and every throwing GET read end in the friendly 429 page.
- **FR-015**: The 429 page MUST come from one exception filter `TooManyRequestsFilter` registered next to `ApiUnavailableFilter` in `AddControllersWithViews`; it handles `HttpRequestException` whose `StatusCode` is 429 and returns the view `TooManyRequests` with HTTP status 429, the message and a Retry link, with no exception text. `UsersController.Index` MUST signal a 429 result by throwing `new HttpRequestException("Too many requests.", null, HttpStatusCode.TooManyRequests)` before its generic `!Success` exception.
- **FR-016**: The text MUST live in one constant (`ApiRateLimit.Message`, new file in `CategoryProduct.MVC/Services`) used by the controllers and the filter.
- **FR-017**: Tests MUST be added: API `BootstrapAdminTests` (all story 2 cases, the parity table, log capture proving the password is absent), API `SeededTestUserMarkerTests` (hasher returns `Failed` for the marker), SQL Server `SeededTestUserNeutralizationSqlServerTests` (story 1, scenarios 1-6), and MVC tests for every acceptance scenario of story 3. Existing tests MUST keep passing; `AdminDemotionSqlServerTests` keeps its logic (only its comment changes).
- **FR-018**: No change to the API contract (routes, DTOs, status codes), no new NuGet package, no new project, no change to `CategoryProduct.slnx`.
- **FR-019**: Known Gaps MUST be updated per story 4.

### Key Entities

- **Users row `testuser`**: existing seeded row; neutralized, never deleted.
- **BootstrapAdmin / BootstrapAdminOutcome**: new static helper and enum. See `contracts/bootstrap-admin.md`.
- **NeutralizeSeededTestUser**: new migration, no model change. See `contracts/neutralize-testuser-migration.md`.
- **ApiRateLimit, TooManyRequestsFilter, `TooManyRequests` view**: new MVC pieces. See `contracts/too-many-requests-pages.md`.
- **Bootstrap configuration section**: `Bootstrap:AdminUserName`, `Bootstrap:AdminPassword`. See `contracts/bootstrap-config.md`.

## Success Criteria *(mandatory)*

- **SC-001**: `dotnet build CategoryProduct.slnx` has 0 errors and no new warnings, and `dotnet test CategoryProduct.slnx` passes three consecutive times locally with more tests than the 022 baseline of 514 (302 MVC, 212 API). `CategoryProduct.API.SqlServerTests` is not part of `CategoryProduct.slnx`; it is built separately (`dotnet build CategoryProduct.API.SqlServerTests`) and run only in CI.
- **SC-002**: On CI, `sqlserver-tests` passes with the new neutralization test (scenarios 1-6 of story 1 against a real SQL Server).
- **SC-003**: `git diff` shows the two original migrations and their designers untouched, and the snapshot unchanged (no model change).
- **SC-004**: `BootstrapAdminTests` cover created, skipped (admin exists), not configured, one-of-two, weak password, bad username, name taken, and prove the password is absent from logs.
- **SC-005**: Every acceptance scenario of story 3 has an MVC test and no existing MVC test changes its expectation.
- **SC-006**: No secret or usable password is added to a tracked file; tests use throwaway literals like the existing `P@ssw0rd!`.

## Out of Scope

- Deleting `testuser` (rejected, see Assumptions).
- A password-change endpoint or UI, password reset, lockout, or forcing a password change on first login (none exists).
- Revoking already issued access tokens (no revocation list exists).
- Adding rate limits to `api/users` or `api/product`.
- Changing the text of the Cart, Orders, Inventory or Account 429 messages, or adding a 429 branch to `CategoriesController`.
- Turning a throttled `api/account/refresh` into a 429 message.
- Logging in as the bootstrap admin inside the `compose-smoke` CI job.

## Assumptions

- **Neutralize, do not delete** `testuser`: `Orders.UserId` is `Restrict` (`ApplicationDbContext`), so deleting a user with orders would fail the migration; `StockMovements.UserId` is `SetNull` (the audit trail would lose its author) and `CartItems` / `RefreshTokens` cascade (data loss). Updating the row has no FK effect and keeps history.
- **Neutralize only when the hash still equals the seeded hash**: a real operator who set a new password owns the account; the comparison is on the full hash string inserted by `SeedTestUserForAuth`.
- **Marker `/w==`**: valid base64 (the single byte 0xFF, an unknown `PasswordHasher` format marker) that verifies as `Failed` and is never a real hash, so login answers 401 and the row is self-describing.
- **Demote to `User`** in the same step: a neutralized row must not stay a privileged identity.
- **A new migration with SQL in `Up` is allowed**: Principle II requires every schema change to go through `dotnet ef migrations add` and prohibits hand-editing generated migration/snapshot files; it does not prohibit SQL in a new migration. Precedent: `AddUserRole` (generated by the tool; its `Up` also calls `migrationBuilder.Sql("UPDATE Users SET Role = 'Admin' WHERE UserName = 'testuser'")`). This feature changes no model, so the generated `Up`/`Down` are empty and the SQL is the only addition inside the new file. Recorded in plan Complexity Tracking.
- **`Down` is a no-op**: restoring the known password would re-create the vulnerability; reverting migration history alone is harmless.
- **No InMemory test for the migration**: InMemory does not run migrations. The proof is a SQL Server test that CI runs (this machine has no SQL Server, see the 020/021 Known Gaps); locally the project is only built. The marker behavior is additionally proven on InMemory-free unit level in `SeededTestUserMarkerTests`.
- **Bootstrap runs in `Program.cs` after the migrator, not in a hosted service**: ordering with `MigrateOnStartup` is deterministic (`DatabaseMigrator` is awaited before `app.Run()`), and tests reach it through `WebApplicationFactory`, which executes `Program` up to that call.
- **Bootstrap never promotes an existing account and never resets a password**; "name taken" is a Warning.
- **Invalid or failing bootstrap does not stop the API**: an Error log is enough; no admin is created (fail safe), and a typo in an optional value does not become an outage.
- **Idempotent by construction**: it acts only when no Admin exists. If all Admins are later removed, the next start re-creates the bootstrap Admin; this is the documented recovery path.
- **429 text**: "Too many requests. Try again in a minute." as requested (the Account pattern; the API policies use a one-minute window). Cart/Orders/Inventory use "Too many requests. Please try again later." and stay unchanged to keep the change small.
- **One filter for GET reads**: the throwing reads (`GetFromJsonAsync`, `GetOrNullAsync`, `EnsureSuccessStatusCode`) raise `HttpRequestException` with `StatusCode = 429` (set by .NET; asserted by the new tests), so one `IExceptionFilter` next to `ApiUnavailableFilter` covers Products and incidentally Categories reads. Tests stay on Users and Products.
- **The 429 page returns status 429** so monitoring and tests tell it from the 503 page.
- **Empty compose defaults**: an empty `Bootstrap__AdminPassword` environment variable reads as an empty string, which FR-007 treats as not set.
- **CI and compose unchanged otherwise**: grep shows `testuser` only in the two migrations, `AdminDemotionSqlServerTests` and spec text, so no test, CI step or compose step logs in as it; `compose-smoke` only checks `/health` and the MVC home page. The API tests seed their own users (`ApiFactory`: Admin `tester`, User `plainuser`; `SqlServerApiFactory.CreateUserAsync`).
- **`AdminDemotionSqlServerTests` keeps working**: it deletes any `testuser` row before counting admins; a neutralized row (role `User`) is deleted just the same.

## Known Gaps

- Not yet implemented; this section is completed in the final task (test counts, CI run, list of older Known Gaps marked fixed).
- **Access tokens already issued** to `testuser` stay valid until expiry (default 60 minutes); there is no revocation list.
- **Databases where an operator changed the `testuser` password** keep that account and its role, by design.
- **No password-change or reset feature**: the bootstrap password is the only credential until one exists; choose a strong value.
- **A throttled `api/account/refresh`** ends as "session expired", not as a 429 message.
- **Message text differs** between Cart/Orders/Inventory ("Please try again later.") and the new pages; `CategoriesController` has no 429 branch (the new filter covers its throwing reads only).
- **The API does not rate-limit `api/users` or `api/product`**; the 429 handling is defensive and only testable with a faked API.
- **`compose-smoke` does not exercise the bootstrap admin**; the passthrough is verified by the manual quickstart step.
