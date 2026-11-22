# Feature Specification: Refresh Tokens

**Feature Branch**: `014-refresh-tokens`

**Created**: 2026-11-22 (retroactive; documents delivered work)

**Status**: Implemented (commit `277a46b`, 2026-11-03)

**Input**: "Keep users signed in beyond the short-lived access token: issue refresh tokens at login,
rotate them on use, detect reuse, and let the MVC site refresh transparently." This extends
`003-jwt-authentication`, which had no logout/revocation and a single access token.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Obtain a refresh token at login (Priority: P1)

A client that logs in receives, besides the access token, a long-lived refresh token and its expiry.

**Independent Test**: `POST /api/account/login`; the response contains a non-empty `refreshToken`
and `refreshTokenExpiresAtUtc` about 7 days ahead (`Login_ReturnsRefreshToken`).

**Acceptance Scenarios**:

1. **Given** valid credentials, **When** logging in, **Then** the response includes `token`,
   `expiresAtUtc`, `role`, `refreshToken` and `refreshTokenExpiresAtUtc`.
2. **Given** a refresh token is issued, **Then** only its SHA-256 hash is stored server side.

---

### User Story 2 - Exchange a refresh token for a new pair (rotation) (Priority: P1)

A client whose access token has expired (or is about to) swaps its refresh token for a fresh access
token and a new refresh token; the old refresh token stops working.

**Independent Test**: Log in, call `POST /api/account/refresh` with the refresh token, expect `200`
with a different refresh token, and a working access token on a protected write endpoint
(`Refresh_ReturnsNewPair_AndNewAccessTokenWorks`).

**Acceptance Scenarios**:

1. **Given** a valid, unrevoked, unexpired refresh token, **When** refreshed, **Then** `200 OK`
   with a new access token and a new, different refresh token; the old token is marked revoked and
   linked to its replacement.
2. **Given** an unknown token, **Then** `401 Unauthorized`.
3. **Given** an expired refresh token, **Then** `401 Unauthorized`.
4. **Given** the owning user no longer exists, **Then** `401 Unauthorized`.
5. **Given** the user's role changed since the last token, **Then** the new access token carries
   the user's current role (the user row is re-read on refresh).

---

### User Story 3 - Reuse detection (Priority: P1)

If an already-revoked refresh token is presented again (possible theft or replay), the server
rejects it and revokes all of that user's still-active refresh tokens.

**Independent Test**: Log in, refresh once (getting new token N), replay the original token ->
`401`, then try N -> `401` (`ReusedRefreshToken_Returns401_AndRevokesFamily`).

**Acceptance Scenarios**:

1. **Given** a revoked refresh token, **When** it is presented to refresh, **Then** `401` and every
   unrevoked refresh token of the same user is revoked.

---

### User Story 4 - Logout revokes the refresh token (Priority: P2)

A client can end a session by revoking its refresh token.

**Independent Test**: `POST /api/account/logout` with the token -> `204`; refreshing with it
afterwards -> `401` (`Logout_RevokesRefreshToken`).

**Acceptance Scenarios**:

1. **Given** an active refresh token, **When** logout is called, **Then** `204 No Content` and the
   token is revoked.
2. **Given** an unknown or already revoked token (or empty), **Then** still `204` (idempotent,
   reveals nothing).

---

### User Story 5 - MVC refreshes the session transparently (Priority: P2)

The MVC site stores both tokens in the auth cookie, refreshes the access token shortly before it
expires or after a `401` from the API, and retries the original request once; logging out also
revokes the refresh token.

**Independent Test**: Log in via MVC, wait until the access token (60 min default) is near expiry or
shorten `Jwt:ExpiryMinutes`, perform a write, and confirm it still succeeds without a new login (see
Known Gaps: not run).

**Acceptance Scenarios**:

1. **Given** an authenticated MVC request whose access token expires within 60 seconds, **Then**
   the handler refreshes first, updates the cookie, and sends the request with the new token.
2. **Given** the API answers `401` to an authenticated request, **Then** the handler refreshes once
   per HTTP request and retries once with the new token; if refresh fails the original `401` is
   returned.
3. **Given** MVC logout, **Then** the API logout is called best-effort (failures ignored) and the
   cookie is cleared.
4. **Given** sign-in via login or register, **Then** the cookie lifetime follows the refresh token
   expiry and is non-persistent (session cookie).

### Edge Cases

- Reuse detection covers any revoked token, including one revoked by logout or rotation; replaying
  it revokes all of the user's active refresh tokens.
- An expired but not revoked token returns `401` without revoking others.
- Access tokens remain valid until their own expiry after logout or revocation (stateless JWT).
- Refresh is rate limited per client IP; logout is not rate limited.
- Concurrent refreshes with the same token are not serialized; one may succeed and the other be
  treated as reuse.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Login and refresh responses MUST be `LoginResponseDto(Token, ExpiresAtUtc, Role,
  RefreshToken, RefreshTokenExpiresAtUtc)`.
- **FR-002**: Refresh tokens MUST be 64 cryptographically random bytes, Base64Url encoded; only the
  uppercase-hex SHA-256 hash MUST be persisted; the raw value is returned only once to the client.
- **FR-003**: Refresh token lifetime MUST come from `Jwt:RefreshTokenDays` (default `7`).
- **FR-004**: `POST /api/account/refresh` MUST rotate: create a new token, set `RevokedAtUtc` and
  `ReplacedByTokenHash` on the old one, and return a new access token (lifetime `Jwt:ExpiryMinutes`,
  default 60) for the current user row.
- **FR-005**: Presenting a revoked token MUST return `401` and revoke all unrevoked refresh tokens
  of that user.
- **FR-006**: Unknown token, expired token, or missing user MUST return `401`.
- **FR-007**: `POST /api/account/logout` MUST revoke the token if found and unrevoked and always
  return `204`.
- **FR-008**: Refresh MUST be rate limited by policy `refresh`: 1-minute fixed window per client IP,
  no queue, limit `RateLimiting:RefreshPermitLimit` (default `30`), rejected with `429`.
- **FR-009**: Token creation MUST be centralized in `TokenService` (access and refresh tokens),
  registered scoped.
- **FR-010**: MVC MUST store `access_token`, `refresh_token` and `access_token_expires_at` as cookie
  claims through a shared `AuthSessionService`, and `TokenRefreshHandler` MUST refresh proactively
  (access token expiring within 60 seconds) and reactively (after `401`, once per request), only for
  authenticated requests carrying a bearer header.
- **FR-011**: A new migration `AddRefreshTokens` MUST create the `RefreshTokens` table (see
  data-model.md).
- **FR-012**: Automated tests MUST cover login token issuance, rotation, reuse, unknown token and
  logout.

### Key Entities

- **RefreshToken**: server-side record of an issued refresh token (hash, owner, created, expiry,
  revoked, replaced-by). See data-model.md.

## Success Criteria *(mandatory)*

- **SC-001**: A refresh token can be used at most once successfully (`ReusedRefreshToken_...`).
- **SC-002**: No raw refresh token is stored in the database; only hashes.
- **SC-003**: After logout, the revoked refresh token cannot obtain new tokens.
- **SC-004**: An MVC session survives access token expiry without a new login while the refresh
  token is valid (not verified in a browser; see Known Gaps).

## Assumptions

- Defaults: access token 60 minutes (`Jwt:ExpiryMinutes`), refresh token 7 days
  (`Jwt:RefreshTokenDays`), refresh limit 30/min/IP (`RateLimiting:RefreshPermitLimit`).
- `Jwt:RefreshTokenDays` is not present in the committed `appsettings.json`; the code default is
  used unless overridden.
- Existing login behavior (rate limit, generic `401`) from `003-jwt-authentication` is unchanged.

## Known Gaps

- MVC `TokenRefreshHandler`, `AuthSessionService` and logout have no automated tests and were not
  run in a browser when this spec was written.
- Expired-token and deleted-user refresh paths, `429` on refresh, and the reuse-after-logout case
  have no dedicated tests. (The reuse-after-logout case and `429` on logout are now covered by
  `015-auth-hardening` tests.)
- Reuse detection is per user (all active tokens), not per token chain; a user logged in on several
  devices loses all sessions when any revoked token is replayed. (Closed by `015-auth-hardening`:
  revocation is now scoped to the token family.)
- Concurrent refresh requests are not serialized; there is no grace window for racing clients.
  (Partly closed by `015-auth-hardening`: a concurrency token makes the loser get `401`; there is still
  no grace window.)
- Expired/revoked refresh token rows are never purged. (Closed by `015-auth-hardening`: periodic purge.)
- The logout endpoint is unauthenticated and not rate limited. (Rate limit closed by `015-auth-hardening`;
  it stays unauthenticated.)
- Missing `refreshToken` in the request body is not specifically handled in code (the value is
  coalesced to an empty string before hashing); the resulting HTTP status was not verified.
