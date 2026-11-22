# Quickstart: Validating User Registration & Management

## Prerequisites

- `Jwt:Key` set via user secrets (see `specs/003-jwt-authentication/quickstart.md`).
- Database up to date; no new migration for this feature, but to apply all existing ones:
  ```
  dotnet ef database update --project CategoryProduct.API
  ```
- At least one `Admin` user (migration `AddUserRole` promotes the seeded `testuser`).

## Automated

```
dotnet test
```

Relevant tests (`CategoryProduct.API.Tests/UsersTests.cs`): `Register_Succeeds_And_CanLogin`,
`Register_DuplicateDifferentCase_Returns409`, `Register_ShortPassword_Returns400`,
`Users_NonAdmin_Returns403`, `Users_Admin_ListsWithoutPasswordHash`, `Admin_CanPromoteUser`,
`LastAdminTests.DemotingLastAdmin_Returns409`. `ApiFactory` seeds an admin (`tester`) and a plain
user (`plainuser`) and raises the register rate limit to 1000.

## Manual (API)

1. `POST /api/account/register` `{ "userName": "demo1", "password": "<8+ chars>" }` -> `201`, role
   `User`.
2. Repeat with `DEMO1` -> `409`. Use a 5-character password -> `400`.
3. Log in as admin, `GET /api/users?search=demo` with the bearer token -> `200`.
4. `PUT /api/users/{id}/role` `{ "role": "Admin" }` -> `200`; `{ "role": "Root" }` -> `400`.
5. As the only admin, `PUT` your own role to `User` -> `409`.
6. More than 5 register calls within a minute from one IP -> `429`.

## Manual (MVC) - not run when documenting

1. Start API and MVC; open `/Account/Register`, register, confirm redirect to Categories while
   signed in.
2. As admin, open `/Users`, search, change a role, confirm the success/error alerts.
