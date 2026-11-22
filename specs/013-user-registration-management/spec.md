# Feature Specification: User Registration & Management

**Feature Branch**: `013-user-registration-management`

**Created**: 2026-11-22 (retroactive; documents delivered work)

**Status**: Implemented (commit `025d652`, 2026-11-03)

**Input**: "Let people self-register an account, and let an admin list users and change their role,
in both CategoryProduct.API and CategoryProduct.MVC." This supersedes the assumption in
`003-jwt-authentication` that accounts are pre-provisioned only.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Self-service registration (Priority: P1)

A visitor creates an account with a username and password, receiving a regular `User` account that
can then log in.

**Why this priority**: Without it, accounts can only be created by hand in the database.

**Independent Test**: `POST /api/account/register` with a new username and a valid password, expect
`201` with role `User`, then log in with the same credentials and expect `200`.

**Acceptance Scenarios**:

1. **Given** an unused username and a password of at least 8 characters, **When** the visitor
   registers, **Then** the response is `201 Created` with `id`, `userName`, `role = "User"` and no
   password hash.
2. **Given** a newly registered account, **When** the user logs in, **Then** login succeeds.
3. **Given** a username already taken (compared case-insensitively), **When** registering,
   **Then** the response is `409 Conflict`.
4. **Given** a password shorter than 8 characters (or an invalid username), **When** registering,
   **Then** the response is `400 Bad Request` with validation details.
5. **Given** more registration attempts from one client IP than the limit within a minute,
   **Then** further attempts are rejected with `429 Too Many Requests`.

---

### User Story 2 - Admin lists users (Priority: P2)

An administrator views a paged, searchable list of accounts (never including password hashes).

**Independent Test**: As admin `GET /api/users` -> `200` with a paged body; as a non-admin -> `403`.

**Acceptance Scenarios**:

1. **Given** an admin token, **When** `GET /api/users` is called, **Then** a paged list (`items`,
   `page`, `pageSize`, `totalCount`, `totalPages`) of `id`, `userName`, `role` is returned, ordered
   by user name, with no `passwordHash`.
2. **Given** a `search` term, **Then** only users whose name contains it (case-insensitive) are
   returned.
3. **Given** a non-admin token, **Then** the response is `403 Forbidden`; with no token, `401`.

---

### User Story 3 - Admin changes a user's role, protecting the last admin (Priority: P2)

An administrator promotes a user to `Admin` or demotes an admin to `User`, but the system always
keeps at least one admin.

**Independent Test**: As admin `PUT /api/users/{id}/role` with `{"role":"Admin"}` -> `200`; with
`{"role":"Root"}` -> `400`; demote the only admin -> `409`.

**Acceptance Scenarios**:

1. **Given** an admin token and an existing user, **When** the role is set to `Admin` or `User`,
   **Then** `200 OK` with the updated user.
2. **Given** any other role value (or none), **Then** `400 Bad Request` with the message
   `Role must be 'User' or 'Admin'.` under `Role`.
3. **Given** an unknown user id, **Then** `404 Not Found`.
4. **Given** the target is the only admin and the new role is not `Admin`, **Then** `409 Conflict`
   with detail `Cannot remove the last admin.` and the role is unchanged.

---

### User Story 4 - Register and manage users in the MVC site (Priority: P3)

A visitor registers from a Register page and is signed in immediately; an admin sees a Users page
to search users and change roles.

**Independent Test**: In the running MVC site, register a new user (lands on Categories, signed
in); as admin open Users and change a role (see Known Gaps: not exercised in a browser).

**Acceptance Scenarios**:

1. **Given** the Register form with matching passwords, **When** submitted successfully, **Then**
   the user is logged in via the API and redirected to the Categories index.
2. **Given** a taken username, **Then** the form shows `Username already taken.` on the user-name
   field; on `429` it shows `Too many attempts. Try again in a minute.`
3. **Given** an admin, **Then** a `Users` nav link and `/Users` page with search, paging and a
   role dropdown per row are available; non-admins do not see the link.
4. **Given** a role change that fails (409/400/404), **Then** a danger alert shows the API message
   (or `User not found.` / `Could not change role.`); success shows `Role updated.`

### Edge Cases

- Usernames are trimmed before checks; allowed characters are letters, digits, `.`, `_`, `-`,
  length 3-50.
- A concurrent duplicate that slips past the pre-check hits the unique index; the
  `DbUpdateException` is caught and also returned as `409 Conflict`.
- Self-demotion of an admin is allowed as long as another admin exists.
- Role claims are embedded in the access token at issuance, so a role change takes effect for a
  user on their next login/token issuance.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: `POST /api/account/register` MUST create a user with role `User`, hashing the
  password with `PasswordHasher<User>`; it MUST never return or log the hash or password.
- **FR-002**: Register MUST validate: `UserName` required, 3-50 chars, regex
  `^[A-Za-z0-9._-]+$`; `Password` required, minimum 8 chars. Invalid input MUST return `400`.
- **FR-003**: Register MUST return `409 Conflict` (ProblemDetails, detail `Username already taken.`)
  for a duplicate username compared case-insensitively, including when the database unique index
  rejects a concurrent insert.
- **FR-004**: Register MUST be rate limited by policy `register`: fixed window of 1 minute per
  client IP, no queue, limit from `RateLimiting:RegisterPermitLimit` (default `5`), rejected with
  `429`.
- **FR-005**: `GET /api/users` MUST require role `Admin`, support `page` (min 1, default 1),
  `pageSize` (clamped 1-50, default 10) and `search`, and return users ordered by `UserName`
  without password hashes.
- **FR-006**: `PUT /api/users/{id}/role` MUST require role `Admin`, accept only `User` or `Admin`,
  return `404` for unknown ids, and return `409` when it would leave the system with no admin.
- **FR-007**: The whole `UsersController` MUST be `[Authorize(Roles = "Admin")]`.
- **FR-008**: MVC MUST provide `GET/POST /Account/Register` (with confirm-password) and an
  admin-only `/Users` list with a `ChangeRole` POST, consuming only the API over HTTP.
- **FR-009**: Automated API tests MUST cover register success/duplicate/short password, non-admin
  `403`, admin list without hash, promotion + invalid role, and last-admin demotion.

### Key Entities

- **User**: `Id`, `UserName`, `PasswordHash`, `Role` (`User` or `Admin`). See data-model.md.

## Success Criteria *(mandatory)*

- **SC-001**: A visitor can register and log in with the new account using only the API.
- **SC-002**: Non-admins receive `403` on every `api/users` endpoint (`Users_NonAdmin_Returns403`).
- **SC-003**: The last remaining admin cannot be demoted (`DemotingLastAdmin_Returns409`).
- **SC-004**: No API response from these endpoints contains `passwordHash`.

## Assumptions

- Roles are the two fixed strings `User` and `Admin` stored on the user row (column added earlier
  by migration `20261103173834_AddUserRole`).
- New accounts always start as `User`; there is no way to self-select a role.
- The first admin is created outside this feature (the earlier migration promotes the seeded
  `testuser`; test fixtures seed their own admin).
- Case-insensitive username uniqueness at the database level relies on the SQL Server column
  collation; the application-level check is case-insensitive regardless.

## Known Gaps

- COVERED by `021-test-coverage` (pending the first CI run). Original gap: the MVC Register and Users pages have no automated tests and were not run in a browser when this
  spec was written. (Server-rendered behavior is now covered by `AccountTests` and `UsersTests`; a real browser is still not used.)
- No migration was added by this commit: the unique index on `UserName` already exists
  (`IX_Users_UserName`, migration `20260820083821_UpdateUsersTableForAuth`).
- No account deletion, password change/reset, or email verification.
- `PUT api/users/{id}/role` and `GET api/users` return `401`/`403` through framework defaults;
  only `403` for non-admin is covered by a test. COVERED by `021-test-coverage` (pending the first CI run). Original gap: `GET api/users` without a token returning `401` (`RefreshEdgeCaseTests`).
- New Known Gap found by `021-test-coverage`: the MVC `UsersController` does not handle an API `401` (no session-expired redirect to login); see the 021 Known Gaps.
