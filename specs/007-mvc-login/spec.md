# Feature Specification: MVC Login

**Feature Branch**: `007-mvc-login` (retroactive; built directly on `master`)

**Created**: 2026-11-03

**Status**: Implemented (commit `142ace4`)

**Input**: Retroactive spec. MVC users sign in with the API's username/password login; the API JWT is kept in the MVC auth cookie and forwarded as a bearer token on write calls.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Sign in and out of the MVC site (Priority: P1)

A user opens the login page, enters their username and password, and is signed in. A layout control lets them sign out.

**Independent Test**: Log in with a valid account and confirm the layout shows the signed-in state; log out and confirm it clears.

**Acceptance Scenarios**:

1. **Given** valid credentials, **When** the login form is submitted, **Then** the MVC calls the API login, stores the returned JWT in the auth cookie, and redirects to the requested page (or home).
2. **Given** invalid credentials (API returns 401), **When** submitted, **Then** the form is shown again with a generic error.
3. **Given** a signed-in user, **When** they sign out, **Then** the cookie is cleared and write pages require login again.

---

### User Story 2 - Writes require sign-in and carry the token (Priority: P1)

Create, edit, and delete pages in MVC require a signed-in user, and every API write call carries that user's JWT.

**Acceptance Scenarios**:

1. **Given** an anonymous visitor, **When** they open a create/edit/delete page, **Then** they are redirected to `/Account/Login`.
2. **Given** a signed-in user, **When** they submit a write, **Then** the API request includes `Authorization: Bearer <token>` taken from the cookie.
3. **Given** the API replies 401 (expired or invalid token), **When** an MVC write runs, **Then** the user is signed out and redirected to login.
4. **Given** anonymous visitors, **When** they open list/details pages, **Then** those still work (reads are public).

### Edge Cases

- Token expiry (about an hour, see 003) and cookie lifetime are not synchronized; the 401 handling above is what resolves a stale cookie.
- The cookie is HttpOnly, SameSite=Lax, and Secure=Always, so the site must be served over HTTPS.
- The "Create New" link on the Index pages is hidden for anonymous users; Edit/Delete links are still shown and lead to the login page via `[Authorize]` (role-based hiding arrives in 008).
- Only successful login-to-write through the browser is verified manually; no automated MVC tests exist (known gap).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: MVC MUST provide `GET/POST /Account/Login` and a logout action using `LoginViewModel` and `Views/Account/Login.cshtml`.
- **FR-002**: MVC MUST use cookie authentication (`LoginPath=/Account/Login`) and store the API JWT as an `access_token` claim in the cookie.
- **FR-003**: `CategoryProductApiClient` MUST attach the bearer token from the current user's claims to each outgoing request that needs it.
- **FR-004**: MVC write actions MUST be protected by `[Authorize]`.
- **FR-005**: An API 401 during an MVC write MUST sign the user out and redirect to login.
- **FR-006**: Passwords MUST NOT be stored by MVC; only the token is retained in the cookie.

## Success Criteria *(mandatory)*

- **SC-001**: An anonymous user can never complete an MVC write (redirected to login or rejected by the API).
- **SC-002**: A user with valid credentials can sign in and complete a write with no extra manual steps.

## Assumptions

- The API login endpoint from 003 exists and users are pre-provisioned.
- No registration, remember-me, or password reset in MVC.
- The cookie lifetime uses framework defaults.
