---

description: "Task list for 025-password-management"
---

# Tasks: Password Management

**Input**: Design documents from `/specs/025-password-management/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Requested. Each story writes its failing tests first, then the change. No fixed sleeps: advance `ApiFactory.Clock` (`FakeTimeProvider`), await `RecordingEmailSender.WaitForCountAsync` for "mail sent" and `EmailQueue.WaitForIdleAsync` before asserting "no mail".

**Organization**: grouped by user story. Foundation (entity, migration, DTOs, config, clock) first; US1 (change) is independent of the email flow; US2 and US3 share `AccountController.cs` and `NotificationService.cs`, so they are sequential; US4 (MVC) needs the API contract only (it is tested against `FakeApi`); US5 last.

## File ownership (for parallel work)

| Story | Production files | Test files |
|---|---|---|
| Foundation | `API/Models/PasswordResetToken.cs`, `API/Data/ApplicationDbContext.cs`, `API/Migrations/*AddPasswordResetTokens*`, `API/Dtos/PasswordDtos.cs`, `API/Program.cs`, `API/appsettings.json`, `docker-compose.yml`, `.env.example` | `APIT/ApiFactory.cs` |
| US1 change | `API/Controllers/AccountController.cs`, `API/Services/NotificationService.cs` (changed mail) | `APIT/ChangePasswordTests.cs` |
| US2 forgot | `API/Controllers/AccountController.cs`, `API/Services/NotificationService.cs` (reset mail) | `APIT/ForgotPasswordTests.cs` |
| US3 reset | `API/Controllers/AccountController.cs` | `APIT/ResetPasswordTests.cs`, `APIT/PasswordRateLimitTests.cs` |
| US4 MVC | `MVC/Models/PasswordViewModels.cs`, `MVC/Services/CategoryProductApiClient.cs`, `MVC/Controllers/AccountController.cs`, `MVC/Views/Account/*` | `MVCT/{ForgotPasswordTests,ResetPasswordPageTests,ChangePasswordPageTests}.cs`, additions to `AccountTests.cs`, `ProfileTests.cs`, `Support/ApiJson.cs` |
| US5 housekeeping | `API/Services/RefreshTokenCleanupService.cs`, `RefreshTokenCleanupWorker.cs` | `APIT/RefreshTokenCleanupTests.cs`, `SQLT/PasswordResetTokenIndexSqlServerTests.cs` |

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency)
- **[Story]**: US1..US5 from spec.md
- Paths relative to the repository root. `API` = `CategoryProduct.API`, `APIT` = `CategoryProduct.API.Tests`, `SQLT` = `CategoryProduct.API.SqlServerTests`, `MVC` = `CategoryProduct.MVC`, `MVCT` = `CategoryProduct.MVC.Tests`.

---

## Phase 1: Setup

- [X] T001 Record the baseline: `dotnet build CategoryProduct.slnx` and `dotnet test CategoryProduct.slnx` (expected 703 tests: 337 MVC, 366 API); `git status`; note the branch and that 024 is committed or staged.
- [X] T002 [P] Re-verify the spec's code claims still hold: `AccountController` endpoints and `IssueAsync`; `PasswordHasher` usage; `RegisterRequestDto.Password` rules; `TokenService.Hash`; policies in `Program.cs` (no `TimeProvider` registration); `NotificationService` pattern; `RefreshTokenCleanupService` signature and its existing tests' expectations on the return value; MVC `ApiResult`, `AuthSessionService`, `FakeApi` handling of empty 202/204.

---

## Phase 2: Foundation (entity, DTOs, config, test clock)

**Purpose**: everything the stories share. Blocks US1-US3.

- [X] T003 [P] Create `API/Models/PasswordResetToken.cs` per `data-model.md` (including `MarkUsed`). (FR-001)
- [X] T004 [P] Create `API/Dtos/PasswordDtos.cs` with the three request DTOs per `contracts/password-endpoints.md`. (FR-011)
- [X] T005 `API/Data/ApplicationDbContext.cs`: `DbSet<PasswordResetToken> PasswordResetTokens` and the `OnModelCreating` block (unique `TokenHash`, `UserId` index, `HasMaxLength(128)`, concurrency token, cascade FK). (FR-001)
- [X] T006 `API/Program.cs`: add policies `forgot-password` and `password` (limits from `RateLimiting:ForgotPasswordPermitLimit` / `PasswordPermitLimit`, default 5, same fixed-window pattern as `login`); `builder.Services.TryAddSingleton(TimeProvider.System)`. (FR-008)
- [X] T007 [P] `API/appsettings.json`: `App:PublicMvcBaseUrl`, `PasswordReset:TokenMinutes`, `PasswordReset:MinIntervalSeconds` per `contracts/config.md`. (FR-013)
- [X] T008 [P] `docker-compose.yml` (api environment) and `.env.example` (commented placeholders) per `contracts/config.md`. `docker compose config` may be unavailable: review the YAML by eye. (FR-013)
- [X] T009 `APIT/ApiFactory.cs`: register `FakeTimeProvider(DateTimeOffset.UtcNow)` (`RemoveAll<TimeProvider>()` then `AddSingleton`), expose `Clock`; virtual `ForgotPasswordPermitLimit` and `PasswordPermitLimit` (default 1000) applied with `UseSetting`; `App:PublicMvcBaseUrl` = `https://mvc.test`. Run the whole `APIT` project: existing tests unchanged and green. (FR-014)
- [X] T010 Add `NotificationService.PasswordResetRequestedAsync` and `PasswordChangedAsync` in `API/Services/NotificationService.cs` per `contracts/reset-email.md` (same pattern: disabled/no-email no-op, `TryEnqueue`, catch-all Warning). Write the message-format tests first in `APIT/PasswordEmailFormatTests.cs` (subject, body lines, no CR/LF in subject, validates via `EmailMessage.Validate()`, link on its own line, no token logged). (FR-006)
- [X] T011 `API/Controllers/AccountController.cs`: add constructor parameters `NotificationService` and `TimeProvider` and the private helper `RevokeAllRefreshTokensAsync(int userId, DateTime now)`; no behavior change yet. Run `APIT`: green. (FR-009)
- [X] T012 Generate the migration: `dotnet ef migrations add AddPasswordResetTokens --project CategoryProduct.API`; review that it only creates `PasswordResetTokens` with the two indexes and the FK; confirm the snapshot changed and `HasPendingModelChanges()` is false (a throwaway local check or the SQLT test). Do not hand-edit. (FR-001, SC-005)

**Checkpoint (foundation)**: solution builds, existing tests green.

---

## Phase 3: User Story 1 - Change password (P1)

**Independent Test**: `dotnet test CategoryProduct.API.Tests --filter "FullyQualifiedName~ChangePasswordTests"`

- [X] T013 [US1] Write `APIT/ChangePasswordTests.cs` FIRST (register a user with an email via `ApiFactory`, log in, call the endpoint): ok returns 200 tokens and login works with the new password and fails with the old (scenario 1); wrong current gives 400 `CurrentPassword`, hash unchanged, no mail, no revocation (2); weak, empty and missing new password give 400 `NewPassword` (3); three logins then change: the three old refresh tokens are revoked, `refresh` with each returns 401, exactly one unrevoked token exists, the returned refresh token works with `refresh` (4); mail `Your password was changed` queued to the user's email with no password in subject or body; none for a user without email (5); anonymous 401; new equals current gives 400 (6); the returned access token works on `GET api/account/me` (1).
- [X] T014 [US1] Implement `POST api/account/change-password` in `API/Controllers/AccountController.cs` per `contracts/password-endpoints.md`: `[Authorize]`, `[EnableRateLimiting("password")]`, 401 for missing/gone user, `VerifyHashedPassword` (`Failed` to 400), equality check, `HashPassword`, `RevokeAllRefreshTokensAsync`, `IssueAsync(user, Guid.NewGuid())` (single save), `DbUpdateConcurrencyException` to 409, then `PasswordChangedAsync`. (FR-002)
- [X] T015 [US1] Run `--filter ChangePasswordTests` then the whole `APIT` project; fix until green.

**Checkpoint (US1)**.

---

## Phase 4: User Story 2 - Forgot password (P1)

**Independent Test**: `dotnet test CategoryProduct.API.Tests --filter "FullyQualifiedName~ForgotPasswordTests"`

- [X] T016 [US2] Write `APIT/ForgotPasswordTests.cs` FIRST: existing user with email by user name and by email in different case gives 202, one row (hash only: `TokenHash == TokenService.Hash(tokenFromMail)` and `!= token`), `ExpiresAtUtc == Clock now + 30 min`, one mail to that address whose body contains `https://mvc.test/Account/ResetPassword?token=` and the token (1, SC-004); unknown name and unknown email give the identical response (status, content type, body) and no row and no mail (2, SC-003); user without email gives 202, no row, no mail (3); empty, missing, whitespace and 255-char input give 400 (4); second request after `Clock.Advance(61s)` invalidates the first token (`UsedAtUtc` set) and mails again; second request within 60 s gives 202 with no new row and no new mail (5); `Email:Enabled=false` factory subclass gives 202 and no row (6); `ThrowOnSend = true` still 202 (7); invalid `App:PublicMvcBaseUrl` falls back to `http://localhost:5256` (factory subclass, 9). Await `EmailQueue.WaitForIdleAsync` before every "no mail" assertion.
- [X] T017 [US2] Implement `POST api/account/forgot-password` in `API/Controllers/AccountController.cs` per the contract (policy `forgot-password`, lookup, interval check, invalidate older unused tokens, `RandomNumberGenerator.GetBytes(32)` Base64Url, hash via `TokenService.Hash`, expiry from `PasswordReset:TokenMinutes`, link from `App:PublicMvcBaseUrl` with validation and Warning fallback, one save, then `PasswordResetRequestedAsync`; single 202 exit; catch-all after validation). (FR-003, FR-004, FR-007)
- [X] T018 [US2] Run `--filter ForgotPasswordTests` then the whole `APIT` project; fix until green.

**Checkpoint (US2)**.

---

## Phase 5: User Story 3 - Reset password (P1)

**Independent Test**: `dotnet test CategoryProduct.API.Tests --filter "FullyQualifiedName~ResetPasswordTests|FullyQualifiedName~PasswordRateLimitTests"`

- [X] T019 [US3] Write `APIT/ResetPasswordTests.cs` FIRST (helper: register user with email, call forgot-password, read the token from the mail with a small parser): success gives 204, login with new password 200 and with old 401, `UsedAtUtc` set (1); second use gives 400 `Token` with the fixed message and the password stays as set by the first call (2); `Clock.Advance(30 min)` gives 400, `Advance(29 min 59 s)` still works (3); made-up and empty token give the same 400 body (4); issue, `Advance(61 s)`, issue again: first token 400, second 204 (5); weak password gives 400 `NewPassword` and the token still works afterwards (6); refresh tokens (two logins before) are revoked and `refresh` returns 401, no tokens in the reset response (7); mail `Your password was changed` queued, no token or password inside (8); other unused tokens invalidated (9); two parallel `Task.WhenAll` resets with the same token: exactly one 204 and one 400 (10, must be deterministic: assert the pair of statuses, not which one wins).
- [X] T020 [US3] Write `APIT/PasswordRateLimitTests.cs` FIRST (factory subclass with `ForgotPasswordPermitLimit` and `PasswordPermitLimit` = 2, pattern of `RateLimitTests.LimitedFactory`): third forgot-password gives 429; third reset-password gives 429 (policy `password`); third change-password gives 429 and shares the counter with reset. (8 and 11, FR-008)
- [X] T021 [US3] Implement `POST api/account/reset-password` in `API/Controllers/AccountController.cs` per the contract (policy `password`; `NewPassword` model errors first; hash lookup with `Include(User)`; `UsedAtUtc == null && ExpiresAtUtc > now` using `_time`; `PasswordHasher.HashPassword`; `MarkUsed`; invalidate siblings; `RevokeAllRefreshTokensAsync`; one save; `DbUpdateConcurrencyException` to the same 400; then `PasswordChangedAsync`; 204). (FR-005)
- [X] T022 [US3] Run `--filter "ResetPasswordTests|PasswordRateLimitTests"` then the whole `APIT` project; fix until green.

**Checkpoint (US3): API complete.**

---

## Phase 6: User Story 4 - MVC pages (P1)

**Independent Test**: `dotnet test CategoryProduct.MVC.Tests --filter "FullyQualifiedName~ForgotPasswordTests|FullyQualifiedName~ResetPasswordPageTests|FullyQualifiedName~ChangePasswordPageTests|FullyQualifiedName~AccountTests|FullyQualifiedName~ProfileTests"`

- [X] T023 [P] [US4] Write `MVCT/ForgotPasswordTests.cs` FIRST: link on the login page (additions to `AccountTests`); GET form for anonymous; POST with API 202 redirects to the confirmation, body sent is `{ userNameOrEmail }`; POST with API 400 or other success-like results behaves per contract; API 404 still shows the uniform confirmation; 429 shows `ApiRateLimit.Message`; empty input re-renders with a validation error and makes no API call; confirmation text identical for two different inputs. (US4 1-3)
- [X] T024 [P] [US4] Write `MVCT/ResetPasswordPageTests.cs` FIRST: GET with token renders hidden `Token`, response headers `Referrer-Policy: no-referrer` and `Cache-Control: no-store`; GET without token shows the error and a link to ForgotPassword and no form; valid POST sends `{ token, newPassword }` and redirects to Login, where the success message is shown; mismatched confirmation re-renders with no API call; API 400 `Token` shows `This reset link is invalid or has expired.` and the link; 400 `NewPassword` at the field; 429 message; the token value never appears in an error message. (US4 4-6)
- [X] T025 [P] [US4] Write `MVCT/ChangePasswordPageTests.cs` FIRST and add the Profile link test to `ProfileTests.cs`: anonymous redirects to login with no API call; GET renders three password fields; valid POST sends bearer plus `{ currentPassword, newPassword }`, replaces the session cookie claims with the response tokens (assert via the next request's bearer, or `RecordingAuthenticationService`), redirects to Profile with `Password changed.`; 400 `CurrentPassword` and `NewPassword` at their fields; 429; 401 signs out and redirects to login; mismatch re-renders with no API call. (US4 7-10)
- [X] T026 [US4] `MVC/Models/PasswordViewModels.cs` and `MVC/Services/CategoryProductApiClient.cs` (`ChangePasswordAsync`, `ForgotPasswordAsync`, `ResetPasswordAsync`) per `contracts/mvc-pages.md`; `MVCT/Support/ApiJson.cs` login-response helper if missing. (FR-012)
- [X] T027 [US4] `MVC/Controllers/AccountController.cs`: `ForgotPassword` GET/POST, `ForgotPasswordConfirmation`, `ResetPassword` GET/POST (headers), `ChangePassword` GET/POST (`SignInAsync` with the new tokens, session-expired handling). (FR-012)
- [X] T028 [US4] Views: `ForgotPassword`, `ForgotPasswordConfirmation`, `ResetPassword`, `ChangePassword`; edit `Login.cshtml` (forgot link, `TempData["Success"]`) and `Profile.cshtml` (change-password link). (FR-012)
- [X] T029 [US4] Run the filter above, then the whole `MVCT` project; fix until green.

**Checkpoint (US4).**

---

## Phase 7: User Story 5 - Cleanup, SQL Server test, Known Gaps, verification (P3)

- [X] T030 [US5] Write the purge tests FIRST in `APIT/RefreshTokenCleanupTests.cs`: reset tokens expired earlier than the cutoff and used earlier than the cutoff are deleted; recent used, recent expired and still-valid tokens stay; refresh-token tests unchanged; the return value is the total.
- [X] T031 [US5] `API/Services/RefreshTokenCleanupService.cs`: purge `PasswordResetTokens` per `data-model.md` in the same save, return the total; `API/Services/RefreshTokenCleanupWorker.cs`: log `Purged {Count} old tokens`. Check `RefreshTokenCleanupWorkerTests` still pass unchanged (adjust only if a test asserts the old log text, and say so in Known Gaps). (FR-010)
- [X] T032 [P] [US5] `SQLT/PasswordResetTokenIndexSqlServerTests.cs` (CI only; follow `UserEmailIndexSqlServerTests`): the unique `TokenHash` index rejects a duplicate, two different hashes are accepted, deleting a user cascades its tokens, `HasPendingModelChanges()` is false. Only compiled locally.
- [X] T033 [US5] Security review pass over the diff: no log statement mentions a password, token, hash or link; reset mail is the only place the raw token appears; responses of `forgot-password` identical for all valid-shaped inputs (re-run the SC-003 test); link base never from the request; no new secret.
- [X] T034 [US5] Grep tracked files for secrets and addresses: nothing real in `appsettings*.json`, `.env.example` (commented placeholders only), compose; no `.env`, `logs/`, `App_Data/` or pickup files added. (SC-007, Principle IV)
- [X] T035 [US5] `git status` review against the "Source changes" list of plan.md: only the listed files; existing migrations untouched; `CategoryProduct.slnx` and the CI workflow unchanged; the new migration only adds `PasswordResetTokens`; no existing test expectation changed (only additions and helper members).
- [X] T036 [US5] Tick `checklists/requirements.md` items (FR-to-task mapping, tests-first, "to verify" items) after review.
- [X] T037 [US5] Run `dotnet build CategoryProduct.slnx` (0 errors, no new warnings in touched files).
- [X] T038 [US5] Run `dotnet build CategoryProduct.API.SqlServerTests` (0 errors).
- [X] T039 [US5] Run `dotnet test CategoryProduct.slnx` THREE times in a row; all green each time; record the test counts (more than the 703 baseline).
- [X] T040 [US5] Complete `specs/025-password-management/spec.md` Known Gaps with the final facts (status line to Implemented, migration name, test counts, whether the SQL Server test ran locally or only compiled, deviations, compose validation status).
- [ ] T041 After pushing (when the user pushes), confirm `build-test`, `docker-build`, `sqlserver-tests` (the new index test) and `compose-smoke` are green and record the run in Known Gaps. **Leave open until a CI run is observed.**

---

## Dependencies and execution order

- T001 first; T002 any time.
- Foundation: T003, T004, T007, T008 in parallel; T005 after T003; T006 any time; T009 after T006; T010 any time after T003; T011 after T010; T012 after T003 and T005 (and after any further model change).
- US1 (T013-T015) after T011; tests T013 before T014. US2 (T016-T018) after T010 and T014 (same file); US3 (T019-T022) after T018. US2/US3 tests can be written in parallel by a second agent but the controller edits are sequential.
- US4 (T023-T029): tests T023-T025 in parallel; T026 before T027; T027 before T028; independent of the API tasks (scripted `FakeApi`), so it may run in parallel with Phases 3-5 except for the shared MVC `AccountController.cs` edits, which stay with one agent.
- T030 before T031; T032 after T012. T033-T036 after all code; T037-T040 last; T041 is the only open item.

## Parallel example

Agent A: T001-T012, T013-T022 (API). Agent B: T023-T029 (MVC, against the contract only). Then one agent: T030-T040.

## Implementation strategy

MVP = Foundation + US1 + US2 + US3 + US4 (a user can change a password, request a link, and reset). US5 adds housekeeping and the final gates. Keep every existing test green at each checkpoint: they are the regression net for the `AccountController` constructor change and the `ApiFactory` additions.
