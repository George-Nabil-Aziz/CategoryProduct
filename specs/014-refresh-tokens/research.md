# Research: Refresh Tokens

## Decision 1: Opaque random refresh tokens stored hashed

**Decision**: `TokenService.CreateRefreshToken` generates 64 random bytes
(`RandomNumberGenerator.GetBytes(64)`), Base64Url encodes them as the raw token, and persists
`Hash(raw)` = uppercase hex SHA-256 (64 chars; column `nvarchar(128)`).

**Rationale**: High-entropy random values do not need a slow password hash; SHA-256 makes a database
leak useless for presenting tokens (Constitution Principle IV). Lookup by hash is a unique-index
seek.

**Alternatives considered**: A second JWT as refresh token was rejected (cannot be revoked
without storage anyway; opaque tokens are simpler). Storing raw tokens was rejected (secret
hygiene).

## Decision 2: Rotation on every refresh

**Decision**: `IssueAsync(user, replacing)` adds the new token, sets `RevokedAtUtc` and
`ReplacedByTokenHash` on the old token and saves once.

**Rationale**: Limits the usefulness of a stolen token and enables reuse detection.

## Decision 3: Reuse detection revokes all of the user's active tokens

**Decision**: If the presented token is already revoked, revoke every token for that `UserId` with
`RevokedAtUtc == null` and return `401`.

**Rationale**: Simplest safe response without tracking token chains (`ReplacedByTokenHash` is
recorded, but the revocation sweep does not walk it).

**Trade-off**: Multi-device users are signed out together; replaying a logged-out token also
triggers the sweep.

## Decision 4: Introduce `TokenService`

**Decision**: Move access-token creation out of `AccountController` into `Services/TokenService`
(scoped) and add refresh-token creation there.

**Rationale**: Login and refresh both need identical token issuance - a second concrete consumer,
which is the Principle III trigger for an abstraction.

## Decision 5: Configuration and defaults

`Jwt:ExpiryMinutes` default 60, `Jwt:RefreshTokenDays` default 7,
`RateLimiting:RefreshPermitLimit` default 30; the refresh policy mirrors `login`/`register`
(fixed window 1 minute, per IP, `QueueLimit = 0`, global `429`). Tests set the refresh limit to 1000
in `ApiFactory`.

## Decision 6: Logout is idempotent and anonymous

**Decision**: `POST /api/account/logout` always returns `204`, revoking when the token exists and is
unrevoked.

**Rationale**: The refresh token itself is the credential; responses must not reveal token
validity. Access tokens cannot be revoked (stateless) and expire naturally.

## Decision 7: MVC transparent refresh via a DelegatingHandler

**Decision**: `TokenRefreshHandler` is added to the API `HttpClient` pipeline after `ApiErrorHandler`.
It acts only if a bearer header is attached and the user is authenticated; it buffers request content,
refreshes proactively when the access token expires within 60 seconds, and on `401` refreshes once per
HTTP request (`HttpContext.Items["refresh_tried"]`) and retries a cloned request. The refresh call uses
a separate named client `api-refresh` (base address from `Api:BaseUrl`) to avoid recursion.
`AuthSessionService` (singleton) re-signs the cookie with new claims and sets `http.User` so the rest
of the request sees them.

**Rationale**: Keeps refresh logic out of controllers and `CategoryProductApiClient` methods.

**Alternatives considered**: Cookie-authentication `OnValidatePrincipal` was not used; the handler
approach also handles reactive `401`s.

## Decision 8: Cookie lifetime follows the refresh token

**Decision**: Non-persistent cookie with `ExpiresUtc` = refresh token expiry (previously the access
token expiry).
