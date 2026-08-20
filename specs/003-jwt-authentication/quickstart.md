# Quickstart: Validating JWT Authentication

Manual verification guide, per the constitution's Development Workflow (no automated test project
exists yet in this solution). Run these against the running `CategoryProduct.API` via Swagger UI
or any HTTP client, once the feature is implemented.

## Prerequisites

- A real JWT signing key set locally via user secrets (run once from `CategoryProduct.API/`):
  ```
  dotnet user-secrets init
  dotnet user-secrets set "Jwt:Key" "<a random base64 string of at least 32 bytes>"
  ```
  (`appsettings.json`'s committed `Jwt:Key` is intentionally left blank/placeholder — see
  `research.md` Decision 3.)
- At least one pre-provisioned `User` row (`UserName` + `PasswordHash` produced by
  `PasswordHasher<User>.HashPassword`, inserted directly, e.g. via a short one-off script or
  SQL — there is no self-service registration endpoint by design).
- `CategoryProduct.API` running locally (Swagger UI enabled in Development, per `Program.cs`).

## Login scenarios

See contract: [`contracts/account-api.md`](contracts/account-api.md)

1. **Valid login** — `POST /api/Account/login` with the correct `UserName`/`Password` for a
   pre-provisioned account → expect `200 OK` with a `Token` and `ExpiresAtUtc`.
2. **Wrong password** — same `UserName`, incorrect `Password` → expect `401 Unauthorized`, generic
   body, no token.
3. **Unknown username** — a `UserName` with no matching account → expect the same `401
   Unauthorized`, generic body, indistinguishable from scenario 2.
4. **Missing fields** — omit `UserName` or `Password` entirely → expect `400 Bad Request` with a
   field-level validation error, before any account lookup.

## Protected write-endpoint scenarios

See contract: [`contracts/protected-write-endpoints.md`](contracts/protected-write-endpoints.md)

Using a token obtained from scenario 1 above (`Authorization: Bearer <token>` header):

1. **No header** — `POST /api/Category` (or any write endpoint) with no `Authorization` header →
   expect `401 Unauthorized`; confirm via `GET` that nothing was created/changed.
2. **Malformed token** — same request with `Authorization: Bearer not-a-real-token` → expect `401
   Unauthorized`; no data modified.
3. **Valid token** — same request with the real token → expect the same success response as
   before this feature shipped (e.g. `200 OK` with the created `CategoryResponseDto`).
4. **Expired token** — wait past `Jwt:ExpiryMinutes` (or temporarily set it to a small value, e.g.
   `1`, log in, wait), then retry with that token → expect `401 Unauthorized`.
5. **Repeat 1–4** for `PUT` and `DELETE` on both `Category` and `Product`.

## Regression check (reads stay public)

- `GET /api/Category`, `GET /api/Category/{id}`, `GET /api/Category/{catId}/products`,
  `GET /api/Product`, `GET /api/Product/{id}` — confirm all succeed with **and** without an
  `Authorization` header, identical to behavior before this feature.

## Success criteria mapping

- SC-001 → protected-write scenarios 1–3 and 5, plus the `GET` regression check.
- SC-002 → login scenario 1 followed immediately by protected-write scenario 3.
- SC-003 → inspect the `Users` table directly; `PasswordHash` values must never equal any known
  plaintext password.
- SC-004 → protected-write scenario 4.
