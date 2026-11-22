# Implementation Plan: Security Cleanup

**Branch**: `023-security-cleanup` | **Date**: 2026-11-22 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/023-security-cleanup/spec.md`

## Summary

Three independent pieces. (1) A new data-only EF Core migration `NeutralizeSeededTestUser` that, only when the seeded `testuser` still has its original hash, revokes its refresh tokens, replaces the hash with the unusable marker `/w==` and demotes it to `User`; `Down` is empty. (2) A static `BootstrapAdmin` helper called from `Program.cs` right after the startup migrator: when `Bootstrap:AdminUserName` and `Bootstrap:AdminPassword` are set and no Admin exists it creates an Admin with the standard password hasher and the registration rules, never logging the password; compose and `.env.example` get optional passthroughs. (3) MVC: one `TooManyRequestsFilter` plus `ApiRateLimit.Message`, a `TooManyRequests` view, and 429 branches in `ProductsController.AddErrors`, `UsersController.ChangeRole` and `UsersController.Index`. Tests: API InMemory for the bootstrap, SQL Server (CI only) for the migration, MVC for the 429 messages.

## Technical Context

**Language/Version**: C# / .NET 10.

**Primary Dependencies**: none added. Bootstrap uses `Microsoft.AspNetCore.Identity.PasswordHasher<T>` (already used by `AccountController`) and EF Core. The migration uses `Microsoft.EntityFrameworkCore.Migrations` (already referenced).

**Storage**: SQL Server via EF Core. No schema change; one data migration. InMemory provider in tests.

**Testing**: xunit. `CategoryProduct.API.Tests` (InMemory, `ApiFactory`), `CategoryProduct.API.SqlServerTests` (needs `SQLSERVER_TEST_CONNECTION`; CI job `sqlserver-tests`; not in `CategoryProduct.slnx`), `CategoryProduct.MVC.Tests` (`FakeApi`, `MvcTestBase`).

**Target Platform**: local `dotnet test CategoryProduct.slnx`; GitHub Actions jobs `build-test`, `docker-build`, `sqlserver-tests`, `compose-smoke` unchanged.

**Project Type**: existing web API + MVC; no new project.

**Constraints**: do not edit existing migrations or the snapshot; bootstrap must work on InMemory and after `DatabaseMigrator`; the password never appears in logs; existing tests keep passing; API contract unchanged.

**Scale/Scope**: 1 migration (+ designer), 1 new API helper, 1 `Program.cs` line, 2 MVC controllers edited, 1 MVC filter + constant + view added, 1 `Program.cs` (MVC) filter registration, `.env.example` and `docker-compose.yml` edited, 3 new API test files (+ small `ApiFactory` hook), 1 new SQL Server test file, MVC tests extended, older specs annotated.

## Constitution Check

Checked against `.specify/memory/constitution.md` v1.0.0.

| Principle | Check | Status |
|---|---|---|
| I. Consistent REST API Design | No route, verb, DTO or status code changes; login for the neutralized user returns the existing 401. The bootstrap is not an endpoint. | PASS |
| II. Schema Changes Only Through EF Core Migrations | The cleanup of already-deployed data ships as a new migration made with `dotnet ef migrations add`; no existing migration or snapshot is edited; the model does not change, so there is no schema change at all, only a data step. Raw SQL in `Up` has precedent in `AddUserRole` (`migrationBuilder.Sql`), which the rule did not prevent; the new file is generated, then only its `Up` body is extended. Judgement call recorded below. | PASS (see Complexity Tracking) |
| III. Simplicity & YAGNI | One static helper next to `DatabaseMigrator` instead of a service layer or hosted service; one migration; one MVC filter instead of handling 429 around every read call; a single message constant. The helper talks to `ApplicationDbContext` directly. | PASS |
| IV. Secrets & Credential Hygiene (NON-NEGOTIABLE) | This feature exists to enforce it: the known-password admin is neutralized; the bootstrap password comes only from configuration/environment (placeholders in `.env.example`, empty default in compose), is hashed with the standard hasher, and is never logged. No real value is committed. | PASS |
| V. API/MVC Separation | MVC only reads the HTTP status; it references no API type. MVC tests reference only `CategoryProduct.MVC`. | PASS |

Workflow gates: "Every change that adds or modifies a Model class MUST include the migration" - no model change, none needed; "no secret values in tracked configuration" - verified by review task T022 (grep). "Controller behavior changes must be verified": automated tests.

## Project Structure

### Documentation

```text
specs/023-security-cleanup/
  spec.md  plan.md  research.md  data-model.md  quickstart.md  tasks.md
  contracts/{neutralize-testuser-migration.md, bootstrap-admin.md, bootstrap-config.md, too-many-requests-pages.md}
  checklists/requirements.md
```

### Source changes

```text
CategoryProduct.API/Migrations/<ts>_NeutralizeSeededTestUser.cs            NEW  generated; Up extended with SQL, Down empty
CategoryProduct.API/Migrations/<ts>_NeutralizeSeededTestUser.Designer.cs   NEW  generated
CategoryProduct.API/Migrations/ApplicationDbContextModelSnapshot.cs        (unchanged; verify)
CategoryProduct.API/Data/BootstrapAdmin.cs                                 NEW  static helper + outcome enum
CategoryProduct.API/Program.cs                                             EDIT one line after DatabaseMigrator call
docker-compose.yml                                                         EDIT api env: Bootstrap__AdminUserName / __AdminPassword (optional)
.env.example                                                               EDIT commented placeholders

CategoryProduct.API.Tests/ApiFactory.cs                                    EDIT protected virtual bool SeedAdmin => true (default unchanged)
CategoryProduct.API.Tests/BootstrapAdminTests.cs                           NEW
CategoryProduct.API.Tests/SeededTestUserMarkerTests.cs                     NEW  marker returns Failed; hash constant sanity
CategoryProduct.API.SqlServerTests/SeededTestUserNeutralizationSqlServerTests.cs  NEW
CategoryProduct.API.SqlServerTests/AdminDemotionSqlServerTests.cs          EDIT comment only

CategoryProduct.MVC/Services/ApiRateLimit.cs                               NEW  const Message
CategoryProduct.MVC/Services/ApiErrorHandler.cs                            EDIT add TooManyRequestsFilter (next to ApiUnavailableFilter)
CategoryProduct.MVC/Program.cs                                             EDIT register the filter
CategoryProduct.MVC/Views/Shared/TooManyRequests.cshtml                    NEW
CategoryProduct.MVC/Controllers/UsersController.cs                         EDIT Index + ChangeRole 429
CategoryProduct.MVC/Controllers/ProductsController.cs                      EDIT AddErrors 429
CategoryProduct.MVC.Tests/UsersTests.cs, ProductsTests.cs, ProductImageTests.cs   EDIT 429 tests
CategoryProduct.MVC.Tests/TooManyRequestsTests.cs                          NEW  GET reads -> 429 page

specs/013, 016, 020, 021, 022 (Known Gaps / risks), 008 (one note)         EDIT annotations
```

**Structure Decision**: no new project. The three pieces share no production file (API migration/bootstrap vs MVC), so they can be built in parallel. The only shared test file is none; `ApiFactory.cs` is touched by the bootstrap tests only.

## Design

1. **Migration**. Run `dotnet ef migrations add NeutralizeSeededTestUser --project CategoryProduct.API`. The generated `Up` and `Down` are empty because the model is unchanged; keep the tool's output and add to `Up` one `migrationBuilder.Sql` call holding a single T-SQL batch (full text in `contracts/neutralize-testuser-migration.md`): repeating the seeded hash literal in the `WHERE` of two `UPDATE`s, refresh tokens first, then the user. `Down` gets only a comment. See research R1-R3.
2. **Marker**. `/w==` (research R2).
3. **Bootstrap**. `BootstrapAdmin.EnsureIfConfiguredAsync(services, configuration, logger)`: read both settings; if either blank, log Warning when exactly one set, else (FR-011) check admin presence for the warning and return `NotConfigured`; validate (explicit rules, FR-008); create a scope, `ApplicationDbContext`; `AnyAsync(Role == "Admin")` -> `SkippedAdminExists`; name check `ToLower()` -> `SkippedUserNameTaken`; build `User`, hash, `Add`, `SaveChangesAsync`; catch `DbUpdateException` and `Exception`. `Program.cs`: `await BootstrapAdmin.EnsureIfConfiguredAsync(app.Services, app.Configuration, app.Logger);` after the migrator.
4. **Test seam for "no admin"**: `ApiFactory` gets `protected virtual bool SeedAdmin => true;` and skips adding `tester` when false (the seeding runs in `ConfigureWebHost`, before the host starts, so the bootstrap at startup sees the result). Tests subclass it per scenario, setting `Bootstrap:*` through `builder.UseSetting`. Helper-level cases build their own InMemory `ServiceProvider` and an in-memory `IConfiguration`, and capture logs with a small `ILogger` collector so the password assertion is exact.
5. **Compose/env**: two lines in the `api` environment, two commented placeholders (research R6).
6. **MVC 429**: `ApiRateLimit.Message`; `TooManyRequestsFilter : IExceptionFilter` handles `HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests }` -> `ViewResult` `TooManyRequests`, status 429; registered with `o.Filters.Add<TooManyRequestsFilter>()`. `ProductsController.AddErrors` adds the message when `result.TooManyRequests`; `UsersController.Index` throws the 429 `HttpRequestException` when `result.TooManyRequests`; `ChangeRole` adds `else if (result.TooManyRequests) TempData["Error"] = ApiRateLimit.Message;` before the generic branch (details in the contract).
7. **Tests first** per story (tasks.md orders them that way).

## Complexity Tracking

| Decision | Why it needs a reviewer's eye | Simpler alternative rejected |
|---|---|---|
| Raw SQL inside a generated migration (`NeutralizeSeededTestUser`) | Principle II forbids hand-editing generated migration/snapshot files. The generated file is created by `dotnet ef migrations add`; adding a `migrationBuilder.Sql` call to its (otherwise empty) `Up` is the documented EF way to ship a data fix and matches the existing `AddUserRole` precedent. Nothing else in the file or snapshot is touched. | Fixing data at API startup in code (runs on every start, bypasses the migration history, and would not run for operators who apply migrations by script); editing `SeedTestUserForAuth`/`AddUserRole` (forbidden and would not fix databases that already ran them). |
| Static helper called from `Program.cs` | Adds startup logic to the API entry point. | Hosted service (ordering with migrations is not deterministic); controller endpoint (an unauthenticated "create first admin" endpoint is an attack surface). |
| Marker value instead of deleting the row | A row that exists but cannot be used. | Deleting: FK and audit consequences (Assumptions). |

## Phase order

Phase A (US1) and Phase B (US2) both touch `CategoryProduct.API` but different files, Phase C (US3) touches only MVC; all three can run in parallel except that the final verification and Known Gaps work (US4) comes last.
