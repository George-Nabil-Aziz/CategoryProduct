---

description: "Task list for Auth Hardening"
---

# Tasks: Auth Hardening

**Input**: Design documents from `/specs/015-auth-hardening/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Included (integration tests per story in `CategoryProduct.API.Tests`, plus a unit-level
test for `PurgeAsync`).

**Organization**: grouped by user story; US1 and US2 are P1, US3 to US5 are P2.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependencies)
- **[Story]**: US1 to US5

## Path Conventions

`CategoryProduct.API/` and `CategoryProduct.API.Tests/` at repository root. Never run
`dotnet ef database update`.

---

## Phase 1: Setup

- [X] T001 [P] Add non-secret defaults `"RefreshTokenCleanup": { "IntervalMinutes": 60, "RetentionDays": 7 }` to `CategoryProduct.API/appsettings.json`
- [X] T002 [P] Add `builder.UseSetting("RateLimiting:LogoutPermitLimit", "1000");` to `CategoryProduct.API.Tests/ApiFactory.cs`

---

## Phase 2: Foundational (blocks US1, US2)

**CRITICAL**: model, DbContext and migration changes underpin US1 and US2.

- [X] T003 Add `Guid FamilyId`, `Guid ConcurrencyStamp = Guid.NewGuid()` and `void Revoke(DateTime utcNow)` (sets `RevokedAtUtc` and a new `ConcurrencyStamp`) to `CategoryProduct.API/Models/RefreshToken.cs`
- [X] T004 Configure `b.Property(r => r.ConcurrencyStamp).IsConcurrencyToken();` in `CategoryProduct.API/Data/ApplicationDbContext.cs` (depends on T003)
- [X] T005 Change `CreateRefreshToken(int userId)` to `CreateRefreshToken(int userId, Guid familyId)` and set `FamilyId` and a fresh `ConcurrencyStamp` in `CategoryProduct.API/Services/TokenService.cs` (depends on T003)
- [X] T006 Generate the migration from `CategoryProduct.API/` with: `dotnet ef migrations add AuthHardening --project CategoryProduct.API` (depends on T003, T004). Do NOT hand-edit the generated migration or snapshot. Do NOT run `dotnet ef database update`.

**Checkpoint**: `dotnet build` passes once T007 updates the `CreateRefreshToken` caller.

---

## Phase 3: User Story 1 - Family-scoped reuse revocation (Priority: P1) MVP

**Goal**: replaying a revoked token revokes only its family (same `UserId` and `FamilyId`).

### Tests for User Story 1

- [X] T007a [P] [US1] In `CategoryProduct.API.Tests/RefreshTokenTests.cs` add `ReuseRevokesOnlyOwnFamily_OtherSessionSurvives`: two logins as `ApiFactory.UserName` (A1, B1), refresh A1 to A2, replay A1 expect `401`, A2 expect `401`, B1 expect `200`
- [X] T008a [P] [US1] In `RefreshTokenTests.cs` add `ReuseAfterLogout_RevokesOnlyOwnFamily`: login twice, logout token 1, replay token 1 expect `401`, token 2 still refreshes `200`
- [X] T009a [P] [US1] In `RefreshTokenTests.cs` add `LegacyDefaultFamily_DoesNotAffectOtherUsers`: seed via `ApiFactory.Services` scope two revoked/active `RefreshToken` rows for different users with `FamilyId = Guid.Empty`, replay the revoked one, assert the other user's token remains unrevoked in the `ApplicationDbContext`

### Implementation for User Story 1

- [X] T007 [US1] In `CategoryProduct.API/Controllers/AccountController.cs` update `IssueAsync`: login passes `Guid.NewGuid()` as family; rotation passes `replacing.FamilyId`; use `replacing.Revoke(DateTime.UtcNow)` instead of assigning `RevokedAtUtc` (depends on T005)
- [X] T008 [US1] In `AccountController.Refresh` change the reuse sweep to `Where(r => r.UserId == stored.UserId && r.FamilyId == stored.FamilyId && r.RevokedAtUtc == null)` and call `t.Revoke(now)` on each (depends on T007)

**Checkpoint**: US1 tests and the existing `ReusedRefreshToken_Returns401_AndRevokesFamily` pass.

---

## Phase 4: User Story 2 - Concurrency-safe refresh (Priority: P1)

**Goal**: a refresh that loses a concurrent update returns `401` without a reuse sweep.

### Tests for User Story 2

- [X] T009 [P] [US2] In `CategoryProduct.API.Tests/RefreshTokenTests.cs` add `ConcurrencyStamp_DetectsCompetingUpdate`: two `ApplicationDbContext` instances over the same InMemory database load one token, the first revokes and saves, the second modifies and saves and must throw `DbUpdateConcurrencyException`
- [X] T010 [P] [US2] In `RefreshTokenTests.cs` add `LostConcurrentRefresh_Returns401_AndDoesNotRevokeFamily` using a nested `WebApplicationFactory` subclass of `ApiFactory` that registers a `SaveChangesInterceptor` which, once, before the first save containing a modified `RefreshToken`, updates that token's `ConcurrencyStamp` through a separate context; assert `401`, then assert the family's other active token (login on a second session) still refreshes `200` and no extra token has `RevokedAtUtc` set
- [X] T011 [P] [US2] In `RefreshTokenTests.cs` add `LogoutLosingConcurrentUpdate_StillReturns204` using the same interceptor approach on `api/account/logout`

### Implementation for User Story 2

- [X] T012 [US2] In `CategoryProduct.API/Controllers/AccountController.cs` wrap `IssueAsync(user, stored)` in `Refresh` with `try/catch (DbUpdateConcurrencyException) { return Unauthorized(); }` without any sweep; wrap the sweep's `SaveChangesAsync` the same way (depends on T008)
- [X] T013 [US2] In `AccountController.Logout` use `stored.Revoke(DateTime.UtcNow)` and catch `DbUpdateConcurrencyException`, still returning `NoContent()` (depends on T012)

**Checkpoint**: US2 tests pass; no `500` from refresh/logout races.

---

## Phase 5: User Story 3 - Purge old refresh tokens (Priority: P2)

### Tests for User Story 3

- [X] T014 [P] [US3] Create `CategoryProduct.API.Tests/RefreshTokenCleanupTests.cs` with unit-level tests that build an `ApplicationDbContext` on `UseInMemoryDatabase`, seed tokens (expired 10 days ago, revoked 10 days ago, expired 1 day ago, revoked 1 day ago, active), call `new RefreshTokenCleanupService(db, config).PurgeAsync(now)` and assert only the first two are removed and the return value is 2; add a case for `RetentionDays <= 0` falling back to 7

### Implementation for User Story 3

- [X] T015 [US3] Create `CategoryProduct.API/Services/RefreshTokenCleanupService.cs` (scoped; ctor `ApplicationDbContext`, `IConfiguration`; `Task<int> PurgeAsync(DateTime utcNow, CancellationToken ct = default)` using load + `RemoveRange`, cutoff from `RefreshTokenCleanup:RetentionDays` default 7 with <= 0 fallback)
- [X] T016 [US3] Create `CategoryProduct.API/Services/RefreshTokenCleanupWorker.cs` (`BackgroundService`; `PeriodicTimer` from `RefreshTokenCleanup:IntervalMinutes` default 60 with <= 0 fallback; each tick creates a scope via `IServiceScopeFactory`, calls `PurgeAsync(DateTime.UtcNow, stoppingToken)`, logs the count, catches and logs exceptions except cancellation) (depends on T015)
- [X] T017 [US3] Register `builder.Services.AddScoped<RefreshTokenCleanupService>()` and `builder.Services.AddHostedService<RefreshTokenCleanupWorker>()` in `CategoryProduct.API/Program.cs` (depends on T016)

---

## Phase 6: User Story 4 - Logout rate limit (Priority: P2)

### Tests for User Story 4

- [X] T018 [P] [US4] In `CategoryProduct.API.Tests/ApiTests.cs` extend `RateLimitTests.LimitedFactory` with `protected virtual int LogoutPermitLimit => 1000;` (add the virtual property and `UseSetting` in `ApiFactory.cs`, override with `2` in `LimitedFactory`) and add `Logout_ExceedingLimit_Returns429` (two `204`s then `429`)

### Implementation for User Story 4

- [X] T019 [US4] In `CategoryProduct.API/Program.cs` read `RateLimiting:LogoutPermitLimit` (default 30) and add `options.AddPolicy("logout", ...)` mirroring the `refresh` policy (fixed window 1 minute per IP, `QueueLimit = 0`)
- [X] T020 [US4] Add `[EnableRateLimiting("logout")]` to `Logout` in `CategoryProduct.API/Controllers/AccountController.cs` (depends on T019)

---

## Phase 7: User Story 5 - Last-admin transaction (Priority: P2)

### Tests for User Story 5

- [X] T021 [P] [US5] In `CategoryProduct.API.Tests/UsersTests.cs` `LastAdminTests` add `DemotingOneOfTwoAdmins_Succeeds` (register a user, promote to Admin, demote the original admin, expect `200`; then demoting the remaining admin expects `409`); runs on InMemory without a transaction

### Implementation for User Story 5

- [X] T022 [US5] In `CategoryProduct.API/Controllers/UsersController.cs` `UpdateRole`: when `_context.Database.IsRelational()`, run check + `SaveChangesAsync` + commit inside `BeginTransactionAsync(IsolationLevel.Serializable)`; otherwise run the same logic without a transaction; retry the whole transaction once when the exception or any inner exception is a `Microsoft.Data.SqlClient.SqlException` with `Number == 1205` (a deadlock can surface on the count query or on save) (re-fetch the user inside the retry)

---

## Phase 8: Polish

- [X] T023 Run `dotnet build` and `dotnet test` for the solution and fix regressions (existing `RefreshTokenTests`, `UsersTests`, `RateLimitTests`)
- [ ] T024 Walk through `specs/015-auth-hardening/quickstart.md` manual steps against SQL Server (concurrency items cannot be verified on InMemory) and record any deviations in spec.md Known Gaps
- [X] T025 Update `specs/014-refresh-tokens/spec.md` Known Gaps to point at `015-auth-hardening` for the five closed items

---

## Dependencies & Execution Order

- Phase 1 has no dependencies. Phase 2 (T003 to T006) blocks US1 and US2; T007 also fixes the build for the changed `CreateRefreshToken` signature.
- US1 (T007, T008) then US2 (T012, T013): same file `AccountController.cs`, so sequential.
- US3, US4 and US5 are independent of each other; US4 T020 touches `AccountController.cs` after US2.
- Test tasks marked [P] within a story touch the same test file but different methods; add them in one pass to avoid merge noise.
- T006 migration generation requires T003 and T004 only; it must precede T023.

### Parallel Opportunities

- T001 and T002; T014, T018, T021 test authoring; US3, US4 and US5 implementation after Phase 2.

## Implementation Strategy

MVP: Phases 1, 2 and US1. Then US2 (completes the P1 refresh safety), then US3 to US5 in any order.
