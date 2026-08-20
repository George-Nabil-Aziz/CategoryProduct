# Contract: Product API validation

Base route: `api/Product` (unchanged). Only the `POST` and `PUT` contracts below change; `GET` and
`DELETE` are unaffected by this feature.

## POST /api/Product

**Request body**: `ProductRequestDto { Name: string, Price: decimal, Quantity: int, CategoryId: int }`

**Success**: `200 OK` with `ProductResponseDto { Id, Name, Price, Quantity, CategoryId }` —
unchanged from today, requires:
- `Name` is present and, after trimming, non-empty; length between 2 and 100 characters (inclusive)
- `Price` is `>= 0`
- `Quantity` is `>= 0`
- `CategoryId` is `> 0`

**Validation failure** (new): `400 Bad Request` with a `ValidationProblemDetails` body whose
`errors` object contains an entry for every field that failed. Example with two simultaneous
failures:

```json
{
  "errors": {
    "Name": ["The field Name must be a string with a minimum length of 2 and a maximum length of 100."],
    "Price": ["The field Price must be between 0 and 79228162514264337593543950335."]
  }
}
```

No `Product` is created when validation fails. Note: `CategoryId > 0` is a format check only —
this feature does not verify that a `Category` with that id actually exists (out of scope; see
spec Assumptions).

## PUT /api/Product/{id}

**Request body**: `ProductRequestDto { Name, Price, Quantity, CategoryId }`

**Success**: `200 OK` with the updated `ProductResponseDto` — unchanged from today, same rules as
`POST` above.

**Validation failure** (new): `400 Bad Request`, same shape as `POST`. The existing record MUST
remain unmodified when validation fails.

## Unaffected

- `GET /api/Product`, `GET /api/Product/{id}`, `DELETE /api/Product/{id}` — no request body
  validated by this feature; behavior unchanged.
