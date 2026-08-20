# Contract: Protected write endpoints (Category, Product)

This feature adds an authorization requirement in front of existing endpoints; it does not change
any request/response body shape already documented in
`specs/002-request-validation/contracts/category-api.md` and
`.../product-api.md`. Only the new `Authorization` requirement is documented here.

## Affected endpoints

- `POST /api/Category`, `PUT /api/Category/{id}`, `DELETE /api/Category/{id}`
- `POST /api/Product`, `PUT /api/Product/{id}`, `DELETE /api/Product/{id}`

## Requirement

Every request to an endpoint above MUST include an `Authorization: Bearer <token>` header carrying
a valid, unexpired token issued by `POST /api/Account/login` (see `account-api.md`).

- **Missing header** → `401 Unauthorized`; no data modified.
- **Malformed token** (not a well-formed JWT, or signed with the wrong key) → `401 Unauthorized`;
  no data modified.
- **Expired token** (`exp` claim in the past, evaluated with zero clock skew) → `401 Unauthorized`;
  no data modified.
- **Valid token** → request proceeds exactly as it did before this feature; same success response
  shape, same validation-error behavior from `002-request-validation`, same `404` for a missing
  `id`.

All rejections above return the standard ASP.NET Core `JwtBearer` challenge response — no custom
error body is introduced, and none of the three rejection cases are distinguishable from each
other by an API consumer (generic `401`, per the spec's edge cases on expired vs. malformed
credentials).

## Unaffected endpoints

- `GET /api/Category`, `GET /api/Category/{id}`, `GET /api/Category/{catId}/products`,
  `GET /api/Product`, `GET /api/Product/{id}` — no `Authorization` header required; behavior
  identical with or without one present (FR-004).
