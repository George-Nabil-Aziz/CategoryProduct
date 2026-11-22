# Feature Specification: API Quick Fixes

**Feature Branch**: `009-api-quick-fixes` (documented retroactively; implemented directly on `master`)

**Created**: 2026-11-03

**Status**: Implemented (commits `59e6836`, `94b68a8`, `d643b40`, `89d69df`)

**Input**: Four small, independent improvements delivered without a prior spec: fix a vulnerable
transitive dependency, add a JWT "Authorize" button to Swagger UI, refuse deleting a category that
still has products, and rate limit the login endpoint.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Block deleting a category that still has products (Priority: P1)

An admin who tries to delete a category that still owns products is told why it was refused, and
neither the category nor its products are changed.

**Why this priority**: It prevents accidental loss of catalogue data (or a database constraint error
surfacing as a generic failure).

**Independent Test**: Create a category, add a product to it, then `DELETE /api/category/{id}` with a
valid token and confirm `409` and that `GET /api/category/{id}` still returns `200`. Covered by
`DeleteCategory_WithProducts_Returns409` in `CategoryProduct.API.Tests/ApiTests.cs`.

**Acceptance Scenarios**:

1. **Given** a category with at least one product, **When** an authorised caller deletes it, **Then**
   the API responds `409 Conflict` with a ProblemDetails body (title "Category has products", detail
   "Delete or move its products first.") and the category still exists.
2. **Given** a category with no products, **When** an authorised caller deletes it, **Then** it is
   deleted as before (unchanged behaviour).
3. **Given** the MVC delete-confirmation page for a category that has products, **When** the admin
   confirms deletion, **Then** the 409 detail text is shown as a form-level error on the Delete page
   instead of an error page.

---

### User Story 2 - Throttle repeated login attempts (Priority: P1)

Repeated login requests from one client address are limited so the endpoint cannot be hammered with
password guesses.

**Why this priority**: Closes the "no lockout/throttling" gap that `003-jwt-authentication` explicitly
left out of scope.

**Independent Test**: Post more than the permitted number of logins within one minute from the same
address and confirm the over-limit request returns `429`. Covered by
`Login_ExceedingLimit_Returns429` in `RateLimitTests` (limit set to 2 for the test).

**Acceptance Scenarios**:

1. **Given** the permit limit is N, **When** a client sends N login requests within one minute,
   **Then** each is processed normally (e.g. wrong credentials give `401`).
2. **Given** a client has used its N permits in the current one-minute window, **When** it sends
   another login request, **Then** the API responds `429 Too Many Requests` without processing it.
3. **Given** the MVC login form, **When** the API returns `429`, **Then** the form shows "Too many
   login attempts. Try again in a minute." rather than "Invalid username or password."

---

### User Story 3 - Authorize from Swagger UI (Priority: P2)

A developer testing the API in Swagger UI can paste the JWT from the login endpoint once and have it
sent on all protected calls.

**Why this priority**: Developer convenience only; no runtime behaviour change.

**Independent Test**: Run the API in Development, open Swagger UI, confirm an **Authorize** button
exists, paste a token and call a protected write endpoint. (Manual; no automated test.)

**Acceptance Scenarios**:

1. **Given** Swagger UI is open, **When** the developer opens the Authorize dialog, **Then** a `Bearer`
   (HTTP bearer, JWT) scheme with the description "Paste the JWT returned by the login endpoint." is
   offered.
2. **Given** a token was entered, **When** any operation is executed from Swagger UI, **Then** it is
   sent with an `Authorization: Bearer <token>` header.

---

### User Story 4 - Remove a vulnerable dependency (Priority: P2)

The API no longer pulls in a vulnerable version of `System.Security.Cryptography.Pkcs`.

**Why this priority**: Security hygiene; no functional change.

**Independent Test**: `dotnet list CategoryProduct.API package --vulnerable --include-transitive`
reports no vulnerable `System.Security.Cryptography.Pkcs` (manual check, not re-run when writing
this spec).

**Acceptance Scenarios**:

1. **Given** the API project, **When** it is restored, **Then** `System.Security.Cryptography.Pkcs`
   resolves to version `10.0.14` through an explicit `PackageReference`.

---

### Edge Cases

- Deleting a category that does not exist still returns `404` (the existence check precedes the
  product check).
- The product-existence check runs before removal; there is no transaction around check and delete
  (a product added between the two is not guarded against).
- The login rate limit is keyed on the remote IP address (`"unknown"` when unavailable); clients
  behind one shared address/proxy share a single bucket. Successful logins also consume permits.
- Requests blocked by the limiter never reach the controller, so a correct password does not bypass
  the `429`.
- The MVC client reports a `409` whose body cannot be parsed as "The request conflicts with existing
  data."

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: `DELETE /api/category/{id}` MUST return `409 Conflict` (ProblemDetails; title
  "Category has products", detail "Delete or move its products first.") and MUST NOT delete the
  category when any product references it.
- **FR-002**: The MVC client MUST surface the API's 409 `detail` as a form-level model error on the
  category Delete page (fallback text "The request conflicts with existing data.").
- **FR-003**: `POST /api/account/login` MUST be rate limited with a fixed one-minute window per client
  IP address, no queueing, and rejection status `429`.
- **FR-004**: The permit count MUST be configurable through `RateLimiting:LoginPermitLimit` and
  default to 5.
- **FR-005**: The MVC login action MUST show "Too many login attempts. Try again in a minute." when the
  API returns `429`.
- **FR-006**: Swagger UI MUST expose an HTTP `bearer` (JWT) security scheme named `Bearer` and apply it
  as a global security requirement.
- **FR-007**: `CategoryProduct.API.csproj` MUST reference `System.Security.Cryptography.Pkcs` `10.0.14`
  explicitly.
- **FR-008**: No existing success-path response shape may change.

### Key Entities

- **Category / Product**: unchanged; the delete rule relies on the existing `Product.CategoryId`
  relationship.
- **Rate-limit policy `login`**: fixed window, per client IP, permit limit from configuration.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Deleting a category with products returns `409` and leaves it retrievable (test
  `DeleteCategory_WithProducts_Returns409` passes).
- **SC-002**: With the permit limit set to 2, the third login within a minute returns `429` (test
  `Login_ExceedingLimit_Returns429` passes); other tests run with limit 1000 so they are unaffected.
- **SC-003**: Swagger UI shows an Authorize button that accepts a JWT.
- **SC-004**: The API project explicitly references `System.Security.Cryptography.Pkcs` `10.0.14`.

## Assumptions

- Only admins reach category deletion (role rules from `008-role-authorization`); this feature does
  not change authorisation.
- Rate limiting is in-memory per API instance (the built-in limiter); it is not shared across
  instances.
- Swagger UI is only enabled in Development (existing `Program.cs` behaviour).

## Known Gaps

- MVC flows (409 message on the Delete page, 429 message on the login page) have no automated tests
  and were not exercised in a browser while documenting.
- No test covers the successful category delete path in this commit, the Swagger Authorize button, or
  the `RateLimiting:LoginPermitLimit` default of 5.
- The `429` response has no custom body configured in code.
- The advisory that motivated the `Pkcs` pin is not recorded in the commit message and is not stated
  here.
- The product check and the delete are not atomic.
