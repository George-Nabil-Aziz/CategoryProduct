# Feature Specification: Role-Based Authorization

**Feature Branch**: `008-role-authorization` (retroactive; built directly on `master`)

**Created**: 2026-11-03

**Status**: Implemented (commit `f868aa7`)

**Input**: Retroactive spec. Users get a role; only Admins can create, update, or delete Categories and Products, in both the API and the MVC site.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Only Admins can write through the API (Priority: P1)

A signed-in user without the Admin role is refused when trying to create, update, or delete data; an Admin succeeds.

**Why this priority**: 003 gave every authenticated user write access (single access level); this feature separates ordinary users from Admins.

**Independent Test**: Log in as an Admin and as a non-Admin and call a write endpoint with each token.

**Acceptance Scenarios**:

1. **Given** an Admin token, **When** a Category/Product POST/PUT/DELETE is called, **Then** it proceeds as before.
2. **Given** a valid non-Admin token, **When** the same is called, **Then** the API returns 403 and nothing changes.
3. **Given** no/invalid token, **Then** 401, unchanged from 003.
4. **Given** a successful login, **Then** the response includes the user's `role` and the token carries a role claim.
5. **Given** anyone, **When** a GET is called, **Then** it stays public.

---

### User Story 2 - MVC reflects the role (Priority: P1)

The MVC site only offers write actions to Admins and shows an Access Denied page to others.

**Why this priority**: Without it, non-Admins would see write links that can only fail.

**Independent Test**: Sign in to MVC as an Admin and as a non-Admin and compare the Index/Details pages and the write URLs.

**Acceptance Scenarios**:

1. **Given** an Admin signed in to MVC, **When** viewing Index/Details pages, **Then** Create/Edit/Delete links are visible (Create and Delete on Index only; Edit also on Details).
2. **Given** a non-Admin or anonymous user, **When** viewing those pages, **Then** write links are hidden.
3. **Given** a signed-in non-Admin who opens a write URL directly, **Then** they land on `/Account/AccessDenied`; an anonymous visitor is sent to `/Account/Login` as in 007.
4. **Given** the API replies 403 to an MVC write (a defensive path; the cookie and token roles normally agree), **Then** the form is shown again with the error "You don't have permission to make changes." and the user stays signed in.
5. **Given** the API replies 401, **Then** the user is signed out and redirected to login (as in 007).

### Edge Cases

- Cookies issued before this change carry no role claim; those users are treated as non-Admin (Access Denied on write URLs) and must sign in again to get write access.
- Existing users default to the `User` role; the seeded `testuser` is set to `Admin` by the migration.
- A role changed in the database only takes effect for new tokens/cookies (next login).
- Role names are matched as the string `Admin`; `User` is the only other value in use.
- The role is limited to 20 characters and is required.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: `User` MUST have a required `Role` string (max 20 characters, default `User`), and `testuser` is promoted to `Admin` by migration `AddUserRole`.
- **FR-002**: API login MUST add `ClaimTypes.Role` to the JWT and return `Role` in `LoginResponseDto`.
- **FR-003**: API Category and Product write actions MUST use `[Authorize(Roles = "Admin")]`.
- **FR-004**: MVC MUST store the role as a claim in the auth cookie and protect write actions with `[Authorize(Roles = "Admin")]`.
- **FR-005**: MVC `AccessDeniedPath` MUST be `/Account/AccessDenied`; a signed-in non-Admin hitting a protected MVC action is redirected there by the cookie middleware.
- **FR-006**: MVC views MUST hide write links from non-Admins.
- **FR-007**: The role claim in API tokens MUST NOT affect read access.
- **FR-008**: An API 403 on an MVC write MUST be distinguishable from 401 (`ApiResult.Forbidden`) and MUST show a permission error on the form without signing the user out.

## Success Criteria *(mandatory)*

- **SC-001**: 100% of write requests with a non-Admin token return 403 and change no data (automated: `PostCategory_NonAdminToken_Returns403`).
- **SC-002**: An Admin can still log in and perform every write through both API and MVC.

## Assumptions

- This replaces 003's "single access level" assumption.
- Deployment step required: apply the migration with `dotnet ef database update --project CategoryProduct.API`; without it, login fails because the `Role` column is missing.
- Only the API 403 path has an automated test; the MVC role flow is verified manually (known gap).
- No UI for assigning roles at this commit; roles are managed in the database.

## Later changes

`025d652` added registration and an admin users API/page for changing roles; `277a46b` added refresh tokens (the login response gained fields); `d643b40` made category deletes with products return 409. See the newer specs 009-014.
