# Research: Auth Hardening

## Verified facts about the current code

- `AccountController.Refresh` (`CategoryProduct.API/Controllers/AccountController.cs`): on a revoked
  token it loads `Where(r => r.UserId == stored.UserId && r.RevokedAtUtc == null)`, sets
  `RevokedAtUtc`, saves, returns `401`. The sweep today is per user, the opposite of "per chain";
  the gap is that it is too broad.
- `Register` returns `201` with `UserResponseDto` and issues no tokens. Only `Login` calls
  `IssueAsync(user)`; `Refresh` calls `IssueAsync(user, stored)`.
- `Logout` has no `[EnableRateLimiting]`; the `login`, `register`, `refresh` policies exist in
  `Program.cs` (fixed window 1 minute per IP; defaults 5, 5, 30; keys `RateLimiting:*PermitLimit`).
- `UsersController.UpdateRole` does `AnyAsync(u => u.Role == "Admin" && u.Id != id)` then
  `SaveChangesAsync()` with no transaction.
- `Program.cs` registers `UseSqlServer(...)` without a retrying execution strategy, so user-initiated
  transactions are allowed. No hosted services exist yet.
- Tests (`CategoryProduct.API.Tests/ApiFactory.cs`) swap the DbContext to `UseInMemoryDatabase` and
  set `RateLimiting:*PermitLimit` via `UseSetting`; `RateLimitTests.LimitedFactory` in `ApiTests.cs`
  shows the derived-factory pattern for rate-limit tests.
- Latest migration is `20261103183150_AddRefreshTokens`; `ApplicationDbContextModelSnapshot.cs` holds
  the `RefreshToken` entity.

## Decision 1: FamilyId scoped by user

**Decision**: add `Guid FamilyId`; sweep `UserId == X && FamilyId == Y && RevokedAtUtc == null`.
**Rationale**: keeps sibling sessions alive; the `UserId` predicate bounds the blast radius for legacy rows.
**Alternatives**: walking `ReplacedByTokenHash` to find the chain (rejected: iterative loading; a
family column is one predicate); keeping the per-user sweep (rejected: this feature's goal).

## Decision 2: No backfill; legacy rows keep Guid.Empty

**Decision**: the generated migration adds `FamilyId` and `ConcurrencyStamp` with default
`Guid.Empty`; it is not hand-edited (Constitution Principle II forbids hand-editing generated
migrations). **Rationale**: because the reuse sweep is scoped by `UserId` AND `FamilyId`, legacy
tokens of one user share a single legacy family, which equals the old per-user behavior for those
rows only and never touches other users; legacy tokens expire within the refresh lifetime.
`ConcurrencyStamp = Guid.Empty` on legacy rows is still a valid concurrency token (the first
modification regenerates it). **Alternative rejected**: a `migrationBuilder.Sql(... NEWID())`
backfill (violates Principle II; decided during Analyze).

## Decision 3: Guid ConcurrencyStamp, not rowversion

**Decision**: `Guid ConcurrencyStamp` + `IsConcurrencyToken()`, regenerated in `RefreshToken.Revoke`
and set on creation.
**Rationale**: EF InMemory does not generate `rowversion` values; a client-set Guid works on both
providers and is testable. A concurrent update yields `DbUpdateConcurrencyException`
(`UPDATE ... WHERE Id = @id AND ConcurrencyStamp = @old` affects 0 rows).
**Alternatives**: `[Timestamp]` (rejected: not testable on InMemory); serializable transaction around
refresh (rejected: heavier and deadlock-prone).

## Decision 4: Handling the conflict

**Decision**: in `Refresh`, wrap `IssueAsync(user, stored)` in try/catch
`DbUpdateConcurrencyException` and return `Unauthorized()` without a sweep. The reuse sweep's own
`SaveChangesAsync` is also wrapped; a conflict there still returns `401`. `Logout` catches the
exception and returns `204`.
**Rationale**: the loser is not a replay; the winner's token must stay usable.
**Note**: `IssueAsync` adds the successor and modifies the old token in one `SaveChangesAsync`, so on
SQL Server the loser's successor is rolled back. EF InMemory is not transactional and may keep an
orphan successor row that was never returned to a client; harmless.

## Decision 5: Cleanup design

**Decision**: `RefreshTokenCleanupWorker : BackgroundService` (`PeriodicTimer`, interval from
`RefreshTokenCleanup:IntervalMinutes`, default 60) creating a scope per tick and calling scoped
`RefreshTokenCleanupService.PurgeAsync(DateTime utcNow)`; `RetentionDays` default 7. Cutoff =
`utcNow - RetentionDays`; delete where `ExpiresAtUtc < cutoff || (RevokedAtUtc != null && RevokedAtUtc < cutoff)`.
Load + `RemoveRange` (InMemory has no `ExecuteDelete`). Exceptions are logged via `ILogger` and the
loop continues; `OperationCanceledException` on shutdown ends it. Registered with
`AddScoped<RefreshTokenCleanupService>()` and `AddHostedService<RefreshTokenCleanupWorker>()`.
Configuration is read with `IConfiguration.GetValue` like the rest of `Program.cs` (no Options pattern).
**Rationale**: the two-class split exists because tests are a real second consumer of the purge logic
(Constitution III).

## Decision 6: Logout limiter

**Decision**: policy `logout`, fixed window 1 minute per IP, default 30, key `RateLimiting:LogoutPermitLimit`.
**Rationale**: logout is a routine client call like refresh (30), not a credential-guessing target like login (5).

## Decision 7: Last admin transaction

**Decision**: if `_context.Database.IsRelational()`, `BeginTransactionAsync(IsolationLevel.Serializable)`
around check + save + commit; otherwise run the same code without a transaction. Retry once when
`SaveChangesAsync`/commit throws a `DbUpdateException` whose inner exception is a
`Microsoft.Data.SqlClient.SqlException` with `Number == 1205` (deadlock). `Microsoft.Data.SqlClient`
arrives transitively through `Microsoft.EntityFrameworkCore.SqlServer`, so no new package.
**Rationale**: serializable range locks make the count check and update atomic; two concurrent
demotions both take shared range locks then wait on each other, so SQL Server picks a deadlock
victim; the retry re-reads committed data and returns `409`.
**Alternatives**: `UPDLOCK, HOLDLOCK` via raw SQL (rejected: leaves LINQ); a concurrency token on
`User` (rejected: does not protect a count over other rows).
