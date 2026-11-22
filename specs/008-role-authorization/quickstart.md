# Quickstart: Validating Role-Based Authorization

## Prerequisites

- JWT key set once, inside `CategoryProduct.API/` (never commit it):
  ```
  dotnet user-secrets set "Jwt:Key" "<random string of at least 32 characters>"
  ```
- Apply the migration (adds `Users.Role`, default `User`; promotes `testuser` to `Admin`). Without it, login fails because the `Role` column is missing:
  ```
  dotnet ef database update --project CategoryProduct.API
  ```
- Two accounts: an Admin (the seeded `testuser`, if it exists in your database) and a non-Admin (any other user, whose `Role` is `User` by default). There is no UI at this commit to change roles; a role is changed directly in the `Users` table and applies at the user's next login.
- Run the API (`dotnet run --project CategoryProduct.API`) and, for MVC checks, the site (`dotnet run --project CategoryProduct.MVC`) over HTTPS.

## Automated

```
dotnet test CategoryProduct.API.Tests
```

`PostCategory_NonAdminToken_Returns403` covers SC-001; the other five tests must still pass.

## API scenarios (Swagger UI or any HTTP client)

See `contracts/api-role-authorization.md`.

1. **Admin login (US1.4)**: `POST /api/Account/login` as the Admin -> `200` with `token`, `expiresAtUtc`, `role: "Admin"`.
2. **Admin write (US1.1)**: `POST /api/Category` with the Admin bearer token -> `200 OK`.
3. **Non-Admin login**: login as the plain user -> `200` with `role: "User"`.
4. **Non-Admin write (US1.2)**: same `POST /api/Category` with that token -> `403 Forbidden`; `GET /api/Category` shows nothing was created. Repeat for `PUT`/`DELETE` and for `/api/Product`.
5. **No token (US1.3)**: `POST /api/Category` without a header -> `401 Unauthorized`.
6. **Reads (US1.5)**: all `GET` endpoints succeed with no header, and with either token.

## MVC scenarios

See `contracts/mvc-role-routes.md`.

1. **Admin (US2.1)**: sign in as the Admin -> Index pages show "Create New", Edit and Delete; Details pages show Edit; create/edit/delete succeed.
2. **Non-Admin (US2.2, US2.3)**: sign in as the plain user -> write links are hidden; open `/Categories/Create` directly -> `/Account/AccessDenied`.
3. **Anonymous (US2.2, US2.3)**: signed out -> write links hidden; `/Categories/Create` redirects to `/Account/Login`.
4. **API 403 (US2.4)**: not reproducible through normal sign-in, because the cookie role and the token role both come from the same login (a demoted user keeps an Admin token until it expires). Verify by code review: `ApiResult.Forbidden` is set for 403 and both MVC controllers add "You don't have permission to make changes." without signing out.
5. **API 401 (US2.5)**: let the token expire (set `Jwt:ExpiryMinutes` to `1`), submit a write -> signed out and sent to login.
6. **Old cookie edge case**: a cookie issued before this deployment has no role claim -> write URLs go to Access Denied until the user signs in again.

## Success criteria mapping

- SC-001 -> API scenario 4 and `PostCategory_NonAdminToken_Returns403`.
- SC-002 -> API scenario 2 and MVC scenario 1.
