# Research: Password Management

All statements about existing code were read in the working tree after `ba2f6a8` (024 artifacts; the 024 code is staged). Statements marked "to verify" must be confirmed during implementation.

## R1. What exists today

- `CategoryProduct.API/Controllers/AccountController.cs` (route `api/[controller]`): `register` (`[EnableRateLimiting("register")]`), `me` (`[Authorize]`), `email` (`PUT`, policy `profile`), `login` (`login`), `refresh` (`refresh`), `logout` (`logout`). Constructor: `ApplicationDbContext`, `TokenService`, `IConfiguration`. There is no change or reset password endpoint.
- Password hashing: `new PasswordHasher<Models.User>()` in `Register` (`HashPassword`) and `Login` (`VerifyHashedPassword`, only `Failed` is rejected, so `SuccessRehashNeeded` logs in). `User` has `Id`, `UserName`, `PasswordHash`, `Role`, `Email`.
- Register password rules (`Dtos/RegisterRequestDto.cs`): `[Required(AllowEmptyStrings = false)] [MinLength(8)] string Password`. No maximum, no complexity. MVC `RegisterViewModel` mirrors it (`Required`, `MinLength(8)`, `DataType.Password`, `Compare` for the confirmation).
- Refresh tokens: `Models/RefreshToken` (`TokenHash`, `CreatedAtUtc`, `ExpiresAtUtc`, `RevokedAtUtc`, `ReplacedByTokenHash`, `FamilyId`, `ConcurrencyStamp`, `Revoke(utcNow)`); `TokenService.CreateRefreshToken` generates `WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(64))` and stores `Hash(raw)`; `TokenService.Hash` is `Convert.ToHexString(SHA256.HashData(UTF8(raw)))` (uppercase hex, 64 chars; column `HasMaxLength(128)`, unique index on `TokenHash`, `ConcurrencyStamp` is a concurrency token, FK cascade to `User`). `AccountController.IssueAsync(user, familyId, replacing?)` adds a new refresh token, optionally revokes the old one, saves, and returns `LoginResponseDto(Token, ExpiresAtUtc, Role, RefreshToken, RefreshTokenExpiresAtUtc)`. `Refresh` already maps `DbUpdateConcurrencyException` to 401.
- Rate limiter (`Program.cs`): `AddRateLimiter`, `RejectionStatusCode = 429`, fixed-window policies `login`, `register`, `refresh`, `profile`, `logout`, each partitioned by `RemoteIpAddress` with a 1-minute window and `QueueLimit = 0`; limits from `RateLimiting:*PermitLimit` (code defaults 5, 5, 30, 30, 30). `ApiFactory` sets all of them (Login/Refresh/Logout via virtual properties, Register/Profile fixed at 1000) so tests do not hit 429; `RateLimitTests.LimitedFactory` (in `ApiTests.cs`) shows how a test overrides a limit.
- 024 email: `IEmailSender` (`SendAsync(EmailMessage, ct)`), `EmailMessage(To, Subject, Body)` with `Validate()` (valid address, subject max 200 without CR/LF, non-empty body), singleton `EmailQueue.TryEnqueue` (never throws, false when `Email:Enabled` is false), `EmailDispatchWorker`, scoped `NotificationService` (injects `EmailQueue`, `ApplicationDbContext`, `IOptions<InventoryOptions>`, `IOptions<EmailOptions>`, logger; every public method catches all exceptions; returns early when `!_email.Enabled` or the user has no email), `EmailAddressRules.TryNormalize` (trim, lower-case, null for empty) and `Mask`. Bodies are plain text with `\n`.
- `ApiFactory` (tests): InMemory DB, seeds Admin `tester` and User `plainuser` (password `P@ssw0rd!`, no email), sets `Email:Enabled=true`, replaces `IEmailSender` with the singleton `RecordingEmailSender` (`Messages`, `WaitForCountAsync`, `Attempts`, `ThrowOnSend`, `Clear`), exposes `EmailQueue` (`WaitForIdleAsync`). Helpers: `AuthHelper.LoggedInAsync(factory, user, password)` and `AuthHelper.Unique(prefix)` in `UsersTests.cs`; `UserEmailTests` shows registering a user with an email.
- Time: `RefreshTokenCleanupWorker` takes an optional `TimeProvider` and `RefreshTokenCleanupWorkerTests` uses `FakeTimeProvider`; the package `Microsoft.Extensions.TimeProvider.Testing` 10.11.1 is referenced in `CategoryProduct.API.Tests.csproj`. Controllers and `TokenService` use `DateTime.UtcNow`. `Program.cs` does not register `TimeProvider` (grep: no match).
- Cleanup: `RefreshTokenCleanupService.PurgeAsync(utcNow, ct)` deletes refresh tokens expired or revoked before `now - RefreshTokenCleanup:RetentionDays` (default 7) and returns the count; `RefreshTokenCleanupWorker` runs it every `IntervalMinutes` (60) in a new scope and logs `Purged {Count} old refresh tokens`.
- MVC: `AccountController` (Login, Register, Profile GET/POST with `[Authorize]`, `SessionExpiredAsync`, Logout) uses `CategoryProductApiClient` and `AuthSessionService.SignInAsync(HttpContext, userName, login)` (replaces the cookie claims, `http.User` updated). `CategoryProductApiClient` helpers `SendAsync<T>`/`SendAsync` return `ApiResult` with `Success`, `Conflict`, `Errors` (400 field map), `TooManyRequests`, `Unauthorized`, `NotFound`, `Forbidden`; `ApiRateLimit.Message` is `Too many requests. Try again in a minute.`; `LoginAsync` returns `LoginResult` or the `TooManyRequests` sentinel. Views `Login.cshtml` and `Profile.cshtml` (shows `TempData["Success"]` in Profile only). MVC tests: `MvcTestBase` (`Fake` scripted API, `SignInUserAsync`, `PostFormAsync(url, fields, tokenPageUrl:)`, `GetHtmlAsync`, `AssertLoginRedirect`), `ProfileTests` shows the page-test pattern; `ApiJson` builds JSON bodies.
- Compose: `mvc` publishes `${MVC_HOST_PORT:-5256}:8080`; the API gets `Section__Key` environment variables with `${VAR:-default}`.

## R2. Change password: return fresh tokens or require re-login

| | Return new tokens (200) | Require re-login (204) |
|---|---|---|
| Current MVC session | Continues: cookie claims replaced | Dies at the next refresh (own refresh token revoked) or must be signed out explicitly |
| API cost | `IssueAsync` already exists | none |
| Security | Equivalent: all old refresh tokens revoked | Equivalent |
| Client complexity | MVC calls `SignInAsync` with the result | MVC signs out and redirects to login |

**Decision**: return 200 `LoginResponseDto` with a new token family. Re-login is a worse experience without a security benefit. Reset-password, in contrast, returns 204 (no session exists to continue).

## R3. Token generation, storage and lookup

- Raw token: 32 bytes from `RandomNumberGenerator`, Base64Url (43 chars, URL safe, no escaping problems). Refresh tokens use 64 bytes; a 30-minute single-use token does not need more.
- Storage: `TokenService.Hash(raw)` (SHA-256 hex). A fast unsalted hash is appropriate for high-entropy random secrets (the same reasoning as refresh tokens); a slow password hash would only cost CPU. A database leak then reveals no usable link.
- Lookup is an indexed equality query on the hash (no timing oracle on the raw value: only the hash is compared).
- One reset email carries one token; requesting again supersedes older ones (documented, tested).

## R4. No user enumeration

- Identical response (202, empty body) for found, not found, no email, email disabled, throttled interval, exceptions.
- Input validation (400 for empty or over-long `userNameOrEmail`) depends only on the request text.
- Mail goes only to the stored address of the found account; the request never supplies a recipient.
- Residual timing: found accounts do an insert and an enqueue. Not equalized (adding fake work is theatre); per-IP rate limit bounds probing. Known Gap.
- MVC always redirects to the same confirmation page.

## R5. Deterministic time in tests

Expiry (30 min), the 60 s interval and cleanup retention need controlled time. Options: (a) sleep with a tiny config value (flaky, banned by spec), (b) edit `ExpiresAtUtc` in the in-memory database (works but does not test the comparison against the clock), (c) inject `TimeProvider` (built into .NET 8+, already used by the cleanup worker). **Decision**: (c) in `AccountController`, `ApiFactory` registers `FakeTimeProvider(DateTimeOffset.UtcNow)` replacing the `TryAddSingleton(TimeProvider.System)` from `Program.cs` (remove and re-add in `ConfigureServices`, like `IEmailSender`). Other code keeps real time; because the fake starts at real now and tests only advance it, JWT lifetimes (real clock) are not disturbed within a test run, except that tests advancing 31 minutes must not then rely on an access token validated by JWT bearer (JWT validation uses the system clock, not `TimeProvider`, so it is unaffected). **To verify**: that the `WebApplicationFactory` host does not already register a different `TimeProvider` (the `Host` builder registers `TimeProvider.System` via `TryAdd` in .NET 8+; `RemoveAll<TimeProvider>()` then `AddSingleton` is safe either way).

## R6. Refresh token revocation scope

"Revoke ALL the user's refresh tokens" means every token with `UserId == id && RevokedAtUtc == null` (all families), not only one family as `Refresh` reuse detection does. Implementation: load the list, call `Revoke(now)` on each (rotates the concurrency stamp, as elsewhere). Already-expired tokens may be revoked too; harmless. For change-password the new token is created after the revocation list is loaded, so it is not revoked.

## R7. Reset link host and token in the URL

- Host from `App:PublicMvcBaseUrl` only (Host-header poisoning would let an attacker make the victim's mail point at an attacker site). Validated absolute http/https; fallback with Warning.
- Token in the query string (a path segment would end up in the same logs). The reset page sends `Referrer-Policy: no-referrer` and `Cache-Control: no-store`; the form POSTs the token in the body (hidden field), never back in a URL.
- A GET of the page never consumes the token (protection against mail scanners).

## R8. Rate-limit policy design

`password` is shared by change-password and reset-password: both verify a secret (current password / reset token) and deserve the same low limit (5/min per IP). `forgot-password` has its own counter because it is called by anonymous users who may legitimately retry and because its abuse is mail volume, additionally capped per user by `MinIntervalSeconds`. `profile` (30/min) is not reused: it is too generous for secret verification. Per-IP partitioning is the existing convention (behind a proxy all users may share an IP: existing limitation, `Program.cs` has no `ForwardedHeaders` setup (verified by grep) and that is not part of this feature).

## R9. Alternatives rejected

- Embedding the token in a JWT (no table): cannot be single-use or revoked without state.
- Storing the raw token "encrypted": needs a key (new secret, Principle IV) for no benefit.
- Returning 404/400 for unknown users: enumeration.
- A `PasswordStamp` claim to kill other sessions' access tokens immediately: touches every authenticated request and the JWT bearer setup; deferred (Known Gap).
- A new `/api/password-resets` resource: more REST-pure but inconsistent with the existing `login`/`refresh`/`logout` command routes on `api/account`.
