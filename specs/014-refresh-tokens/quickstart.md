# Quickstart: Validating Refresh Tokens

## Prerequisites

- `Jwt:Key` set via user secrets (see `specs/003-jwt-authentication/quickstart.md`).
- Apply the new migration:
  ```
  dotnet ef database update --project CategoryProduct.API
  ```
  (creates `RefreshTokens` via `20261103183150_AddRefreshTokens`).
- A user account (register via `POST /api/account/register` or use an existing one).

## Automated

```
dotnet test
```

`CategoryProduct.API.Tests/RefreshTokenTests.cs`: `Login_ReturnsRefreshToken`,
`Refresh_ReturnsNewPair_AndNewAccessTokenWorks`, `ReusedRefreshToken_Returns401_AndRevokesFamily`,
`UnknownRefreshToken_Returns401`, `Logout_RevokesRefreshToken`.

## Manual (API)

1. `POST /api/account/login` -> note `refreshToken` (call it R1).
2. `POST /api/account/refresh` `{ "refreshToken": "<R1>" }` -> `200`, new `token` and new
   `refreshToken` (R2).
3. Repeat step 2 with R1 -> `401`; then with R2 -> `401` (reuse sweep).
4. Log in again (R3), `POST /api/account/logout` with R3 -> `204`; refresh with R3 -> `401`.
5. `POST /api/account/refresh` `{ "refreshToken": "nope" }` -> `401`.
6. Optional: inspect the `RefreshTokens` table: only `TokenHash` values, never raw tokens.

## Manual (MVC) - not run when documenting

1. Start API and MVC; log in. To observe refresh quickly, set `Jwt:ExpiryMinutes` to a small value.
2. After the access token expires (or is within 60 seconds), perform a write (for example create a
   category) and confirm it succeeds without re-login.
3. Log out and confirm refreshing with the old refresh token returns `401`.
