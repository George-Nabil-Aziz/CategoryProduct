# Research: User Registration & Management

## Decision 1: Reuse the existing `User.Role` column and unique index (no migration)

**Decision**: No new migration. `Role` (`nvarchar(20)`, default `User`) was added by
`20261103173834_AddUserRole`; the unique index `IX_Users_UserName` and `UserName nvarchar(450)` come
from `20260820083821_UpdateUsersTableForAuth` (verified in both migration files).

**Rationale**: Constitution Principle II requires a migration only for schema changes; none was
needed.

## Decision 2: Application pre-check plus unique-index backstop for duplicates

**Decision**: Register first checks `UserName.ToLower() == lower` and returns `409`; it also wraps
`SaveChangesAsync` in a `DbUpdateException` catch that returns the same `409`.

**Rationale**: The pre-check gives a friendly case-insensitive answer on any provider (tests use the
in-memory provider); the index plus catch handles races.

## Decision 3: Validation via DataAnnotations on a record DTO

**Decision**: `RegisterRequestDto` carries `[Required]`, `[StringLength(50, MinimumLength = 3)]`,
`[RegularExpression]` for `UserName` and `[Required]`, `[MinLength(8)]` for `Password`. `[ApiController]`
produces the `400` automatically, as established in `002-request-validation`.

**Alternatives considered**: FluentValidation rejected (new dependency, Principle III).

## Decision 4: Role-based authorization with `[Authorize(Roles = "Admin")]`

**Decision**: Class-level attribute on `api/users`. The `Role` claim is already placed in access
tokens at login.

**Rationale**: Built-in mechanism; no custom policy needed for two fixed roles.

## Decision 5: Last-admin protection by query, not constraint

**Decision**: When demoting an admin, check `AnyAsync(u => u.Role == "Admin" && u.Id != id)` and
return `409` if no other admin exists.

**Rationale**: Smallest rule that satisfies the requirement. Not transactionally serialized, so two
simultaneous demotions of different last admins could in theory race; accepted for current scale.

## Decision 6: Rate limiting `register` like `login`

**Decision**: Fixed-window limiter per remote IP, 1-minute window, `QueueLimit = 0`, permit limit
from `RateLimiting:RegisterPermitLimit` (default `5`), global rejection status `429`. Tests set the
limit to 1000 in `ApiFactory`.

## Decision 7: MVC consumes the API only

**Decision**: `CategoryProductApiClient` gained `RegisterAsync`, `GetUsersAsync`,
`ChangeUserRoleAsync`; MVC `AccountController`/`UsersController` use it. The MVC `RegisterViewModel`
duplicates validation rules client-side and adds `ConfirmPassword`. After successful registration the
MVC logs the user in through the API and signs the cookie in; if that login fails or is rate limited
it redirects to the Login page.

**Rationale**: Principle V (no shared types or DbContext).
