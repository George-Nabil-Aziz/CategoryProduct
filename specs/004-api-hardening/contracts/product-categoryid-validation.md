# Contract: Product CategoryId validation (changed)

Base route: `api/Product`. Only the new failure case is documented; other request/response details are in `specs/002-request-validation/contracts/product-api.md`. Both endpoints require a bearer token (see `specs/003-jwt-authentication/contracts/protected-write-endpoints.md`; Admin role from 008).

## POST /api/Product

## PUT /api/Product/{id}

**New failure**: `400 Bad Request` with `ValidationProblemDetails` when no `Category` has the submitted `CategoryId`. The `errors` object contains:

```json
{ "CategoryId": ["Category does not exist."] }
```

- Nothing is saved.
- The check runs before any other lookup. For `PUT`, an unknown `CategoryId` returns 400 even if `{id}` does not exist; `404 Not Found` for a missing product is returned only when `CategoryId` is valid.
- With an existing `CategoryId` behavior is unchanged (`200 OK` with `ProductResponseDto`).

## Startup contract (not HTTP)

The API process throws `InvalidOperationException` at startup when `Jwt:Key` is missing, blank or under 32 characters, so no endpoint is served.
