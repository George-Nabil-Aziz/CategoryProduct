# Data Model: Role-Based Authorization

## User (EF Core model, modified)

`CategoryProduct.API/Models/User.cs`

| Field | Type | Rule(s) | Source |
|-------|------|---------|--------|
| `Id` | `int` | Primary key - unchanged | existing |
| `UserName` | `string` | Unique index - unchanged (003) | existing |
| `PasswordHash` | `string` | unchanged (003) | existing |
| `Role` | `string` | Required, `MaxLength(20)`, default `"User"` (both as C# initializer and database default) | FR-001 |

`CategoryProduct.API/Data/ApplicationDbContext.cs` configures `Role` with `.HasMaxLength(20).IsRequired().HasDefaultValue("User")`. There is no seeding in the `DbContext`.

**Migration required** (Constitution Principle II): `CategoryProduct.API/Migrations/20261103173834_AddUserRole.cs` (with its designer and the updated model snapshot).

- `Up`: add column `Role` to `Users` (`nvarchar(20)`, not null, default `User`), then `UPDATE Users SET Role = 'Admin' WHERE UserName = 'testuser'`.
- `Down`: drop column `Role`.

Apply with `dotnet ef database update --project CategoryProduct.API`.

## LoginResponseDto (modified)

`CategoryProduct.API/Dtos/LoginResponseDto.cs`: `record LoginResponseDto(string Token, DateTime ExpiresAtUtc, string Role)`.

## Access credential (JWT)

Claims now: `ClaimTypes.NameIdentifier` (user id), `ClaimTypes.Name` (user name), `ClaimTypes.Role` (`user.Role`). Expiry and signing are unchanged from 003.

## MVC (no persistence)

| Item | Change |
|------|--------|
| `LoginResult` (nested in `Services/CategoryProductApiClient.cs`) | `record LoginResult(string Token, DateTime ExpiresAtUtc, string Role)` |
| `ApiResult` (`Models/ApiModels.cs`) | new `bool Forbidden` flag (set for 403); `Unauthorized` is now set only for 401 |
| Auth cookie claims | adds `ClaimTypes.Role` (value from the login result) beside `ClaimTypes.Name` and `access_token` |
| `AccessDeniedPath` | `/Account/AccessDenied` (was `/Account/Login`) |

## Test data (`CategoryProduct.API.Tests/ApiFactory.cs`)

Seeded users: `tester` (`Role = "Admin"`) and `plainuser` (`Role = "User"`), both with a hashed test password.

## Unaffected

`Category`, `Product`, request/response DTOs for Category and Product, `LoginRequestDto`.
