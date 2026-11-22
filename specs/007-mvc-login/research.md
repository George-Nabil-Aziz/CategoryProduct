# Research: MVC Login

## Decision 1: Store the API JWT as a claim in the MVC auth cookie

**Decision**: After a successful `POST api/account/login`, MVC signs in with `CookieAuthenticationDefaults.AuthenticationScheme` using a `ClaimsIdentity` with `ClaimTypes.Name` (the entered user name) and a custom `access_token` claim holding the JWT. The ticket's `ExpiresUtc` is the token's `ExpiresAtUtc` and `IsPersistent = false`.

**Rationale**:
- No server-side session store or database is needed (Principle III); the cookie is the session.
- Cookie and token expire at the same moment, so a user is not left signed in with a dead token.
- The cookie is HttpOnly, SameSite=Lax and Secure=Always, so scripts cannot read the token and it is only sent over HTTPS.

**Alternatives considered**:
- **ASP.NET `Session` or distributed cache**: extra infrastructure for a single host. Rejected.
- **Store the token in `localStorage` and call the API from JavaScript**: exposes the token to XSS and bypasses the MVC server. Rejected.
- **Pass username/password to the API on every request**: violates the constitution's Principle IV (credentials transmitted beyond the initial submission). Rejected.

## Decision 2: Attach the bearer token per request

**Decision**: `CategoryProductApiClient` receives `IHttpContextAccessor` and `ApplyBearerToken(HttpRequestMessage request, string? token = null)` reads `access_token` from `HttpContext.User` and sets `request.Headers.Authorization` on that single `HttpRequestMessage`. It is called from `SendAsync` (writes) only.

**Rationale**: The typed client's `HttpClient` is shared; setting `DefaultRequestHeaders.Authorization` (what 006 did) could leak one user's token into another user's request. Reads are public so they carry no token.

**Alternatives considered**:
- **A `DelegatingHandler` that adds the header to every request**: more moving parts than needed for write-only auth (a handler was introduced later with token refresh, `277a46b`). Rejected for this feature.
- **Pass the token as a parameter of each client method**: pollutes every signature. Rejected.

## Decision 3: Let the API remain the only credential authority

**Decision**: MVC posts `{ userName, password }` to `api/account/login`; a 401 maps to a `null` result and the generic error "Invalid username or password."; MVC never hashes, stores or logs the password. `LoginViewModel` only requires non-blank values.

**Rationale**: Principle IV and V: credentials are verified by the API's `PasswordHasher`; MVC cannot diverge from the API's rules.

**Alternatives considered**: ASP.NET Core Identity in MVC with its own user table. Rejected: violates the single-database-owner decision from 006.

## Decision 4: Handle an expired token by signing out

**Decision**: If a write returns `ApiResult.Unauthorized`, the controller calls `SignOutAsync` and redirects to `Account/Login` with `returnUrl = Request.Path + Request.QueryString`. `[Authorize]` (default policy) on every create/edit/delete action redirects anonymous users to `LoginPath`.

**Rationale**: Prevents users from being stuck with a stale cookie, and returns them to where they were after signing in. `Url.IsLocalUrl` is checked before redirecting back (no open redirect).

**Alternatives considered**: Show an error and keep the session. Rejected: the user could never recover without clearing cookies.

## Decision 5: Minimal layout and view changes

**Decision**: The layout shows "Hello, {name}" and a Logout form (POST, anti-forgery) when signed in, otherwise a Login link; Index pages show "Create New" only to authenticated users. Logout is `[HttpPost]` with `[ValidateAntiForgeryToken]`.

**Rationale**: POST logout prevents cross-site logout via a simple link. Role-based hiding is deferred to 008.
