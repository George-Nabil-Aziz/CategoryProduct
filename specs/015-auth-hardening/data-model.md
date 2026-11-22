# Data Model: Auth Hardening

**Migration**: `AuthHardening` (created by `dotnet ef migrations add AuthHardening` in
`CategoryProduct.API/Migrations/`). Never applied by this feature's tasks (no `dotnet ef database update`).

## RefreshToken (changed)

`CategoryProduct.API/Models/RefreshToken.cs`. Existing fields (`Id`, `UserId`, `User`, `TokenHash`,
`CreatedAtUtc`, `ExpiresAtUtc`, `RevokedAtUtc`, `ReplacedByTokenHash`) are unchanged.

| Field | CLR type | Column | Rules |
|-------|----------|--------|-------|
| `FamilyId` | `Guid` | `uniqueidentifier` not null | New on login; copied on rotation; legacy rows keep `Guid.Empty` (no backfill) |
| `ConcurrencyStamp` | `Guid` | `uniqueidentifier` not null | `.IsConcurrencyToken()`; `Guid.NewGuid()` on creation and on every modification via `RefreshToken.Revoke(utcNow)` |

New method (no column): `void Revoke(DateTime utcNow)` sets `RevokedAtUtc = utcNow` and
`ConcurrencyStamp = Guid.NewGuid()`.

`ApplicationDbContext.OnModelCreating`: `b.Property(r => r.ConcurrencyStamp).IsConcurrencyToken();`
No new index (YAGNI): the unique `TokenHash` lookup finds the token and the sweep filters rows
already narrowed by `IX_RefreshTokens_UserId`.

## Migration shape (expected after `migrations add`)

`Up`: `AddColumn<Guid>("FamilyId", "RefreshTokens", nullable: false, defaultValue: Guid.Empty)` and
`AddColumn<Guid>("ConcurrencyStamp", "RefreshTokens", nullable: false, defaultValue: Guid.Empty)`.
No hand edits (Constitution Principle II); legacy rows keep `Guid.Empty`.
`Down`: drops both columns. The snapshot is generated, not hand-edited.

## Configuration (not stored)

| Key | Default | Meaning |
|-----|---------|---------|
| `RefreshTokenCleanup:IntervalMinutes` | 60 | Time between purges |
| `RefreshTokenCleanup:RetentionDays` | 7 | Keep expired/revoked tokens this long |
| `RateLimiting:LogoutPermitLimit` | 30 | Logout calls per minute per IP |

## State

active = `RevokedAtUtc` null and `ExpiresAtUtc` future; revoked = `RevokedAtUtc` set; purgeable =
`ExpiresAtUtc < now - RetentionDays` or `RevokedAtUtc < now - RetentionDays`.
