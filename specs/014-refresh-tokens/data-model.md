# Data Model: Refresh Tokens

**Migration**: `20261103183150_AddRefreshTokens` (`CategoryProduct.API/Migrations/`), applied with
`dotnet ef database update --project CategoryProduct.API`. `Down` drops the `RefreshTokens` table.

## RefreshToken (new)

`CategoryProduct.API/Models/RefreshToken.cs`; `DbSet<RefreshToken> RefreshTokens` on
`ApplicationDbContext`.

| Field | CLR type | Column | Rules |
|-------|----------|--------|-------|
| `Id` | `int` | `int` identity, PK `PK_RefreshTokens` | |
| `UserId` | `int` | `int` not null | FK to `Users.Id`, cascade delete |
| `User` | `User?` | navigation | `HasOne(r => r.User).WithMany()` |
| `TokenHash` | `string` | `nvarchar(128)` not null | Uppercase hex SHA-256 of the raw token; unique |
| `CreatedAtUtc` | `DateTime` | `datetime2` not null | Issue time |
| `ExpiresAtUtc` | `DateTime` | `datetime2` not null | Issue time + `Jwt:RefreshTokenDays` (default 7) |
| `RevokedAtUtc` | `DateTime?` | `datetime2` null | Set on rotation, logout, or reuse sweep |
| `ReplacedByTokenHash` | `string?` | `nvarchar(128)` null | Hash of the successor token when rotated |

**Indexes**: `IX_RefreshTokens_TokenHash` (unique), `IX_RefreshTokens_UserId`.

**Relationships**: many `RefreshToken` to one `User`; deleting a user deletes its tokens.

**State**: active = `RevokedAtUtc` null and `ExpiresAtUtc` in the future; revoked = `RevokedAtUtc`
set (reuse of such a token triggers the sweep); expired = past `ExpiresAtUtc`, not revoked
(rejected without sweep).

## DTOs

`LoginResponseDto(string Token, DateTime ExpiresAtUtc, string Role, string RefreshToken,
DateTime RefreshTokenExpiresAtUtc)` (extended; used by login and refresh).

`RefreshRequestDto(string RefreshToken)` (new; used by refresh and logout). Both in
`CategoryProduct.API/Dtos/LoginResponseDto.cs`.

## Access token (unchanged shape, not persisted)

Claims `NameIdentifier`, `Name`, `Role`; HMAC-SHA256; lifetime `Jwt:ExpiryMinutes` (default 60).
Now created by `TokenService.CreateAccessToken`.

## MVC cookie claims

`ClaimTypes.Name`, `ClaimTypes.Role`, `access_token`, `refresh_token`, `access_token_expires_at`
(ISO 8601 round-trip UTC). `CategoryProductApiClient.LoginResult` gained `RefreshToken` and
`RefreshTokenExpiresAtUtc`.

## Configuration

| Key | Default |
|-----|---------|
| `Jwt:ExpiryMinutes` | 60 (also set in `appsettings.json`) |
| `Jwt:RefreshTokenDays` | 7 (code default only) |
| `RateLimiting:RefreshPermitLimit` | 30 |
