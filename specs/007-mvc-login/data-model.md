# Data Model: MVC Login

**No schema change.** No API model, DTO or migration changed; MVC has no database. The types below are MVC-side.

## LoginViewModel (`CategoryProduct.MVC/Models/LoginViewModel.cs`, new)

| Field | Type | Rule(s) |
|-------|------|---------|
| `UserName` | `string` | `[Required]`, display name "User name" |
| `Password` | `string` | `[Required]`, `[DataType(DataType.Password)]` |

## LoginResult (nested in `Services/CategoryProductApiClient.cs`, new)

`public record LoginResult(string Token, DateTime ExpiresAtUtc)` mirrors the API's `LoginResponseDto` at this commit (008 adds `Role`).

## Authentication cookie (not a persisted entity)

| Setting | Value |
|---------|-------|
| Scheme | `CookieAuthenticationDefaults.AuthenticationScheme` |
| `LoginPath` | `/Account/Login` |
| `LogoutPath` | `/Account/Logout` |
| `AccessDeniedPath` | `/Account/Login` (changed in 008) |
| `Cookie.HttpOnly` | `true` |
| `Cookie.SecurePolicy` | `Always` |
| `Cookie.SameSite` | `Lax` |
| `IsPersistent` | `false` |
| `ExpiresUtc` | the token's `ExpiresAtUtc` |

Claims in the cookie principal:

| Claim | Value |
|-------|-------|
| `ClaimTypes.Name` | The user name typed into the form |
| `access_token` | The API JWT |

The password is never stored or placed in a claim.

## Unaffected

`Category`, `Product`, `ApiModels` (`PagedResult<T>`, `CategoryRequest`, `ProductRequest`, `ApiResult`), and the API's `User`/`LoginRequestDto`.
