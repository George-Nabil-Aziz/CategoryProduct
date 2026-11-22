# Research: API Quick Fixes

## Decision 1: Fix the vulnerable package by adding an explicit reference

**Decision**: Add `<PackageReference Include="System.Security.Cryptography.Pkcs" Version="10.0.14" />`
to `CategoryProduct.API.csproj`.

**Rationale**: A direct reference overrides the lower transitive version without changing any other
dependency (one-line change). The specific advisory is not recorded in the commit and is not asserted
here.

**Alternatives considered**: Upgrade the parent packages (larger blast radius; not done).

## Decision 2: Swagger bearer scheme as HTTP/JWT with a global requirement

**Decision**: `AddSecurityDefinition("Bearer", ...)` with `SecuritySchemeType.Http`, scheme `bearer`,
`BearerFormat = "JWT"`, plus `AddSecurityRequirement` referencing it through
`OpenApiSecuritySchemeReference("Bearer", document)`.

**Rationale**: With the HTTP bearer type Swagger UI prepends `Bearer ` itself, so users paste the raw
token. A global requirement is the least code; it also marks public GETs as secured in the UI
(accepted).

**Alternatives considered**: An operation filter that only marks `[Authorize]` actions (more code,
YAGNI).

## Decision 3: 409 with ProblemDetails for category delete

**Decision**: In `CategoryController.Delete`, after the 404 check,
`AnyAsync(p => p.CategoryId == id)` then
`Problem(statusCode: 409, title: "Category has products", detail: "Delete or move its products first.")`.

**Rationale**: Gives a clear, client-readable reason (ProblemDetails, aligned with `011`) rather than
a database FK error or a silent cascade.

**Alternatives considered**: Cascade delete (destroys data); reassign products (new behaviour, out of
scope).

## Decision 4: Built-in fixed-window rate limiter, per IP

**Decision**: `AddRateLimiter` with policy `login`: `RateLimitPartition.GetFixedWindowLimiter` keyed by
`RemoteIpAddress` (fallback `"unknown"`), `Window = 1 minute`, `QueueLimit = 0`,
`PermitLimit = Configuration["RateLimiting:LoginPermitLimit"]` default 5, `RejectionStatusCode = 429`.
Applied with `[EnableRateLimiting("login")]`; `app.UseRateLimiter()` runs after
`UseAuthentication`/`UseAuthorization`.

**Rationale**: No new package; configurable so tests can use 1000 (all other tests) or 2 (the rate
limit test).

**Alternatives considered**: Per-username limiting or lockout (more state; out of scope).

## Decision 5: MVC representation of the new statuses

**Decision**: `ApiResult.Conflict` (`string?`) carries the 409 detail; `LoginResult.TooManyRequests` is
a static sentinel instance compared with `ReferenceEquals` in `AccountController.Login`.

**Rationale**: Fits the existing `ApiResult` pattern with minimal change. The sentinel is a pragmatic
shortcut rather than a dedicated result type.
