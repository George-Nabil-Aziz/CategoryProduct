# Contract: Users API (admin)

Base route: `api/users`. The entire controller is `[Authorize(Roles = "Admin")]`: no/invalid token
-> `401`, authenticated non-admin -> `403`.

## GET /api/users

**Query**: `page` (default 1, values < 1 become 1), `pageSize` (default 10, clamped to 1-50),
`search` (optional; trimmed, case-insensitive "contains" on `UserName`).

**Success**: `200 OK` with `PagedResponseDto<UserResponseDto>`:

```json
{ "items": [ { "id": 1, "userName": "tester", "role": "Admin" } ],
  "page": 1, "pageSize": 10, "totalCount": 1, "totalPages": 1 }
```

Ordered by `UserName`. Never includes `passwordHash`.

## PUT /api/users/{id}/role

**Request body**: `UpdateRoleRequestDto { Role: string? }` - must be exactly `User` or `Admin`.

| Status | When | Body |
|--------|------|------|
| `200 OK` | Role updated | `UserResponseDto` |
| `400 Bad Request` | Role missing or not `User`/`Admin` | `ValidationProblemDetails` with `Role: ["Role must be 'User' or 'Admin'."]` |
| `404 Not Found` | No user with that id | empty |
| `409 Conflict` | Target is the only `Admin` and new role is not `Admin` | `ProblemDetails` detail `Cannot remove the last admin.` |
| `401` / `403` | Not authenticated / not admin | framework default |

Setting a user to the role they already have returns `200`.
