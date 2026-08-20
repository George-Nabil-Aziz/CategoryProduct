# Contract: Category API validation

Base route: `api/Category` (unchanged). Only the `POST` and `PUT` contracts below change; `GET`
and `DELETE` are unaffected by this feature.

## POST /api/Category

**Request body**: `CategoryRequestDto { Name: string }`

**Success**: `200 OK` with `CategoryResponseDto { Id, Name }` — unchanged from today, requires:
- `Name` is present and, after trimming, non-empty
- `Name` length is between 2 and 100 characters (inclusive)

**Validation failure** (new): `400 Bad Request` with a `ValidationProblemDetails` body whose
`errors` object contains a `Name` entry when either rule above is violated. Example shape:

```json
{
  "errors": {
    "Name": ["The Name field is required."]
  }
}
```

No `Category` is created when validation fails.

## PUT /api/Category/{id}

**Request body**: `CategoryRequestDto { Name: string }`

**Success**: `200 OK` with the updated `CategoryResponseDto` — unchanged from today, same `Name`
rules as `POST` above.

**Validation failure** (new): `400 Bad Request`, same shape as `POST`, evaluated before the
existing `404 Not Found` (id lookup) short-circuits or after — order is an implementation detail,
but the existing record MUST remain unmodified when validation fails.

## Unaffected

- `GET /api/Category`, `GET /api/Category/{id}`, `GET /api/Category/{catId}/products`,
  `DELETE /api/Category/{id}` — no request body validated by this feature; behavior unchanged.
