# Feature Specification: Auth Hardening

**Feature Branch**: `015-auth-hardening`

**Created**: 2026-11-22

**Status**: Draft

**Input**: User description: "Close the Known Gaps left by 013-user-registration-management and 014-refresh-tokens in CategoryProduct.API: scope refresh-token reuse revocation, make concurrent refreshes safe, purge old refresh tokens, rate limit logout, and make last-admin protection safe under concurrent demotions."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Reuse detection only ends the compromised session (Priority: P1)

A user signed in on several devices (each login is its own session) keeps their other sessions when
a revoked refresh token from one session is replayed. Only the sessions of the replayed token's
family are revoked.

**Why this priority**: Today `AccountController.Refresh` revokes every active refresh token of the
user when any revoked token is replayed (`Where(r => r.UserId == stored.UserId && r.RevokedAtUtc == null)`),
which also signs out innocent devices and lets one stale client log a user out everywhere
(`014-refresh-tokens` Known Gaps).

**Independent Test**: Log in twice as the same user (tokens A1 and B1), refresh A1 to get A2, replay
A1 (expect `401`), then confirm A2 is rejected (family revoked) while B1 still refreshes
successfully.

**Acceptance Scenarios**:

1. **Given** two logins by the same user (two families), **When** a revoked token of family A is
   replayed, **Then** the response is `401`, every active token of family A is revoked, and family
   B's tokens remain usable.
2. **Given** a single login whose token was rotated, **When** the old token is replayed, **Then**
   the successor token is also rejected (the behavior of the existing test
   `ReusedRefreshToken_Returns401_AndRevokesFamily` is preserved).
3. **Given** a token revoked by logout, **When** it is replayed, **Then** the response is `401` and
   the rest of its family is revoked, but other families are not.
4. **Given** pre-existing token rows that received a `FamilyId` from the migration, **When** one is
   replayed, **Then** no other user's tokens are affected.

---

### User Story 2 - Concurrent refreshes with the same token are safe (Priority: P1)

If two requests present the same refresh token at the same moment, at most one succeeds in rotating
it. The other receives `401` and is not mistaken for a replay attack, so the winner's new token
stays valid.

**Why this priority**: `014-refresh-tokens` Edge Cases state concurrent refreshes "are not serialized;
one may succeed and the other be treated as reuse", which can sign a legitimate client out (for
example the MVC `TokenRefreshHandler` racing with another request).

**Independent Test**: Force a competing modification of the token row between the read and the save
of a refresh (test `SaveChangesInterceptor`), and confirm the request returns `401`, no family
revocation happens, and the winner's token still refreshes.

**Acceptance Scenarios**:

1. **Given** two refresh requests with the same active token, **When** both reach the save step,
   **Then** exactly one rotation is persisted and the other request gets `401`.
2. **Given** the losing request, **When** it fails on a concurrency conflict, **Then** the reuse
   revocation sweep does NOT run and the winner's new token remains usable.
3. **Given** a refresh and a logout racing on the same token, **When** the logout loses the
   conflict, **Then** logout still returns `204` and the token ends up revoked.

---

### User Story 3 - Old refresh tokens are purged automatically (Priority: P2)

A background job periodically deletes refresh-token rows that expired or were revoked more than a
retention period ago, so the `RefreshTokens` table does not grow without bound.

**Why this priority**: Rows are currently never deleted (`014-refresh-tokens` Known Gaps); this is a
slow-growing operational problem rather than a security defect.

**Independent Test**: Seed tokens (old expired, old revoked, recently expired, active), call
`RefreshTokenCleanupService.PurgeAsync(now)`, and confirm only the old ones are removed.

**Acceptance Scenarios**:

1. **Given** tokens whose `ExpiresAtUtc` or `RevokedAtUtc` is older than `now - RetentionDays`,
   **When** the purge runs, **Then** they are deleted and the count is returned.
2. **Given** active tokens and tokens expired or revoked within the retention window, **When** the
   purge runs, **Then** they are kept.
3. **Given** the API is running, **When** `IntervalMinutes` elapses, **Then** the purge runs; a
   failing run is logged and the next interval still runs.

---

### User Story 4 - Logout is rate limited (Priority: P2)

`POST /api/account/logout` is limited per client IP like login, register and refresh, returning
`429` when exceeded.

**Why this priority**: The endpoint is unauthenticated and is the only account endpoint without a
limiter (`014-refresh-tokens` Known Gaps).

**Independent Test**: With a low `RateLimiting:LogoutPermitLimit`, call logout past the limit and
confirm `429`.

**Acceptance Scenarios**:

1. **Given** the limit is N per minute per IP, **When** N+1 logout requests arrive in a minute,
   **Then** request N+1 gets `429`.
2. **Given** normal use (below the limit), **When** logout is called, **Then** it still returns
   `204` for valid, unknown, and already-revoked tokens.

---

### User Story 5 - Last-admin protection survives simultaneous demotions (Priority: P2)

Two admins demoted at the same moment can no longer both succeed and leave the system with zero
admins. At least one admin always remains.

**Why this priority**: `UsersController.UpdateRole` does a check-then-update without isolation, so
two concurrent requests can both pass the check (`013-user-registration-management`).

**Independent Test**: Sequential regression on InMemory (`DemotingLastAdmin_Returns409` still
passes; demoting one of two admins succeeds). True concurrency can only be verified manually
against SQL Server (see Known Gaps).

**Acceptance Scenarios**:

1. **Given** exactly two admins, **When** both are demoted concurrently, **Then** one request
   returns `200` and the other `409 Cannot remove the last admin.`
2. **Given** exactly one admin, **When** that admin is demoted, **Then** `409` (unchanged).
3. **Given** the transaction is aborted as a deadlock victim, **When** the request retries once,
   **Then** the check is re-evaluated against committed data and yields `200` or `409`, not `500`.

---

### Edge Cases

- A replayed token whose row was already purged is "unknown" and returns `401` without any
  revocation sweep (purge only removes tokens expired/revoked longer than the retention period).
- Purge deletes revoked tokens from the middle of a family chain; this does not affect active
  tokens of that family.
- A concurrency conflict inside the reuse sweep itself (two replays at once) is caught; the request
  still returns `401`.
- `RetentionDays` or `IntervalMinutes` configured as zero or negative falls back to the defaults
  (7 and 60).
- `POST /api/account/register` does not issue tokens (it returns `201` with `UserResponseDto`), so
  it creates no family; the MVC register flow obtains tokens by calling login.
- Access tokens (JWT) already issued stay valid until their own expiry after any revocation.
- A rate-limited logout in MVC is best-effort (failures ignored), so `429` does not break MVC logout.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Each `RefreshToken` MUST have a `FamilyId` (`Guid`). Login MUST create a new family;
  rotation (`Refresh`) MUST copy the replaced token's `FamilyId` to its successor.
- **FR-002**: When a revoked refresh token is presented, the system MUST revoke all active tokens
  WHERE `UserId == token.UserId AND FamilyId == token.FamilyId` and return `401`; tokens of other
  families and other users MUST NOT be affected.
- **FR-003**: `RefreshToken` MUST have a `ConcurrencyStamp` (`Guid`) configured as an EF
  concurrency token and regenerated every time the row is modified (rotation, logout, reuse sweep).
- **FR-004**: When refresh hits a `DbUpdateConcurrencyException`, the system MUST return `401` and
  MUST NOT run the reuse revocation sweep.
- **FR-005**: Logout MUST treat a `DbUpdateConcurrencyException` as success (`204`), since the
  token is being revoked anyway.
- **FR-006**: A hosted background service MUST periodically call a scoped
  `RefreshTokenCleanupService.PurgeAsync(DateTime utcNow)` which deletes tokens with
  `ExpiresAtUtc < now - RetentionDays` or (`RevokedAtUtc != null` and `RevokedAtUtc < now - RetentionDays`).
- **FR-007**: The cleanup MUST be configured by section `RefreshTokenCleanup` (`IntervalMinutes`
  default 60, `RetentionDays` default 7); errors MUST be logged and MUST NOT stop the loop.
- **FR-008**: `POST /api/account/logout` MUST be protected by a named rate-limit policy `logout`
  per client IP (fixed window 1 minute, `RateLimiting:LogoutPermitLimit`, default 30) and return
  `429` when exceeded.
- **FR-009**: `UsersController.UpdateRole` MUST perform the last-admin count check and the update
  inside one `IsolationLevel.Serializable` transaction when `Database.IsRelational()`; otherwise
  (EF InMemory in tests) it MUST run without an explicit transaction.
- **FR-010**: If the serializable transaction fails as a deadlock victim, `UpdateRole` MUST retry
  once; a repeated failure is left to the global exception handler (`500` ProblemDetails).
- **FR-011**: The schema change MUST ship as one EF Core migration that adds `FamilyId` and
  `ConcurrencyStamp` (default `Guid.Empty` for existing rows; the generated migration is not hand-edited).
- **FR-012**: Existing response shapes and status codes for login, refresh, logout (except `429`)
  and role change MUST NOT change.

### Key Entities

- **RefreshToken** (existing, `CategoryProduct.API/Models/RefreshToken.cs`): gains `FamilyId` (the
  session lineage; all rotations of one login share it) and `ConcurrencyStamp` (optimistic
  concurrency token).
- **RefreshTokenCleanupService** (new, scoped): purge logic, directly testable.
- **RefreshTokenCleanupWorker** (new, `BackgroundService`): timer loop that opens a scope and
  calls the service.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Replaying a revoked token revokes 100% of its family's active tokens and 0 tokens of
  other families or users (integration tests).
- **SC-002**: A refresh that loses a concurrent update returns `401` and revokes 0 additional
  tokens (interceptor-driven integration test).
- **SC-003**: After a purge with `RetentionDays = 7`, 0 tokens older than the cutoff remain and 100%
  of newer/active tokens remain (unit-level test).
- **SC-004**: Logout returns `429` after the configured number of calls per minute per IP.
- **SC-005**: On SQL Server, 100% of simultaneous-demotion attempts on a two-admin system leave at
  least one admin (manual verification; not provable on InMemory).

## Assumptions

- **Family model**: `FamilyId` (`Guid`) is added to `RefreshToken`. Login creates a new family
  (`TokenService.CreateRefreshToken` receives the family id); rotation copies the parent's
  `FamilyId`. Reuse revokes active tokens WHERE `UserId == token.UserId AND FamilyId == token.FamilyId`
  (scoped by user too, so legacy rows with a default `FamilyId` can never affect other users). Existing
  rows are not backfilled: they keep `FamilyId = Guid.Empty`, so one user's legacy tokens form one
  legacy family (old per-user behavior for those rows only). Backfilling would require hand-editing
  the generated migration, which Constitution Principle II forbids (decided during Analyze).
  Registration was verified not to issue tokens (`AccountController.Register` returns `201` with
  `UserResponseDto` only), so only login creates a family; the earlier assumption "login/register
  creates a family" is narrowed to login.
- **Concurrency token**: a `Guid ConcurrencyStamp` with `.IsConcurrencyToken()` (works on SQL
  Server and EF InMemory, unlike `rowversion`). It is regenerated through a single
  `RefreshToken.Revoke(DateTime utcNow)` helper used at every site that modifies a token row.
- **Cleanup**: `BackgroundService` with `PeriodicTimer`; the first purge happens after one interval
  (not at startup, so tests are unaffected); delete logic uses load + `RemoveRange` because
  `ExecuteDeleteAsync` is not supported by InMemory.
- **Logout limit**: policy `logout`, 30 per minute per IP (same as `refresh`, the other endpoint
  clients call routinely), configurable via `RateLimiting:LogoutPermitLimit`; tests set 1000 in
  `ApiFactory` like the other limits.
- **Last admin**: serializable transaction on relational providers; skipped on InMemory. Because
  two serializable transactions that both read then write can deadlock on SQL Server, one retry is
  added (FR-010).
- **Migration**: created later with `dotnet ef migrations add AuthHardening`; `dotnet ef database
  update` is never run by this feature's tasks.
- MVC (`CategoryProduct.MVC`) is unchanged: logout is best-effort and refresh failures are already
  treated as "not refreshed".

## Known Gaps

- The concurrency and transaction behavior (US2 optimistic concurrency, US5 serializable transaction
  and deadlock retry, rollback of a lost refresh save) and the manual `quickstart.md` walkthrough
  (T024) were not verified against SQL Server; only EF InMemory tests were run. The generated
  `AuthHardening` migration was not applied to any database.
- True concurrency (US2 simultaneous requests, US5 simultaneous demotions) cannot be reproduced on
  EF InMemory; US2 is verified by a deterministic interceptor, US5 only sequentially. Real
  verification needs SQL Server (manual).
- On EF InMemory `SaveChanges` is not atomic, so a losing refresh may leave an orphan, never-returned
  successor row in tests; SQL Server rolls the whole save back.
- Refresh tokens issued before this migration share one legacy family per user (`Guid.Empty`);
  replaying one of them revokes that user's other legacy tokens, until they expire.
- No grace window for racing clients: the loser gets `401` and must retry with the winner's token
  (MVC `TokenRefreshHandler` behavior after a lost race was not verified in a browser).
- The concurrency token only protects refreshes that both read the token before either commits. If
  the second request reads the row after the first has committed, it sees a revoked token and is
  handled as reuse, which revokes the family (including the winner's new token). Closing that needs
  a grace window, which is out of scope (found in Review).
- Purge is not coordinated across multiple API instances (concurrent purges are harmless but
  redundant).
- Existing gaps from 014 not in scope remain: no tests for MVC handlers, the status for a missing
  `refreshToken` body is unverified, access tokens cannot be revoked.
