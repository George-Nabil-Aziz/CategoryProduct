# Research: Security Cleanup

All statements below were checked against the code at commit `0b8829d` unless marked "scratch test" (run on this machine with the project's SDK in a throwaway console project, deleted afterwards).

## R1. Where `testuser` comes from and what depends on it

- `CategoryProduct.API/Migrations/20260820083951_SeedTestUserForAuth.cs` inserts `UserName = "testuser"` with a fixed `PasswordHash` (`AQAAAAIAAYagAAAAENKkunRHnzjDucj641tHfwyMEdkA6lL7wKrwYWVP4LKRA2L9/9W536fomTkCKhKHSw==`); its `Down` deletes the row by `UserName`.
- `20261103173834_AddUserRole.cs` adds `Users.Role` (default `User`) and runs `migrationBuilder.Sql("UPDATE Users SET Role = 'Admin' WHERE UserName = 'testuser'")`.
- `grep -rn testuser` over the repository (excluding `bin`/`obj`/`.git`): the two migrations, `CategoryProduct.API.SqlServerTests/AdminDemotionSqlServerTests.cs` (deletes the row before counting admins) and spec text (008, 013, 016, 020, 022). No API test, MVC test, compose file, `.env.example` or CI step uses `testuser` or its password.
- API tests seed their own users: `ApiFactory` creates Admin `tester` and User `plainuser` in `ConfigureWebHost` (InMemory); `SqlServerApiFactory` runs `MigrateAsync` and creates users per test with `CreateUserAsync`. After this feature the SQL Server database still contains the (neutralized) `testuser`, as before it contained the Admin one.
- `.github/workflows/ci.yml`: `compose-smoke` only calls `/health` and the MVC `/`; the throwaway `.env` has no user settings.

## R2. Delete or neutralize

Foreign keys to `Users` (`ApplicationDbContext.OnModelCreating`):

| Table | Behavior on user delete |
|---|---|
| `RefreshTokens.UserId` | Cascade |
| `CartItems.UserId` | Cascade |
| `StockMovements.UserId` | SetNull (the movement stays, the author is lost) |
| `Orders.UserId` | Restrict (delete fails if the user has orders) |

Deleting would fail on any database where `testuser` placed an order (aborting the migration and so the API start with `MigrateOnStartup`), and otherwise silently drops carts and the audit author. Updating the row is safe for all four.

**Decision**: update, conditional on the hash still being the seeded one.

**Condition**: `PasswordHash = '<seeded hash>'`. If an operator changed the password, the hash differs, nothing happens. If the row is absent, nothing happens.

**Unusable value**. Scratch test with `PasswordHasher<object>.VerifyHashedPassword(user, value, "x")`:

| Stored value | Result |
|---|---|
| `/w==` (base64 of 0xFF) | `Failed` |
| empty string | `Failed` |
| `!` (not base64) | throws `FormatException` (would make `POST api/account/login` a 500) |
| the seeded hash with a wrong password | `Failed` |

`AccountController.Login` treats `Failed` as 401, so `/w==` gives a clean 401. Chosen over the empty string because a marker is self-describing in the table and cannot be mistaken for "missing hash" by a future constraint; both verify as `Failed`. A unit test (`SeededTestUserMarkerTests`) pins the behavior so a framework change would be noticed.

**Demotion**: `Role = 'User'`. A neutralized row should not be a privileged identity.

**Refresh tokens**: revoke every unrevoked token of the user (`RevokedAtUtc = SYSUTCDATETIME()`, `ConcurrencyStamp = NEWID()`; `RefreshToken.Revoke` does both and `ConcurrencyStamp` is a concurrency token). The UPDATE of the tokens must run BEFORE the user UPDATE because it joins on the still-matching hash. Access tokens already issued live until expiry (`Jwt:ExpiryMinutes`, default 60 in `appsettings.json`; there is no revocation list) - accepted and listed in Known Gaps.

**Statement shape** (T-SQL, one `migrationBuilder.Sql` call; column names from the model snapshot: `RefreshTokens.UserId`, `RevokedAtUtc` (datetime2, nullable), `ConcurrencyStamp` (uniqueidentifier), `Users.PasswordHash`, `Users.Role`):

```sql
UPDATE rt SET RevokedAtUtc = SYSUTCDATETIME(), ConcurrencyStamp = NEWID()
FROM RefreshTokens rt INNER JOIN Users u ON u.Id = rt.UserId
WHERE u.UserName = 'testuser' AND u.PasswordHash = '<seeded hash>' AND rt.RevokedAtUtc IS NULL;

UPDATE Users SET PasswordHash = '/w==', Role = 'User'
WHERE UserName = 'testuser' AND PasswordHash = '<seeded hash>';
```

Idempotent: a second run finds the marker, matches nothing.

## R3. Is raw SQL in a new migration allowed (constitution Principle II)?

Principle II: "All database schema changes MUST be made by adding an EF Core Code-First migration (`dotnet ef migrations add ...`) ... Manually editing the database schema outside of migrations, or hand-editing generated migration/snapshot files, is prohibited."

- This is a data change, with no schema change, and the only way to reach databases that already ran the seed migrations is a migration (it runs wherever migrations run: `MigrateOnStartup`, `dotnet ef database update`, `dotnet ef migrations script`).
- `AddUserRole` is precedent: its generated file contains `AddColumn` plus a hand-added `migrationBuilder.Sql(...)` data step in `Up`.
- The new migration is generated with the tool; the snapshot is not edited; only the `Up` method body of the new, otherwise empty file is extended. That is the standard EF mechanism for data migrations (`migrationBuilder.Sql`) and the rule's "generated files" prohibition targets edits that make the migration disagree with the model, which this does not.

**Decision**: allowed; justified in plan Complexity Tracking. Guard: `HasPendingModelChanges()` stays false (asserted by the existing `MigrationTests.EmptyDatabase_MigratesFully_AndModelHasNoPendingChanges`, which will run with the new migration).

Alternatives rejected: startup code that fixes the row (not tied to migration history); editing the old migrations (forbidden, and useless for deployed databases).

## R4. Down

`Down` cannot restore the password without re-creating the vulnerability and must not throw. Empty body plus a comment. Consequence: `dotnet ef database update SeedTestUserForAuth` (reverting below the seed) still deletes the row by the old `Down`, which is fine.

## R5. Bootstrap admin design

Existing facts:

- `Program.cs` ends with `await DatabaseMigrator.MigrateIfEnabledAsync(app.Services, app.Configuration, app.Logger); app.Run();`. `DatabaseMigrator` returns early if `Database:MigrateOnStartup` is false, skips non-relational providers (InMemory tests) and retries `MigrateAsync` up to 10 times.
- `WebApplicationFactory<Program>` runs `Program` until the host is built and started, so code placed before `app.Run()` executes in API tests (the existing `MigrationStartupTests.Startup_FlagTrue_InMemory_ServesHealth` relies on exactly that).
- Registration rules (`RegisterRequestDto`): username required, 3-50 characters, regex `^[A-Za-z0-9._-]+$`; password required, `MinLength(8)`. `AccountController.Register` trims the username, rejects a duplicate case-insensitively (`ToLower()`), hashes with `new PasswordHasher<Models.User>()`, role `User`.
- `ApplicationDbContext` has a unique index on `Users.UserName`.
- `UsersController` (API) refuses to demote the last Admin; no code path creates an Admin other than SQL and test fixtures. There is no password-change endpoint.

**Validation reuse**: scratch test: `Validator.TryValidateObject(new R("ab", "short"), ctx, results, validateAllProperties: true)` on a positional record with the same attributes returned success with 0 results (attributes sit on constructor parameters; MVC model binding reads them from the constructor parameters, `Validator` reads properties). So the helper implements the three checks itself; a parity test (register vs helper) guards drift.

**Where to run**: after the migrator in `Program.cs` (deterministic order, reachable from tests). A `BackgroundService` would start concurrently with request handling and could run before migrations; an endpoint would be an unauthenticated privilege-creating surface.

**Failure handling**: bootstrap errors are logged and swallowed. If the table does not exist (migrations not applied) the read fails, a Debug/Error log is written, and the API starts as it does today. Rejected: failing startup (a typo in an optional setting becomes an outage; and misconfiguration still produces no admin, which is the safe state).

**Concurrency**: two instances, both see "no admin", both insert; the unique index makes the second `SaveChangesAsync` throw `DbUpdateException`, caught and reported as `SkippedUserNameTaken`. A second instance using a different name is impossible (same configuration).

**Logging**: `ILogger` message templates take only the user name and outcome. The password is read into a local, validated, hashed and never passed to the logger, a log scope or an exception. Test: a collecting logger asserts no entry (message or exception) contains the password.

**Existing user with the name**: not promoted (that would let anyone who can register a name before the operator's first start become admin when the operator configures that name later; with the "name taken" rule the operator notices the Warning).

**Test seam**: `ApiFactory` always seeds Admin `tester`; add `protected virtual bool SeedAdmin => true`. The default keeps all 212 existing tests identical.

## R6. Config, compose, CI

- `docker-compose.yml` `api.environment` lists `Jwt__Key`, `ConnectionStrings__DefaultConnection`, `Database__MigrateOnStartup`, `Hosting__UseHttpsRedirection`, `ProductImages__StoragePath`, `Inventory__LowStockThreshold`. Required variables use `${VAR:?message}`; optional ones use `${VAR:-default}` (`LOW_STOCK_THRESHOLD`). The bootstrap variables follow the optional pattern with an empty default.
- ASP.NET Core maps `Bootstrap__AdminUserName` to `Bootstrap:AdminUserName`. An empty environment variable yields an empty string, treated as unset (FR-007).
- `.env.example` documents `SA_PASSWORD`, `JWT_KEY`, ports and `LOW_STOCK_THRESHOLD`; add two commented lines (not active, so copying `.env.example` to `.env` creates no account). `.env` and `.env.*` are git-ignored (`!.env.example` excepted).
- `compose-smoke` writes its own `.env` without the new variables; compose then passes empty strings; the stack starts as before. No CI change.
- Fresh compose databases now have no Admin until the operator sets the variables (previously the seeded `testuser`). That is the intended behavior and the quickstart says so.

## R7. Which API endpoints are rate limited, and which MVC pages call them

`grep EnableRateLimiting` in `CategoryProduct.API`: `AccountController.Register` ("register"), `Login` ("login"), `Refresh` ("refresh"), `Logout` ("logout"); policies in `Program.cs` (`AddRateLimiter`, `RejectionStatusCode = 429`, fixed window 1 minute per client IP; permit limits from `RateLimiting:*PermitLimit`, defaults 5/5/30/30). No other controller has the attribute and there is no global limiter.

MVC callers:

| MVC page / code | API call | 429 today |
|---|---|---|
| Account Login | `login` | handled: "Too many login attempts. Try again in a minute." |
| Account Register | `register`, then `login` | handled: "Too many attempts. Try again in a minute." (login 429 after register redirects to Login) |
| Account Logout | `logout` | swallowed by design (best effort, `catch (Exception)`); `SkipRefreshOption` skips refresh |
| every page with a bearer | `refresh` (inside `TokenRefreshHandler`) | handler treats a non-2xx refresh as failure and returns the original 401 -> "session expired"; not a message (Known Gap) |
| Users list, role change | `api/users` (not rate limited) | list: `!Success` throws "Could not load users." (500 page outside Development); role: generic "Could not change role." |
| Products create/edit/delete/image | `api/product` (not rate limited) | `AddErrors` has no branch: silent re-render |
| Products Index/Details/Edit/Delete/Image, category lookups | `GetFromJsonAsync`, `GetOrNullAsync`, `GetProductImageAsync` -> `EnsureSuccessStatusCode` | throws `HttpRequestException` (StatusCode 429) -> 500 page |
| Cart, Orders, Inventory | handled with "Too many requests. Please try again later." | n/a |
| Categories | no 429 branch on writes; reads throw like Products | out of scope except through the shared filter |

So the only rate-limited endpoints MVC reaches are already handled except through the refresh handler. The Users/Products work is defensive. 429 sources that matter in practice: a reverse proxy / gateway in front of the API, a future policy.

**Where the message is shown**:

- Write actions that re-render a form (Products): `AddErrors` -> validation summary (same place as 409).
- `ChangeRole` redirects to the list: `TempData["Error"]` (the Users view renders `TempData["Error"]` in an `alert-danger`).
- GET reads (list, details): no form to put an error on. Options: (a) per-controller try/catch (many call sites), (b) make every client read return `ApiResult` (a rewrite), (c) one exception filter. Choose (c): `HttpRequestException.StatusCode` is populated by `EnsureSuccessStatusCode` and `GetFromJsonAsync` (.NET 5+), the same mechanism by which the existing `ApiUnavailableFilter` handles `ApiUnavailableException`. The page is `Views/Shared/TooManyRequests.cshtml`, modeled on `ServiceUnavailable.cshtml` (message in an alert, Retry button `href="javascript:location.reload()"`), served with status 429.
- Text: one constant `ApiRateLimit.Message = "Too many requests. Try again in a minute."` (the Account wording; the existing Cart/Orders/Inventory wording is "Please try again later." and stays).

Filter order with `ApiUnavailableFilter`: both are `IExceptionFilter`; each only handles its own exception type and leaves others unhandled, so order does not matter. `ApiErrorHandler` only converts connection failures and 5xx; a 429 passes through to the client unchanged.

## R8. Test design

- **Bootstrap (API, InMemory)**: helper-level tests build `ServiceCollection` + `AddDbContext<ApplicationDbContext>(UseInMemoryDatabase(Guid))`, `ConfigurationBuilder().AddInMemoryCollection`, a collecting `ILogger`; they call the helper directly and assert outcome, `Users` content, hash verifies with `PasswordHasher<User>`, logs. Host-level tests: `NoAdminFactory : ApiFactory` with `SeedAdmin => false` and `Bootstrap:*` settings; `CreateClient()` triggers startup; login as the bootstrap user returns 200 and role `Admin`; `AdminExistsFactory` (default seeding plus settings) proves skip.
- **Parity**: `[Theory]` of (username, password, expectedValid) run through `POST api/account/register` (status 201 vs 400) and through the helper (`Created` vs `Rejected`).
- **Neutralize (SQL Server, CI)**: `SqlServerEnv.NewDatabaseName()`, `ApplicationDbContext` with `SqlServerEnv.Options`, `db.GetService<IMigrator>().MigrateAsync("20261122120100_AddCartAndOrders")` (the migration before the new one), raw `INSERT`/`UPDATE` via EF `ExecuteSqlRawAsync` to set up cases (hash equal vs different, tokens, an order referencing the user to prove no FK failure), then `MigrateAsync()` and assertions, then `EnsureDeletedAsync`. `AdminDemotionSqlServerTests` keeps passing: it deletes `testuser` and counts only after creating its own admins.
- **MVC**: scripted through `FakeApi` with `FakeResponse.Status(429)`; assert via `HtmlHelpers.FieldError(html, "")`, `HtmlHelpers.Alert(html, "danger")`, response status 429 and `HtmlHelpers.HasText`.
