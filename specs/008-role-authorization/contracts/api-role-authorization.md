# Contract: API role authorization (changed)

Builds on `specs/003-jwt-authentication/contracts/account-api.md` and `protected-write-endpoints.md`.

## POST /api/Account/login (response changed)

**Request body**: `LoginRequestDto { UserName, Password }` - unchanged.

**Success**: `200 OK` with `LoginResponseDto { Token: string, ExpiresAtUtc: DateTime, Role: string }`. `Role` is the user's role (`"Admin"` or `"User"`); the JWT additionally carries a `ClaimTypes.Role` claim with the same value.

**Failure**: `401 Unauthorized` and `400 Bad Request` as in 003.

## Protected write endpoints (requirement tightened)

- `POST /api/Category`, `PUT /api/Category/{id}`, `DELETE /api/Category/{id}`
- `POST /api/Product`, `PUT /api/Product/{id}`, `DELETE /api/Product/{id}`

Each requires `Authorization: Bearer <token>` for a token whose role claim is `Admin` (`[Authorize(Roles = "Admin")]`).

| Request | Status | Data changed |
|---------|--------|--------------|
| No header, malformed or expired token | `401 Unauthorized` | No |
| Valid token, role not `Admin` | `403 Forbidden` | No |
| Valid token, role `Admin` | Same result as before this feature (`200 OK` with the response DTO, or the existing 400/404) | As before |

## Public endpoints (unchanged)

All `GET` endpoints (`api/Category`, `api/Category/{id}`, `api/Category/{catId}/products`, `api/Product`, `api/Product/{id}`) stay anonymous regardless of role.

## Verified by

`PostCategory_NonAdminToken_Returns403` (login as `plainuser`, `POST api/category` -> 403), plus the 401 and 400 tests from 005 which still pass using the Admin user `tester`.
