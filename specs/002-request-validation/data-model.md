# Data Model: Request Validation

This feature adds validation rules to two existing request DTOs. It does not add, remove, or
rename any entity, and does not change any Response DTO or EF Core model/migration.

## CategoryRequestDto

`CategoryProduct.API/Dtos/CategoryRequestDto.cs`

| Field | Type | Rule(s) | Source |
|-------|------|---------|--------|
| `Name` | `string` | Required (non-blank after trimming whitespace); length between 2 and 100 characters (inclusive) | FR-001, FR-002 |

No relationships change. Consumed by `POST /api/Category` and `PUT /api/Category/{id}` only;
`GET`/`DELETE` are unaffected (FR-008).

## ProductRequestDto

`CategoryProduct.API/Dtos/ProductRequestDto.cs`

| Field | Type | Rule(s) | Source |
|-------|------|---------|--------|
| `Name` | `string` | Required (non-blank after trimming whitespace); length between 2 and 100 characters (inclusive) | FR-003 |
| `Price` | `decimal` | Must be `>= 0` | FR-004 |
| `Quantity` | `int` | Must be `>= 0` | FR-005 |
| `CategoryId` | `int` | Must be `> 0` | FR-006 |

No relationships change; `CategoryId` is not verified against existing `Category` rows as part of
this feature (see spec Assumptions — out of scope). Consumed by `POST /api/Product` and
`PUT /api/Product/{id}` only; `GET`/`DELETE` are unaffected (FR-008).

## Unaffected entities/DTOs

- `CategoryResponseDto`, `ProductResponseDto` — untouched (FR-009).
- `Category`, `Product`, `User` (EF Core models) — untouched; no migration required, since no
  schema or model shape changes.

## Validation error shape

When a request fails one or more rules, the framework's automatic `[ApiController]` model-
validation behavior returns `400 Bad Request` with a `ValidationProblemDetails` body whose
`errors` dictionary maps each failing field name (`Name`, `Price`, `Quantity`, `CategoryId`) to
the list of validation messages for that field — covering the "multiple invalid fields at once"
edge case in one response, per FR-007.
