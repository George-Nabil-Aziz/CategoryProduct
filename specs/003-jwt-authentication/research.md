# Research: JWT Authentication

## Decision 1: Password hashing mechanism

**Decision**: Use `Microsoft.AspNetCore.Identity.PasswordHasher<User>` to hash passwords at login
(verification) time and whenever a password is stored. This type ships in the
`Microsoft.Extensions.Identity.Core` assembly, which is part of the ASP.NET Core shared framework
already referenced by `Microsoft.NET.Sdk.Web` — no new `PackageReference` is required.

**Rationale**:
- Implements PBKDF2 with a random per-password salt and iteration count out of the box —
  satisfies FR-001 ("only a securely hashed form MUST ever be persisted") and Constitution
  Principle IV ("hashed with a modern password hashing algorithm") without hand-rolling crypto.
- Requires no new dependency, matching Principle III (Simplicity & YAGNI) — the full ASP.NET Core
  Identity system (`UserManager`, `SignInManager`, Identity stores) is *not* introduced; only the
  standalone hasher class is used, since the spec explicitly rules out self-service registration,
  roles, lockout, and recovery.
- `PasswordHasher<TUser>.HashPassword` / `.VerifyHashedPassword` are the documented building blocks
  for exactly this "roll your own login, but don't roll your own hashing" scenario.

**Alternatives considered**:
- **BCrypt.Net-Next**: a common alternative, but adds a new third-party `PackageReference` for
  something the shared framework already provides. Rejected — no functional gap `PasswordHasher`
  leaves open.
- **Full ASP.NET Core Identity (`AddIdentity<User, IdentityRole>`)**: brings user/role stores,
  `UserManager`, cookie + token providers, and EF Core Identity schema conventions. Far more than
  this feature needs (no roles, no self-service, no recovery) — rejected as premature scaffolding
  the constitution's Simplicity principle warns against.

## Decision 2: Token creation and validation library

**Decision**: Use the existing `Microsoft.AspNetCore.Authentication.JwtBearer` package (already
referenced in `CategoryProduct.API.csproj`) for both halves: `System.IdentityModel.Tokens.Jwt`
(`JwtSecurityTokenHandler`, `JwtSecurityToken`, `SigningCredentials`, `SymmetricSecurityKey`) —
a transitive dependency of the package — to *create* the token in `AccountController`, and the
`JwtBearer` authentication handler (`AddAuthentication().AddJwtBearer(...)`) to *validate* it on
incoming requests.

**Rationale**: The package is already present (per the Technology Stack section of the
constitution and the existing `.csproj`), so no new dependency is added. It is the standard,
Microsoft-supported way to issue and validate JWTs in ASP.NET Core, and keeps issuing/validating
symmetric with a single shared key and claim set.

**Alternatives considered**: A third-party JWT library (e.g., `Jose-JWT`) — rejected; the
already-referenced Microsoft package covers every requirement with no gap.

## Decision 3: Fixing the committed JWT signing key (Constitution Principle IV)

**Discovered state**: `CategoryProduct.API/appsettings.json` already contains a `Jwt` section with
a literal signing key (`"Key": "SecretKey123456789"`) committed to source control. This violates
Principle IV ("Real secrets... MUST NOT be committed to source control, including in
`appsettings.json`") even though nothing currently reads or uses it (`Program.cs` does not yet call
`AddAuthentication`/`AddJwtBearer`).

**Decision**: As part of this feature:
1. Remove the literal key value from the tracked `appsettings.json`, leaving only the non-secret
   `Issuer`/`Audience`/`ExpiryMinutes` settings and an empty/placeholder `Key` (empty string) so the
   shape of configuration is documented without exposing a usable secret.
2. For local development, the real key is set via .NET user secrets
   (`dotnet user-secrets set "Jwt:Key" "<value>"` inside `CategoryProduct.API/`), which
   `ASP.NET Core` layers over `appsettings.json` automatically in `Development` and is
   git-ignored by design (stored outside the repo, under the user profile).
3. For any non-development environment, the key is supplied via the `Jwt__Key` environment
   variable (ASP.NET Core's double-underscore convention for nested config), never committed.
4. The key must be a cryptographically random value of at least 32 bytes (256 bits) — sufficient
   for `HmacSha256Signature` — generated once (e.g., `openssl rand -base64 64`), not the short
   placeholder currently in the file.

**Rationale**: Principle IV is explicitly NON-NEGOTIABLE; leaving the existing committed key in
place (or wiring authentication up to actually use it) would ship a known, source-controlled
signing secret — anyone with repo access could forge tokens. Fixing it is in scope for this
feature because this feature is what makes the key security-relevant for the first time (today it
is unused dead configuration).

**Alternatives considered**: Leaving the key in `appsettings.json` "for now" — rejected outright;
this is the exact scenario Principle IV calls a hard gate, not a guideline.

## Decision 4: Where to store per-request identity, and how to protect write endpoints

**Decision**: Apply the `[Authorize]` attribute directly on the `POST`, `PUT`, and `DELETE` actions
of `CategoryController` and `ProductController` (not at the controller level). `GET` actions
receive no attribute and remain implicitly anonymous, since no global authorization policy is
registered.

**Rationale**: The spec requires reads to stay public and only writes to require a credential.
Attribute-per-write-action is the smallest change that expresses this directly at each affected
action, without relying on remembering to add `[AllowAnonymous]` to every current and future `GET`
action if a controller-level `[Authorize]` were used instead — one fewer way to accidentally lock
down a read endpoint by omission.

**Alternatives considered**: Controller-level `[Authorize]` + `[AllowAnonymous]` on each `GET`
action — equally correct today, but inverts the safe default (new actions are locked-down unless
explicitly opened up) in a codebase that currently has no other examples to learn the convention
from. Rejected in favor of the more explicit, harder-to-misuse option given there's no existing
precedent either way.

## Decision 5: `User` model changes and required migration

**Decision**: Rename the existing `User.Password` property/column to `PasswordHash` (it will only
ever hold the PBKDF2 output, never a plaintext password), and add a unique index on `UserName`.
Both changes ship as a single new EF Core migration, per Constitution Principle II.

**Rationale**: `Password` as a name invites a future contributor to assign a raw password to it
directly, defeating FR-001; `PasswordHash` documents the invariant in the type itself. A unique
index on `UserName` is required by the Key Entities section of the spec ("unique username") and
must exist at the database level, not just be assumed — nothing today enforces it.

**Assumption carried forward**: this project's SQL Server instance uses the default,
case-insensitive collation (e.g. `SQL_Latin1_General_CP1_CI_AS`), consistent with every other
string column already in this schema. A standard (case-sensitive-at-the-index-definition-level,
but case-insensitive-at-the-collation-level) unique index on `UserName` is therefore sufficient to
enforce case-insensitive uniqueness; no explicit collation override is added. If the target server
ever uses a case-sensitive collation, this would need revisiting — out of scope to guard against
here since nothing else in the project does.

**Alternatives considered**: Leaving the column named `Password` — rejected as misleading given
FR-001's emphasis; the rename is a one-line change already covered by the migration this feature
needs anyway for the unique index, so it adds no extra migration.

## Decision 6: Token claims and expiry

**Decision**: Issue a token with `ClaimTypes.NameIdentifier` (the account's `Id`, as a string) and
`ClaimTypes.Name` (the account's `UserName`) claims, signed with `HmacSha256Signature` using the
configured key, expiring `Jwt:ExpiryMinutes` (default `60`) minutes after issuance. Validation sets
`ClockSkew = TimeSpan.Zero`.

**Rationale**: `NameIdentifier` + `Name` are the minimum claims needed to satisfy FR-007 (identify
the requesting consumer without resending credentials); 60 minutes matches the spec's illustrative
"on the order of an hour" assumption. The default `JwtBearerOptions.ClockSkew` is 5 minutes, which
would let a token keep working up to 5 minutes past its stated expiry — directly conflicting with
SC-004 ("An access credential that has passed its expiration point is rejected on 100% of attempts
... with no exceptions"). Setting it to zero closes that gap.

**Alternatives considered**: Leaving the default 5-minute clock skew — rejected, conflicts with
SC-004 as stated above.

## Decision 7: Fixing `Program.cs` pipeline ordering

**Discovered state**: `Program.cs` already contains `app.UseAuthentication();` and a duplicate
`app.UseAuthorization();` — but *after* `app.Run()`, making them unreachable dead code (`app.Run()`
blocks until shutdown). Authentication/authorization services (`AddAuthentication`,
`AddAuthorization`) are also not yet registered in the DI container.

**Decision**: Register `AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(...)`
and `AddAuthorization()` in the service-registration section, then move `app.UseAuthentication()`
followed by `app.UseAuthorization()` into the request pipeline *before* `app.MapControllers()` and
*before* `app.Run()`, removing the unreachable lines after `app.Run()`.

**Rationale**: `UseAuthentication` must run before `UseAuthorization` and before endpoint execution
for `[Authorize]` to have any effect; the existing unreachable calls are corrected in place rather
than left alongside new, working ones (avoids duplicate/dead code).

## Decision 8: Testing / verification approach

**Decision**: No automated test project exists in the solution. Per Constitution "Development
Workflow" (also followed by `002-request-validation`), this feature is verified manually against
the running API via Swagger UI, exercising both the login endpoint and the write-endpoint
protection end-to-end.

**Rationale**: Consistent with prior features in this codebase; introducing a test project is a
separate, larger change not requested here.

## Summary of resolved unknowns

No `NEEDS CLARIFICATION` markers remain. The technical decisions above (hashing mechanism, JWT
library usage, secret handling, endpoint protection strategy, model/migration changes, claims and
expiry, pipeline wiring, verification approach) fully resolve the Technical Context for Phase 1.
