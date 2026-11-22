# Feature Specification: MVC Login

**Feature Branch**: `007-mvc-login` (retroactive; built directly on `master`)

**Created**: 2026-11-03

**Status**: Implemented (commit `142ace4`)

**Input**: Retroactive spec. MVC users sign in with the API's username/password login; the API JWT is kept in the MVC auth cookie and forwarded as a bearer token on write calls.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Sign in and out of the MVC site (Priority: P1)

A user opens the login page, enters their username and password, and is signed in. A layout control lets them sign out.

**Why this priority**: Without sign-in, MVC cannot perform any write now that the API requires a token (003, 006).

**Independent Test**: Log in with a valid account and confirm the layout shows "Hello, <user name>" and a Logout button; log out and confirm it shows the Login link again.

**Acceptance Scenarios**:

1. **Given** valid credentials, **When** the login form is submitted, **Then** MVC calls the API login, stores the returned JWT in the auth cookie, and redirects to the requested local `returnUrl`, or to Categories Index when there is none.
2. **Given** invalid credentials (API returns 401), **When** submitted, **Then** the form is shown again with the generic error "Invalid username or password."
3. **Given** a signed-in user, **When** they click Logout (a POST with an anti-forgery token), **Then** the cookie is cleared, they are redirected to Categories Index, and write pages require login again.
4. **Given** a blank user name or password, **When** submitted, **Then** the form is shown again with field errors and the API is not called.

---

### User Story 2 - Writes require sign-in and carry the token (Priority: P1)

Create, edit, and delete pages in MVC require a signed-in user, and every API write call carries that user's JWT.

**Why this priority**: Closes the gap left by 006, where MVC writes were rejected by the API for lack of a token.

**Independent Test**: Anonymously open `/Categories/Create` and confirm the redirect to login; sign in and create a category.

**Acceptance Scenarios**:

1. **Given** an anonymous visitor, **When** they open a create/edit/delete page (GET or POST), **Then** they are redirected to `/Account/Login` (with a return URL).
2. **Given** a signed-in user, **When** they submit a write, **Then** the API request includes `Authorization: Bearer <token>` taken from the cookie's `access_token` claim.
3. **Given** the API replies 401 (expired or invalid token), **When** an MVC write runs, **Then** the user is signed out and redirected to login with the current path and query string as `returnUrl`.
4. **Given** anonymous visitors, **When** they open list/details pages, **Then** those still work (reads are public and send no token).

### Edge Cases

- The auth ticket's expiry is set to the API token's `ExpiresAtUtc` (`IsPersistent = false`), so the cookie and token expire together; the 401 handling above is the safety net if the API still rejects the token.
- The cookie is HttpOnly, SameSite=Lax, and Secure=Always, so the site must be served over HTTPS.
- The "Create New" link on the Index pages is hidden for anonymous users; Edit/Delete links are still shown and lead to the login page via `[Authorize]` (role-based hiding arrives in 008).
- At this commit `AccessDeniedPath` is `/Account/Login`; 008 changes it to a dedicated page.
- If the API login call fails with a status other than 200/401 (for example 400 or 5xx) or the API is unreachable, MVC throws; no friendly handling yet.
- Only successful login-to-write through the browser is verified manually; no automated MVC tests exist (known gap).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: MVC MUST provide `GET/POST /Account/Login` and a `POST /Account/Logout` action using `LoginViewModel` and `Views/Account/Login.cshtml`.
- **FR-002**: MVC MUST use cookie authentication (`LoginPath=/Account/Login`) and store the API JWT as an `access_token` claim in the cookie.
- **FR-003**: `CategoryProductApiClient` MUST attach the bearer token from the current user's claims to each outgoing write request, on that request only (not on shared default headers).
- **FR-004**: MVC create/edit/delete actions (GET and POST) MUST be protected by `[Authorize]`.
- **FR-005**: An API 401 during an MVC write MUST sign the user out and redirect to login.
- **FR-006**: Passwords MUST NOT be stored by MVC; only the token is retained in the cookie.
- **FR-007**: The layout MUST show a Login link to anonymous users and a greeting plus Logout button to signed-in users.

## Success Criteria *(mandatory)*

- **SC-001**: An anonymous user can never complete an MVC write (redirected to login or rejected by the API).
- **SC-002**: A user with valid credentials can sign in and complete a write with no extra manual steps.

## Assumptions

- The API login endpoint from 003 exists and users are pre-provisioned.
- No registration, remember-me, or password reset in MVC at this commit.
- Cookie `Name`, sliding expiration and other cookie options use framework defaults; only the options listed in this spec are set.

## Later changes

`f868aa7` (008) adds the role claim, Admin-only writes and `/Account/AccessDenied`; `025d652` added MVC registration; `89d69df` handles the login rate limit; `277a46b` added refresh-token handling in the MVC client. See the newer specs 009-014.
