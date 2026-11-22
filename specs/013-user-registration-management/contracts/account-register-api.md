# Contract: Register (addition to Account API)

Base route: `api/Account` (same controller as login; see `003-jwt-authentication`).

## POST /api/account/register

**Auth**: none. **Rate limit**: policy `register` (per client IP, 1-minute fixed window, default 5).

**Request body**: `RegisterRequestDto { UserName: string, Password: string }`

- `UserName`: required, 3-50 characters, letters/digits/`.`/`_`/`-` only; trimmed before use.
- `Password`: required, at least 8 characters.

**Responses**

| Status | When | Body |
|--------|------|------|
| `201 Created` | Account created with role `User` | `UserResponseDto { id, userName, role }` |
| `400 Bad Request` | Validation failed | `ValidationProblemDetails` (framework) |
| `409 Conflict` | Username already taken (case-insensitive) or unique index violation | `ProblemDetails { status: 409, title: "Conflict", detail: "Username already taken." }` |
| `429 Too Many Requests` | Rate limit exceeded | (rate limiter default; no body defined) |

No `Location` header is set. The password and hash are never returned.
