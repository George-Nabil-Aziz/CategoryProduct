# Data Model: User Registration & Management

**No schema change in this feature; no migration was added.** All required columns and the unique
index already existed.

## User (EF Core model, unchanged by this commit)

`CategoryProduct.API/Models/User.cs`

| Field | Type | Rule(s) | Migration |
|-------|------|---------|-----------|
| `Id` | `int` | Primary key, identity | earlier migrations |
| `UserName` | `string` | Required, `nvarchar(450)`, unique index `IX_Users_UserName` | `20260820083821_UpdateUsersTableForAuth` |
| `PasswordHash` | `string` | Required; output of `PasswordHasher<User>.HashPassword` | `20260820083821_UpdateUsersTableForAuth` (rename from `Password`) |
| `Role` | `string` | Required, `nvarchar(20)`, default `"User"`; valid values `User`, `Admin` (enforced in the API, not by a DB constraint) | `20261103173834_AddUserRole` |

**Relationships**: none from `User` in this feature (a `RefreshToken` relationship arrives in
`014-refresh-tokens`).

## DTOs (all under `CategoryProduct.API/Dtos/`)

`RegisterRequestDto` (new)

| Field | Type | Rules |
|-------|------|-------|
| `UserName` | `string` | Required, non-empty, length 3-50, regex `^[A-Za-z0-9._-]+$` |
| `Password` | `string` | Required, non-empty, `MinLength(8)` |

`UserResponseDto(int Id, string UserName, string Role)` (new) - never includes the hash.

`UpdateRoleRequestDto(string? Role)` (new) - validated manually in the controller.

`PagedResponseDto<T>(Items, Page, PageSize, TotalCount, TotalPages)` (existing) - reused for the
user list.

## MVC models (`CategoryProduct.MVC/Models/`)

- `RegisterViewModel` (new): `UserName` (same rules as the API), `Password` (min 8),
  `ConfirmPassword` (`[Compare]` with `Password`).
- `UserItem(int Id, string UserName, string Role)` (new, in `RegisterViewModel.cs`).
- `ApiResult` (existing, in `ApiModels.cs`) gained `TooManyRequests`.

## Configuration

| Key | Default | Meaning |
|-----|---------|---------|
| `RateLimiting:RegisterPermitLimit` | `5` | Register requests per client IP per 1-minute window |
