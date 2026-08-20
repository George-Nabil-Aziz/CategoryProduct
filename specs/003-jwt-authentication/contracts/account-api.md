# Contract: Account API (new)

Base route: `api/Account` (new controller action; `AccountController` already exists as an empty
stub).

## POST /api/Account/login

**Request body**: `LoginRequestDto { UserName: string, Password: string }`

**Success**: `200 OK` with `LoginResponseDto { Token: string, ExpiresAtUtc: DateTime }` when
`UserName` matches an existing account (case-insensitive) and `Password` verifies against its
stored `PasswordHash`.

**Failure**: `401 Unauthorized` with a generic body (no field-level or "which part was wrong"
detail) when:
- `UserName` does not match any account, or
- `Password` does not verify against the matched account's `PasswordHash`.

Both failure cases MUST be indistinguishable from the response alone (FR-003; spec edge case on
not revealing which accounts exist). No `LoginResponseDto`/token is returned on failure.

**Request validation**: `UserName` and `Password` are both required (non-blank) via
`DataAnnotations`, consistent with the pattern established in `002-request-validation` — a request
missing either field returns `400 Bad Request` with a `ValidationProblemDetails` body before any
account lookup happens.
