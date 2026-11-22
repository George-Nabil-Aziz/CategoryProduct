# Feature Specification: Password Management (change password, forgot/reset password by email)

**Feature Branch**: `025-password-management`

**Created**: 2026-11-22

**Status**: Implemented (CI verification pending, T041)

**Input**: User description: "Add password management. (1) A signed-in user can change their password (`POST api/account/change-password`): verify the current one, apply the register password rules, rehash, revoke all refresh tokens and keep the current MVC session working; rate limited; an MVC page under Profile. (2) Forgot/reset by email using the 024 email queue: `POST api/account/forgot-password` always answers the same way (no user enumeration) and mails a single-use, expiring, hash-only-stored token link to the MVC reset page; `POST api/account/reset-password` sets the password, marks the token used, revokes all refresh tokens and mails a 'password changed' notice. (3) The same notice after a change-password. (4) MVC: Forgot password link on login, ForgotPassword page, ResetPassword page, ChangePassword page. Rate limits on all of it. Purge expired reset tokens in the existing refresh-token cleanup."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A signed-in user changes their password (Priority: P1)

A signed-in user supplies the current and a new password. A wrong current password or a weak new one is a 400. On success every refresh token of the user is revoked (so other browsers/devices must log in again once their access token expires) and the response carries a fresh login (access and refresh token) so the calling session continues without a new login. If the user has an email, a "your password was changed" mail is queued.

**Why this priority**: the smallest useful slice and the base for the others (password rules, revoke-all, notice mail). Today there is no way to change a password at all (verified: `AccountController` has `register`, `me`, `email`, `login`, `refresh`, `logout` only).

**Independent Test**: API tests `ChangePasswordTests` and MVC tests `ChangePasswordPageTests`; no reset flow needed.

**Acceptance Scenarios**:

1. **Given** a signed-in user, a correct `currentPassword` and a valid `newPassword`, **Then** 200 with `LoginResponseDto` (new access token, role, new refresh token); the stored hash verifies the new password and no longer the old one; `login` with the new password works and with the old fails (401).
2. **Given** a wrong `currentPassword`, **Then** 400 `ValidationProblem` with key `CurrentPassword` (`Current password is incorrect.`); nothing changes; no email is queued; no token is revoked. (400, not 401: a 401 makes the MVC token handler try a refresh and treat the session as expired.)
3. **Given** a `newPassword` shorter than 8 characters or empty/missing, **Then** 400 `ValidationProblem` with key `NewPassword` (the same `Required` + `MinLength(8)` rules as `RegisterRequestDto.Password`); nothing changes.
4. **Given** the user holds three refresh tokens (three sessions), **When** the password is changed, **Then** the old three are revoked (`RevokedAtUtc` set), `POST api/account/refresh` with any of them returns 401, and exactly one new, unrevoked token exists (the one returned).
5. **Given** a user with an email and `Email:Enabled=true`, **Then** exactly one mail `Your password was changed` is queued to that address; **given** no email, none.
6. **Given** no access token, **Then** 401. **Given** `newPassword` equal to `currentPassword`, **Then** 400 on `NewPassword` (`New password must differ from the current password.`).
7. **Given** more requests than the policy allows from one IP in a minute, **Then** 429 (policy `password`).
8. **Given** the request or the mail, **Then** no log line and no mail contains either password.

---

### User Story 2 - Forgot password: request a reset link without leaking who exists (Priority: P1)

Anyone can submit a user name or an email. The API always answers `202 Accepted` with an empty body, whether or not the account exists, has an email, or the mail was queued. Only an existing user that has an email gets a mail containing a link to the MVC reset page with a one-time token. The token is random, stored only as a SHA-256 hash, expires (default 30 minutes), and issuing a new one invalidates the older unused ones.

**Why this priority**: the core of the reset flow and its security properties.

**Independent Test**: `ForgotPasswordTests` (API) with `RecordingEmailSender`; token facts are read from the in-memory database.

**Acceptance Scenarios**:

1. **Given** an existing user with an email, **When** `POST api/account/forgot-password` `{ "userNameOrEmail": "<user name>" }` (also by email address, case-insensitive), **Then** 202, one `PasswordResetTokens` row (hash only, `ExpiresAtUtc = now + 30 min`, `UsedAtUtc` null) and exactly one mail `Reset your password` to that address whose body contains `<App:PublicMvcBaseUrl>/Account/ResetPassword?token=<raw token>`; the raw token is nowhere in the database (the row holds `TokenService.Hash(raw)`).
2. **Given** an unknown user name or email, **Then** the response is the same as in scenario 1 (status 202, same content type, empty body), no row is written and no mail is queued.
3. **Given** a user without an email, **Then** the same 202; no row, no mail.
4. **Given** `userNameOrEmail` missing, empty or longer than 254 characters, **Then** 400 (this reveals nothing about accounts).
5. **Given** a user who requests twice, more than `MinIntervalSeconds` (default 60) apart, **Then** the first token stops working (`UsedAtUtc` set when invalidated) and only the second link resets; **given** a second request within the interval, **Then** the same 202 but no new row and no new mail (stops mail-bombing a victim).
6. **Given** `Email:Enabled=false`, **Then** the same 202; nothing is written or queued (a token nobody can receive is not created).
7. **Given** the sender throws or the queue is full, **Then** the response is still 202 (a notification never fails a request, as in 024).
8. **Given** more requests than the policy allows, **Then** 429 (policy `forgot-password`, default 5/min per IP).
9. **Given** `App:PublicMvcBaseUrl` is unset or not an absolute http(s) URL, **Then** the default `http://localhost:5256` is used and a Warning is logged; the link base is never taken from the request (Host header).

---

### User Story 3 - Reset the password with the emailed token (Priority: P1)

The token plus a new password resets the password. Every failure that is about the token (unknown, expired, used, superseded) answers the same 400 so a caller learns nothing. Success consumes the token, revokes every refresh token of the user, and queues a "your password was changed" mail.

**Why this priority**: completes the forgot flow; it is the only way to regain access.

**Independent Test**: `ResetPasswordTests` (API); time is controlled with `FakeTimeProvider` (research R5), no sleeps.

**Acceptance Scenarios**:

1. **Given** a valid unexpired unused token and a valid `newPassword`, **When** `POST api/account/reset-password` `{ "token": "...", "newPassword": "..." }`, **Then** 204; the password hash changes; `login` with the new password returns 200 and with the old 401; the row's `UsedAtUtc` is set.
2. **Given** the same token used a second time, **Then** 400 `ValidationProblem` key `Token` (`The reset link is invalid or has expired.`) and the password is unchanged by the second call.
3. **Given** a token older than its `ExpiresAtUtc` (fake time advanced past 30 minutes; exactly at expiry counts as expired), **Then** the same 400.
4. **Given** a made-up token or an empty token, **Then** the same 400 (identical message to scenarios 2 and 3).
5. **Given** a token issued, then a second `forgot-password` (after the interval), **Then** the first token yields the same 400 and the second works.
6. **Given** a weak `newPassword` with a valid token, **Then** 400 `NewPassword`; the token is NOT consumed and can be retried.
7. **Given** the user had refresh tokens, **When** a reset succeeds, **Then** all of them are revoked and `refresh` returns 401; no new login is issued (the user logs in again).
8. **Given** a successful reset, **Then** one mail `Your password was changed` is queued to the user's current email (if it has none any more, none); no password or token appears in it.
9. **Given** a successful reset, **Then** every other unused token of that user is invalidated too.
10. **Given** two simultaneous resets with the same token, **Then** exactly one succeeds (204) and the other gets the same 400 (concurrency stamp, like `RefreshToken`).
11. **Given** more requests than the policy allows, **Then** 429 (policy `password`).

---

### User Story 4 - MVC pages (Priority: P1)

The MVC app gets: a "Forgot password?" link on the login page; `/Account/ForgotPassword` (user name or email, then a generic confirmation); `/Account/ResetPassword?token=...` (new password and confirmation); `/Account/ChangePassword` (current, new, confirm) linked from the Profile page. API 400 and 429 are shown to the user.

**Why this priority**: without the pages the API is unusable for people.

**Independent Test**: MVC tests `ForgotPasswordTests`, `ResetPasswordPageTests`, `ChangePasswordPageTests`, additions to `AccountTests` (login link, success message) and `ProfileTests` (link); the API is the scripted `FakeApi`.

**Acceptance Scenarios**:

1. **Given** the login page, **Then** it shows a link to `/Account/ForgotPassword`.
2. **Given** `GET /Account/ForgotPassword` (anonymous allowed), **Then** a form with `UserNameOrEmail`; `POST` calls `api/account/forgot-password` and, whatever the API said except 429 and a 400, redirects to a confirmation saying "If an account with that name or email exists and has an email address, a reset link was sent." The confirmation never differs by account.
3. **Given** the API answers 429, **Then** the form is shown again with `Too many requests. Try again in a minute.`; **given** 400 for the field, its message is shown at the field.
4. **Given** `GET /Account/ResetPassword?token=abc`, **Then** a form with a hidden `Token`, `NewPassword`, `ConfirmPassword`; the response has `Referrer-Policy: no-referrer` and `Cache-Control: no-store`; **given** no `token`, an error and a link to ForgotPassword (no form).
5. **Given** a valid POST, **Then** the API receives `{ token, newPassword }`, and the user is redirected to `/Account/Login` with the success message `Your password was changed. Sign in with your new password.`; a signed-in cookie (if any) is not touched.
6. **Given** the API answers 400 with key `Token`, **Then** the form is replaced by `This reset link is invalid or has expired.` with a link to request a new one; 400 on `NewPassword` shows next to the field; 429 shows the rate-limit text. The token is never rendered into error messages or logs.
7. **Given** a signed-in user, **When** opening `/Account/ChangePassword` (anonymous is redirected to login), **Then** a form with `CurrentPassword`, `NewPassword`, `ConfirmNewPassword`; the Profile page links to it.
8. **Given** a valid POST, **Then** the API gets `{ currentPassword, newPassword }` with the user's bearer token; the new tokens from the response replace the session cookie claims (`AuthSessionService.SignInAsync`), and the user is redirected to Profile with `Password changed.`.
9. **Given** the API answers 400 `CurrentPassword`/`NewPassword`, **Then** each error is shown at its field; 429 shows the rate-limit text; 401 (expired session) signs the user out and redirects to login.
10. **Given** client-side rules, **Then** `NewPassword` is `Required` + `MinLength(8)` and the confirmation must match (the API stays the authority).

---

### User Story 5 - Housekeeping, Known Gaps and final verification (Priority: P3)

Expired and old used reset tokens are removed by the existing refresh-token cleanup; the build and test gates pass; Known Gaps carry the final facts.

**Independent Test**: `RefreshTokenCleanupTests` additions; `dotnet build CategoryProduct.slnx`, `dotnet build CategoryProduct.API.SqlServerTests`, `dotnet test CategoryProduct.slnx` three times.

**Acceptance Scenarios**:

1. **Given** reset tokens expired or used earlier than `RefreshTokenCleanup:RetentionDays` (default 7) ago, plus recent and still-valid ones, **When** `RefreshTokenCleanupService.PurgeAsync(now)` runs, **Then** only the old ones are deleted; refresh-token behavior is unchanged.
2. **Given** the finished feature, **Then** both builds have 0 errors and the full test run passes three consecutive times with more tests than the 024 baseline of 703 (MVC 337, API 366).
3. **Given** `spec.md`, **Then** Known Gaps lists what stays open, with the CI verification task left open.

---

### Edge Cases

- **No email verification (024 Known Gap)**: a reset mail goes to the account's own stored email, never to an address supplied in the forgot request, so an attacker cannot redirect a reset. Because emails are unique (filtered unique index plus controller checks), an attacker cannot claim a victim's address on a second account either. Accounts without an email cannot self-reset (see Out of Scope).
- **User enumeration**: status, content type and body of `forgot-password` are identical for all valid-shaped inputs; the MVC confirmation too. Residual timing differences (an insert and an enqueue happen only for real accounts) are mitigated by rate limits and are a Known Gap.
- **Token in the URL**: query strings land in browser history and logs. Mitigations: 32 random bytes, single use, 30 minutes, hash-only storage, MVC sends `Referrer-Policy: no-referrer` and `Cache-Control: no-store` on the reset page, and neither app logs the token. The raw token appears only in the mail (a pickup file in development).
- **The Development pickup folder holds live reset links** (024 already stores personal data there); acceptable for development only and called out in the quickstart.
- **Access tokens are stateless JWTs** (`Jwt:ExpiryMinutes`, default 60 in `appsettings.json`): after a change or reset, other sessions' already-issued access tokens stay valid until they expire; only refresh is cut off immediately. A password-stamp claim would fix it and is a Known Gap (it would touch every authenticated request).
- **Refresh tokens are revoked with `Revoke(now)`** (sets `RevokedAtUtc`, rotates `ConcurrencyStamp`); a refresh racing the revoke ends in `DbUpdateConcurrencyException`, which `Refresh` already maps to 401.
- **Roles**: any role may change or reset its password; the last-Admin rules are unaffected (no role change).
- **Login after reset**: the login endpoint is unchanged; its existing `login` rate limit protects password guessing.
- **One password rule set**: `[Required(AllowEmptyStrings = false)]` + `[MinLength(8)]`, exactly as `RegisterRequestDto.Password`; there is no maximum length today, kept identical on purpose and noted as a Known Gap.
- **InMemory vs SQL Server**: the unique `TokenHash` index is not enforced by InMemory; lookup is by hash and 256 random bits make a collision a non-case; the concurrency stamp works on both providers (as for `RefreshToken`).
- **Link scanners**: a mail scanner that only GETs `/Account/ResetPassword` does not consume the token, because consumption happens on the POST.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A new entity `PasswordResetToken` (`Id`, `UserId`, `User?`, `TokenHash`, `CreatedAtUtc`, `ExpiresAtUtc`, `UsedAtUtc?`, `ConcurrencyStamp`) and `DbSet<PasswordResetToken> PasswordResetTokens` MUST be added; the table `PasswordResetTokens` MUST ship as a migration created with `dotnet ef migrations add AddPasswordResetTokens --project CategoryProduct.API` (unique index on `TokenHash`, index on `UserId`, FK to `Users` with cascade delete, `TokenHash` `nvarchar(128)`). No hand-written migration or snapshot edit.
- **FR-002**: `POST api/account/change-password` (`[Authorize]`, `[EnableRateLimiting("password")]`) MUST accept `ChangePasswordRequestDto(CurrentPassword, NewPassword)`, take the user id from `ClaimTypes.NameIdentifier` (401 if missing or the user row is gone), verify the current password with `PasswordHasher<User>` (400 `CurrentPassword` on `Failed`; `SuccessRehashNeeded` counts as success), apply the register password rules to `NewPassword` and reject equality with the current password (400 `NewPassword`), rehash with `HashPassword`, revoke ALL of the user's non-revoked refresh tokens (`Revoke(now)`), issue a new login through the existing `IssueAsync(user, Guid.NewGuid())` (new family), and return 200 `LoginResponseDto`. The hash change, the revocations and the new token MUST be saved together.
- **FR-003**: `POST api/account/forgot-password` (anonymous, `[EnableRateLimiting("forgot-password")]`) MUST accept `ForgotPasswordRequestDto(UserNameOrEmail)` (`Required`, max 254; 400 otherwise) and return `202 Accepted` with an empty body in every other case. It finds a user by case-insensitive user name OR by the `EmailAddressRules.TryNormalize` result equal to `Users.Email`. Only when a user is found AND has an email AND `Email:Enabled` is true AND no unused token of that user was created within `PasswordReset:MinIntervalSeconds` does it create a token and enqueue the mail. Every exception after input validation is caught and logged (Warning, no address, no token) and still returns 202.
- **FR-004**: The raw token MUST be `WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32))`, stored only as `TokenService.Hash(raw)` (SHA-256, uppercase hex: the function that hashes refresh tokens). The raw value exists only in memory and in the mail. `ExpiresAtUtc = now + PasswordReset:TokenMinutes` (default 30; values <= 0 fall back to 30). Issuing a token sets `UsedAtUtc = now` on every other unused, unexpired token of the user in the same save.
- **FR-005**: `POST api/account/reset-password` (`[EnableRateLimiting("password")]`) MUST accept `ResetPasswordRequestDto(Token, NewPassword)`. Order: model validation of `NewPassword` (400 `NewPassword`, token not consumed), lookup by hash, then `UsedAtUtc == null` and `ExpiresAtUtc > now`. Any token failure returns 400 `ValidationProblem` key `Token` with one fixed message `The reset link is invalid or has expired.`. On success: set the new hash, set `UsedAtUtc = now` and rotate the token `ConcurrencyStamp`, invalidate the user's other unused tokens, revoke all refresh tokens, save once, then enqueue the "password changed" mail; return 204. `DbUpdateConcurrencyException` maps to the same 400.
- **FR-006**: `NotificationService` MUST gain `PasswordResetRequestedAsync(User user, string link, int validMinutes)` and `PasswordChangedAsync(User user)`; both follow the existing pattern (no-op when email is disabled or the user has no email, plain text with invariant formatting, `TryEnqueue`, every exception caught and logged as a Warning). Messages are in `contracts/reset-email.md`. They are called after the database save.
- **FR-007**: The link MUST be `<base>/Account/ResetPassword?token=<Uri.EscapeDataString(raw)>` where `<base>` is `App:PublicMvcBaseUrl` (absolute http or https, trailing slash trimmed; unset, empty or invalid falls back to `http://localhost:5256` with a Warning log). It MUST NOT be derived from the request.
- **FR-008**: `Program.cs` MUST register the rate-limit policies `forgot-password` (`RateLimiting:ForgotPasswordPermitLimit`, default 5/min per IP) and `password` (`RateLimiting:PasswordPermitLimit`, default 5/min per IP; shared by change-password and reset-password) with the same fixed-window pattern as `login`, and `TimeProvider.System` as a singleton (`TryAddSingleton`) so the controller can take `TimeProvider`.
- **FR-009**: `AccountController` MUST take `NotificationService` and `TimeProvider`; new code uses `_time.GetUtcNow().UtcDateTime` for token times so tests control time. Existing endpoints keep their behavior and their `DateTime.UtcNow` usage.
- **FR-010**: `RefreshTokenCleanupService.PurgeAsync` MUST also delete `PasswordResetTokens` with `ExpiresAtUtc < cutoff` or `UsedAtUtc < cutoff` (same `RetentionDays`, default 7) and return the total removed; the `RefreshTokenCleanupWorker` log text becomes "old tokens". No new worker.
- **FR-011**: DTOs (`Dtos/PasswordDtos.cs`): `ChangePasswordRequestDto([Required] CurrentPassword, [Required][MinLength(8)] NewPassword)`, `ForgotPasswordRequestDto([Required][StringLength(254)] UserNameOrEmail)`, `ResetPasswordRequestDto([Required] Token, [Required][MinLength(8)] NewPassword)`, all with `AllowEmptyStrings = false` as in `RegisterRequestDto`. No password or token is ever returned or logged.
- **FR-012**: MVC MUST add `CategoryProductApiClient.ChangePasswordAsync(current, new)` (returns the new `LoginResult` on success), `ForgotPasswordAsync(userNameOrEmail)`, `ResetPasswordAsync(token, newPassword)`; view models `ChangePasswordViewModel`, `ForgotPasswordViewModel`, `ResetPasswordViewModel`; actions `ForgotPassword` (GET/POST), `ForgotPasswordConfirmation` (GET), `ResetPassword` (GET/POST), `ChangePassword` (GET/POST) on the MVC `AccountController`; a view for each; a link in `Login.cshtml` and `Profile.cshtml`; `Login.cshtml` shows `TempData["Success"]`. Behavior per User Story 4. MVC references no API type (Principle V).
- **FR-013**: Configuration (`contracts/config.md`): `appsettings.json` gains `App:PublicMvcBaseUrl` (`http://localhost:5256`), `PasswordReset:TokenMinutes` (30), `PasswordReset:MinIntervalSeconds` (60); the `RateLimiting` keys are read with code defaults. `docker-compose.yml` passes `App__PublicMvcBaseUrl: ${APP_PUBLIC_MVC_BASE_URL:-http://localhost:${MVC_HOST_PORT:-5256}}` and `PasswordReset__TokenMinutes: ${PASSWORD_RESET_TOKEN_MINUTES:-30}`; `.env.example` documents them as comments. No secrets are involved.
- **FR-014**: `ApiFactory` MUST register a `FakeTimeProvider` (package `Microsoft.Extensions.TimeProvider.Testing` is already referenced by `CategoryProduct.API.Tests`) started at the real current UTC time, expose it as `Clock`, and add virtual permit-limit properties for the two new policies (default 1000), without changing existing tests' expectations.
- **FR-015**: Tests per the plan: API (`ChangePasswordTests`, `ForgotPasswordTests`, `ResetPasswordTests`, `PasswordRateLimitTests`, cleanup additions), MVC (`ForgotPasswordTests`, `ResetPasswordPageTests`, `ChangePasswordPageTests`, additions to `AccountTests` and `ProfileTests`) and a SQL Server test for the new unique index (CI only, `PasswordResetTokenIndexSqlServerTests`). Deterministic: fake time, `RecordingEmailSender.WaitForCountAsync` or `EmailQueue.WaitForIdleAsync`, no fixed sleeps; "no email" assertions call `WaitForIdleAsync` first.
- **FR-016**: No change to existing routes, status codes or response shapes; no new NuGet package; no new project; `CategoryProduct.slnx` unchanged.

### Key Entities

- **PasswordResetToken** (new): a hashed, single-use, expiring token for one user. See `data-model.md`.
- **Password endpoints**: `change-password`, `forgot-password`, `reset-password`. See `contracts/password-endpoints.md`.
- **Reset and notice emails**: see `contracts/reset-email.md`.
- **Configuration**: see `contracts/config.md`.
- **MVC pages**: see `contracts/mvc-pages.md`.

## Success Criteria *(mandatory)*

- **SC-001**: `dotnet build CategoryProduct.slnx` has 0 errors and no new warnings in touched files; `dotnet test CategoryProduct.slnx` passes three consecutive times locally with more tests than the 024 baseline of 703. `CategoryProduct.API.SqlServerTests` (not in the solution) builds with 0 errors.
- **SC-002**: Every acceptance scenario of stories 1-5 has an automated test; no test uses a fixed sleep.
- **SC-003**: A test proves the `forgot-password` responses for an existing user, an unknown user and a user without email are identical (status, content type, body), and that only the first sends mail.
- **SC-004**: A test proves no raw token is stored: the only database value is `TokenService.Hash(raw)` and it differs from `raw`.
- **SC-005**: A migration `AddPasswordResetTokens` exists, the snapshot matches the model (`HasPendingModelChanges()` false), and on CI the SQL Server test proves the unique `TokenHash` index rejects a duplicate.
- **SC-006**: Existing tests pass without changing their expectations (only helper additions such as `ApiFactory` members).
- **SC-007**: A search over tracked files finds no secret introduced by this feature (none are needed).

## Out of Scope

- Admin-initiated password reset, self-service recovery for accounts without email, email verification, "this wasn't me" lock links.
- Password strength rules beyond the existing `MinLength(8)`; password history; breached-password checks.
- Invalidating already-issued access tokens (JWT password stamp).
- Account lockout; CAPTCHA; per-account rate limiting beyond the 60 s mail interval.
- HTML or localized emails.
- Changing the login endpoint, the refresh rotation logic or the 024 email transport.
- A "log out other devices" UI.

## Assumptions

- **No questions were asked** (the requester asked for none); each open decision below is the safe, small option.
- **Change-password returns fresh tokens (200 `LoginResponseDto`) instead of requiring re-login**: revoke-all also revokes the caller's own refresh token, so without new tokens the MVC session would die at its next access-token refresh. Issuing a new family in the same save keeps the session at no cost (`IssueAsync` exists). Forcing re-login would be simpler for the API but worse for the user with no security gain.
- **Reset-password does not log the user in** (204): the caller proves possession of a mailbox, not of a session; one explicit login afterwards is the conventional, safer choice and keeps the endpoint free of token issuance.
- **forgot-password answers 202 Accepted with an empty body**: the work (mail) is asynchronous; the status is standard and identical for every input.
- **One error message for every token failure**, no distinction between unknown, expired, used or superseded.
- **A wrong current password is 400 on `CurrentPassword`, not 401**, so the MVC refresh handler does not misread it as an expired session.
- **Used and superseded tokens share `UsedAtUtc`**: invalidating a token sets `UsedAtUtc`. The requested columns are `UserId, TokenHash, ExpiresAtUtc, UsedAtUtc, CreatedAtUtc`; `Id` and `ConcurrencyStamp` are added for the key and the race guard, as `RefreshToken` does.
- **60 s minimum interval per user** (`PasswordReset:MinIntervalSeconds`) stops the endpoint being used to flood one person's inbox from many IPs; a legitimate retry after a minute works.
- **Token of 32 random bytes** (256 bits) is enough for a 30-minute single-use token (refresh tokens use 64).
- **Hash with the existing `TokenService.Hash`** (unsalted SHA-256) because the input is high-entropy random, the same reasoning as refresh tokens; no new hashing code.
- **No token is created when `Email:Enabled` is false**: the response is unchanged and nothing useless is stored.
- **The link base is configuration, not the request**, to avoid Host-header poisoning; compose defaults it to `http://localhost:${MVC_HOST_PORT}`; real deployments set `APP_PUBLIC_MVC_BASE_URL`.
- **Controllers keep using `ApplicationDbContext` directly** (Principle III); only mail composition goes into the existing `NotificationService`.
- **`TimeProvider` is injected only where the new time-dependent code lives**; the existing refresh logic is not refactored.
- **Cleanup reuses `RefreshTokenCleanupService`** with the same retention instead of a second worker: a few lines on the same schedule.
- **Rate-limit policies**: `password` (5/min) is shared by change-password and reset-password because both accept a secret an attacker could guess; `forgot-password` (5/min) is separate because its abuse mode is mail volume. Limits are per IP like every other policy here.
- **Tests**: `ApiFactory` already swaps `IEmailSender` for the recording sender; the real queue and worker run, so the whole path controller, notification, queue, worker, sender is covered.

## Known Gaps

- **Implemented** (T001-T040 done; T041 open). Migration: `20261122195916_AddPasswordResetTokens` (generated with `dotnet ef migrations add AddPasswordResetTokens --project CategoryProduct.API`, only creates `PasswordResetTokens` with the two indexes and the cascade FK; never hand-edited; `dotnet ef database update` was not run).
- **Test counts**: `dotnet test CategoryProduct.slnx` = 365 MVC (was 337, +28) + 406 API (was 366, +40) = 771 (was 703); four consecutive full runs all green. `CategoryProduct.API.SqlServerTests` = 24 tests (+4 for the new table), run locally on LocalDB (`(localdb)\MSSQLLocalDB`, throwaway GUID-named databases) and all passed; it is not part of `CategoryProduct.slnx` and was also built with `dotnet build CategoryProduct.API.SqlServerTests` (0 errors).
- **Compose validation**: Docker is not installed on the development machine, so `docker compose config` was not run and the nested `${APP_PUBLIC_MVC_BASE_URL:-http://localhost:${MVC_HOST_PORT:-5256}}` interpolation was reviewed by eye only; both new variables have non-empty defaults. `compose-smoke` in CI is the real check.
- **Deviations from the docs**: (1) `AccountController` also takes `ILogger<AccountController>` (for the Warning logs of the forgot-password catch-all and the base-URL fallback), in addition to `NotificationService` and `TimeProvider`. (2) `ResetPasswordRequestDto.Token` carries the fixed message `The reset link is invalid or has expired.` as its `[Required]` error message so an empty or missing token returns the same text as an unknown one. (3) Forgot-password looks the user up by user name first and only then by normalized email (so an ambiguous input cannot pick a random account); it invalidates only unused, unexpired older tokens. (4) The `PasswordEmailFormatTests` go through the real `NotificationService`/queue/recording sender of `ApiFactory` (the queue reader is internal), and `ApiFactory` also gained virtual `EmailEnabled` and `PublicMvcBaseUrl` for the "email disabled" and "invalid base URL" tests. (5) Production code for cleanup (T031) was written before its purge test (T030) in this run; the test exists and passes.
- **Worker clock**: `RefreshTokenCleanupWorker` now receives the registered `TimeProvider`; in API tests that is the fake clock, so advancing it by 60 minutes in total fires one harmless purge (nothing is older than the retention).
- **CI verification (T041) is still open**: `build-test`, `docker-build`, `sqlserver-tests` and `compose-smoke` have not been observed yet.
- **No email verification** (carried from 024): only an account's own stored address ever receives a link, but a user can store a wrong address and then cannot recover by mail.
- **Accounts without an email cannot reset by themselves**; there is no Admin reset in this feature.
- **Other sessions' access tokens survive** until their `Jwt:ExpiryMinutes` (default 60); only refresh is revoked.
- **Timing side channel** on `forgot-password` (extra insert and enqueue for real accounts) is not eliminated, only rate limited.
- **Queued mails are lost on restart** and are not retried (024 queue); a lost reset mail is fixed by requesting again after the interval.
- **No maximum password length** (same as register), so a huge password costs hashing time; rate limits bound it.
- **Reset tokens in the URL** can still leak through browser history or mail-client link previews that execute scripts; they expire and are single use.
