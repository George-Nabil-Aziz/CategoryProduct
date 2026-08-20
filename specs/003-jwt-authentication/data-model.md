# Data Model: JWT Authentication

## User (EF Core model, modified)

`CategoryProduct.API/Models/User.cs`

| Field | Type | Rule(s) | Source |
|-------|------|---------|--------|
| `Id` | `int` | Primary key, identity — unchanged | existing |
| `UserName` | `string` | Required; unique, case-insensitive (enforced by a unique index, relying on the database's default case-insensitive collation — see `research.md` Decision 5) | Key Entities (Account) |
| `PasswordHash` | `string` | Required; holds only the output of `PasswordHasher<User>.HashPassword`, never a plaintext password. Renamed from `Password` | FR-001 |

**Migration required** (Constitution Principle II): a new EF Core migration that (a) renames the
`Password` column to `PasswordHash` and (b) adds a unique index on `UserName`. No other schema
change.

**Relationships**: none — `User` remains unrelated to `Category`/`Product`, consistent with the
"single access level" assumption (no ownership or per-user data scoping in this feature).

## Access Credential (JWT — not a persisted entity)

Not a database row; a signed, stateless token issued by `POST /api/Account/login` and validated on
every subsequent write request. Modeled here as its logical shape:

| Claim / property | Value | Source |
|---|---|---|
| `ClaimTypes.NameIdentifier` | `User.Id` (string) | FR-007 |
| `ClaimTypes.Name` | `User.UserName` | FR-007 |
| Expiration (`exp`) | Issued-at + `Jwt:ExpiryMinutes` (default 60) | FR-005 |
| Signature | HMAC-SHA256, keyed by `Jwt:Key` (never committed — see `research.md` Decision 3) | FR-001, Constitution IV |

No revocation list or server-side session is stored (per spec Assumptions — no logout/revocation
in scope); validity is entirely determined by signature and expiration at request time.

## New DTOs

`CategoryProduct.API/Dtos/LoginRequestDto.cs`

| Field | Type | Rule(s) | Source |
|-------|------|---------|--------|
| `UserName` | `string` | Required (non-blank) | FR-002 |
| `Password` | `string` | Required (non-blank) | FR-002 |

`CategoryProduct.API/Dtos/LoginResponseDto.cs`

| Field | Type | Notes |
|-------|------|-------|
| `Token` | `string` | The signed JWT |
| `ExpiresAtUtc` | `DateTime` | Absolute expiration instant, for the consumer's own bookkeeping |

## Unaffected entities/DTOs

- `Category`, `Product`, `CategoryRequestDto`/`ResponseDto`, `ProductRequestDto`/`ResponseDto` —
  untouched. This feature adds a credential *requirement* in front of existing write endpoints; it
  does not change their request/response shapes (FR-006).

## Login failure shape

A login with an unknown `UserName` or an incorrect `Password` both return `401 Unauthorized` with
the same generic body (no field-level detail, no indication of which part was wrong) — per FR-003
and the spec's edge case on not revealing which accounts exist.

## Protected-endpoint failure shape

A write request (`POST`/`PUT`/`DELETE` on `Category` or `Product`) with a missing, malformed,
expired, or otherwise invalid token returns `401 Unauthorized` via the standard `JwtBearer`
challenge — no custom body — before the action method runs and before any data changes (FR-004).
