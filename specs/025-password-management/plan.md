# Implementation Plan: Password Management

**Branch**: `025-password-management` | **Date**: 2026-11-22 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/025-password-management/spec.md`

## Summary

(1) `POST api/account/change-password` verifies the current password, applies the register rules, rehashes, revokes every refresh token of the user and returns a fresh login so the MVC session continues; a "password changed" mail follows. (2) `POST api/account/forgot-password` always answers 202; for an existing user with an email it stores a SHA-256-hashed single-use token in the new `PasswordResetTokens` table (30 min, config) and queues a mail with a link to the MVC reset page through the 024 `EmailQueue`/`NotificationService`. (3) `POST api/account/reset-password` consumes the token, sets the password, revokes all refresh tokens and queues the notice. (4) MVC gets Forgot/Reset/Change password pages and links. (5) The existing `RefreshTokenCleanupService` also purges old reset tokens. Time-dependent tests use a `FakeTimeProvider` registered by `ApiFactory`.

## Technical Context

**Language/Version**: C# / .NET 10.

**Primary Dependencies**: none added. `Microsoft.AspNetCore.Identity.PasswordHasher<T>` (already used), `System.Security.Cryptography`, `Microsoft.AspNetCore.WebUtilities.WebEncoders` (already used by `TokenService`). Tests: `Microsoft.Extensions.TimeProvider.Testing` is already referenced by `CategoryProduct.API.Tests`.

**Storage**: SQL Server via EF Core: one new table `PasswordResetTokens` (migration). InMemory in tests.

**Testing**: xunit. `CategoryProduct.API.Tests` (`ApiFactory`, `RecordingEmailSender`, InMemory), `CategoryProduct.MVC.Tests` (`FakeApi`, `MvcTestBase`, `HtmlHelpers`), `CategoryProduct.API.SqlServerTests` (CI only, not in `CategoryProduct.slnx`).

**Target Platform**: local `dotnet test CategoryProduct.slnx`; GitHub Actions jobs unchanged.

**Project Type**: existing web API + MVC; no new project.

**Constraints**: constitution principles I-V; no user enumeration; tokens and passwords never stored raw or logged; sending never blocks or fails a request (024); existing tests keep their expectations; API contract only grows.

**Scale/Scope**: 1 model + 1 generated migration, 1 DTO file, 1 controller edited (API `AccountController`), `NotificationService` (2 methods), `RefreshTokenCleanupService` (+ worker log text), `Program.cs` (2 policies, `TimeProvider`), `appsettings.json`, compose/`.env.example`; MVC: client (3 methods), 3 view models, 7 action methods on `AccountController`, 4 views, 2 view edits; ~9 new test files plus additions.

## Constitution Check

Checked against `.specify/memory/constitution.md` v1.0.0.

| Principle | Check | Status |
|---|---|---|
| I. Consistent REST API Design | New actions live on the existing `api/account` controller (route `api/[controller]`) with standard verbs and codes: `POST` commands return 200 (`LoginResponseDto`), 202, 204, 400 `ValidationProblem`, 401, 429; no ad-hoc body shapes (errors are `ValidationProblem` like `Register`). No list endpoint is added. Verbs-as-resources: `change-password`, `forgot-password`, `reset-password` are command sub-routes next to the existing `login`, `refresh`, `logout`. | PASS |
| II. Schema Changes Only Through EF Core Migrations | `PasswordResetToken` ships with `dotnet ef migrations add AddPasswordResetTokens --project CategoryProduct.API` in the same change; index and FK are declared in `OnModelCreating` (as for `RefreshToken`), nothing hand-edited; `HasPendingModelChanges()` is asserted false by the existing `CategoryProduct.API.SqlServerTests/MigrationTests` (CI) and checked locally in T012. MVC keeps its own models; no shared `DbContext`. | PASS |
| III. Simplicity & YAGNI | Controllers still use `ApplicationDbContext` directly; no repository or `PasswordResetService`. New abstractions: none except two methods on `NotificationService` (a second and third consumer of the 024 service) and a `TimeProvider` constructor parameter (needed for deterministic expiry tests; .NET built-in, not a custom interface). Cleanup reuses the existing service and worker. See Complexity Tracking. | PASS (judgement calls recorded) |
| IV. Secrets & Credential Hygiene (NON-NEGOTIABLE) | Passwords hashed only with `PasswordHasher<User>`; plaintext never persisted, logged or mailed (tests assert it for the mail bodies). Reset tokens stored only as SHA-256 hash; raw value only in memory and the mail. No new secret, key or credential is introduced; new config values are non-secret defaults. `.env.example` holds commented placeholders only. | PASS |
| V. API/MVC Separation | MVC calls the three endpoints over HTTP with its own view models and client methods; it references no API type. MVC tests reference only `CategoryProduct.MVC`. The reset link target is configuration (`App:PublicMvcBaseUrl`), not a shared type. | PASS |

Workflow gates: "Every change that adds or modifies a Model class MUST include the migration": task T012 (generation), T035 (diff review). "No secret values in tracked configuration": T034 (grep). "Controller behavior verified": automated tests per story.

## Project Structure

### Documentation

```text
specs/025-password-management/
  spec.md  plan.md  research.md  data-model.md  quickstart.md  tasks.md
  contracts/{password-endpoints.md, reset-email.md, config.md, mvc-pages.md}
  checklists/requirements.md
```

### Source changes (repository root)

```text
CategoryProduct.API/
  Models/PasswordResetToken.cs                       NEW
  Data/ApplicationDbContext.cs                       + DbSet, OnModelCreating entry
  Migrations/<ts>_AddPasswordResetTokens*.cs         GENERATED (+ snapshot update)
  Dtos/PasswordDtos.cs                               NEW (3 request DTOs)
  Controllers/AccountController.cs                   + change-password, forgot-password, reset-password; ctor takes NotificationService, TimeProvider
  Services/NotificationService.cs                    + PasswordResetRequestedAsync, PasswordChangedAsync
  Services/RefreshTokenCleanupService.cs             + purge of PasswordResetTokens
  Services/RefreshTokenCleanupWorker.cs              log text only
  Program.cs                                         + policies forgot-password, password; TryAddSingleton(TimeProvider.System)
  appsettings.json                                   + App, PasswordReset sections
CategoryProduct.MVC/
  Models/PasswordViewModels.cs                       NEW (3 view models)
  Services/CategoryProductApiClient.cs               + ChangePasswordAsync, ForgotPasswordAsync, ResetPasswordAsync
  Controllers/AccountController.cs                   + ForgotPassword, ForgotPasswordConfirmation, ResetPassword, ChangePassword
  Views/Account/{ForgotPassword,ForgotPasswordConfirmation,ResetPassword,ChangePassword}.cshtml   NEW
  Views/Account/Login.cshtml                         + forgot link, TempData["Success"]
  Views/Account/Profile.cshtml                       + change-password link
docker-compose.yml, .env.example                     + App__PublicMvcBaseUrl, PasswordReset__TokenMinutes
CategoryProduct.API.Tests/
  ApiFactory.cs                                      + FakeTimeProvider (Clock), permit-limit virtuals
  ChangePasswordTests.cs  ForgotPasswordTests.cs  ResetPasswordTests.cs  PasswordRateLimitTests.cs   NEW
  RefreshTokenCleanupTests.cs                        + reset-token purge tests
CategoryProduct.MVC.Tests/
  ForgotPasswordTests.cs  ResetPasswordPageTests.cs  ChangePasswordPageTests.cs   NEW
  AccountTests.cs, ProfileTests.cs, Support/ApiJson.cs   additions
CategoryProduct.API.SqlServerTests/PasswordResetTokenIndexSqlServerTests.cs   NEW (CI only)
```

`CategoryProduct.slnx`, the CI workflow, existing migrations and existing test expectations are not touched.

## Design decisions

- **Where logic lives**: in `AccountController` actions (Principle III), next to `Login`/`Refresh` which already own `PasswordHasher`, `IssueAsync` and refresh-token revocation. A private `RevokeAllRefreshTokensAsync(userId, now)` helper loads the user's non-revoked tokens and calls `Revoke(now)` (the loop in `Refresh` does the same for one family).
- **Change-password**: load user by id, `VerifyHashedPassword`; failure to 400; validate equality; set `PasswordHash`; revoke all; `IssueAsync(user, Guid.NewGuid())` saves hash, revocations and the new token in one `SaveChangesAsync`; then `NotificationService.PasswordChangedAsync`. Concurrency: `RefreshToken` has a concurrency token, a racing refresh surfaces as `DbUpdateConcurrencyException`; the action returns 409 `ProblemDetails` (`Please try again.`) in that case. MVC treats unknown failures as the generic error text.
- **Forgot-password**: input validated by model binding (400); then one `try` block holds everything else and returns 202 from a single exit; lookup `Users` by `UserName.ToLower() == lower` or (when `TryNormalize` yields a non-null address) `Email == normalized`; checks in order: user found, `Email` non-empty, `Email:Enabled`, no unused token created within the interval; then invalidate older unused tokens, add the new one, save, compose the link, call `PasswordResetRequestedAsync`.
- **Reset-password**: `NewPassword` model validation first; then `TokenService.Hash(token)` lookup with `Include(User)`; `UsedAtUtc == null && ExpiresAtUtc > now`; set hash, `UsedAtUtc`, rotate stamp; invalidate siblings; revoke refresh tokens; one save; mail; 204. `DbUpdateConcurrencyException` to the same 400.
- **Time**: `TimeProvider _time` injected; registered in `Program.cs` with `TryAddSingleton(TimeProvider.System)`. `ApiFactory` replaces it with `FakeTimeProvider` started at real UTC now (so JWT and refresh tokens, which use `DateTime.UtcNow`, stay consistent), exposed as `Clock`; tests call `Clock.Advance(...)`. The cleanup worker keeps its optional `TimeProvider` constructor parameter (the host now supplies the registered one; its tests construct it directly with a `FakeTimeProvider` as today).
- **Link and config**: a private static helper builds the link from `App:PublicMvcBaseUrl` (validated with `Uri.TryCreate(..., UriKind.Absolute)` and scheme http/https), falling back with a Warning.
- **MVC**: `ForgotPassword` POST treats API 202 (and any non-429/non-400 outcome) as success to keep the confirmation uniform; `ResetPassword` GET sets `Referrer-Policy` and `Cache-Control` response headers; `ChangePassword` POST on success calls `AuthSessionService.SignInAsync(HttpContext, User.Identity.Name, login)` to replace the cookie claims.

## Complexity Tracking

| Judgement call | Why | Simpler alternative rejected because |
|---|---|---|
| `TimeProvider` injected into `AccountController` | Deterministic expiry and interval tests with no sleeps; the codebase already uses `TimeProvider` in `RefreshTokenCleanupWorker` and `FakeTimeProvider` in tests | `DateTime.UtcNow` would force real waiting or hand-edited rows for expiry tests |
| Two new methods on `NotificationService` rather than a new class | Same collaborators (queue, options, logger), same error policy | A `PasswordMailService` would duplicate the plumbing for two methods |
| Purge in `RefreshTokenCleanupService` | Same retention and schedule, 5 lines | A second worker adds a hosted service and config for the same job |
| `ConcurrencyStamp` on `PasswordResetToken` | Makes "exactly one of two parallel resets wins" true on SQL Server | Without it two requests could both consume one token |
| Per-user 60 s mail interval | Closes mail-bombing from many IPs with one query | Per-IP rate limit alone does not protect one victim |
