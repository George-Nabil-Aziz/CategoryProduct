# Quickstart: Validating Request Validation

Manual verification guide, per the constitution's Development Workflow (no automated test project
exists yet in this solution). Run these against the running `CategoryProduct.API` via Swagger UI
or any HTTP client, once the feature is implemented.

## Prerequisites

- `CategoryProduct.API` running locally (Swagger UI enabled in Development, per `Program.cs`).
- At least one existing `Category` row to reference by id (needed for valid `Product` requests).

## Category scenarios

See contract: [`contracts/category-api.md`](contracts/category-api.md)

1. **Missing name** — `POST /api/Category` with `{ "name": "" }` → expect `400` with a `Name`
   error; confirm no new row via `GET /api/Category`.
2. **Too short** — `POST /api/Category` with `{ "name": "A" }` → expect `400` with a `Name` error.
3. **Too long** — `POST /api/Category` with a 101-character `name` → expect `400` with a `Name`
   error.
4. **Whitespace only** — `POST /api/Category` with `{ "name": "   " }` → expect `400` with a
   `Name` error.
5. **Valid boundary** — `POST /api/Category` with a 2-character `name`, then a 100-character
   `name` → expect `200` and the `Category` persisted both times (verify via `GET`).
6. **Update rejected** — `PUT /api/Category/{id}` on an existing category with `{ "name": "" }` →
   expect `400`; confirm via `GET /api/Category/{id}` that the original name is unchanged.

## Product scenarios

See contract: [`contracts/product-api.md`](contracts/product-api.md)

1. **Bad name** — `POST /api/Product` with an empty/1-char/102-char `name` (valid `price`,
   `quantity`, `categoryId`) → expect `400` with a `Name` error each time.
2. **Negative price** — `POST /api/Product` with `price: -1` → expect `400` with a `Price` error.
3. **Negative quantity** — `POST /api/Product` with `quantity: -1` → expect `400` with a
   `Quantity` error.
4. **Non-positive categoryId** — `POST /api/Product` with `categoryId: 0` and `categoryId: -5` →
   expect `400` with a `CategoryId` error both times.
5. **Multiple failures at once** — `POST /api/Product` with `name: ""`, `price: -1` → expect a
   single `400` whose `errors` object lists both `Name` and `Price`.
6. **Valid boundary** — `POST /api/Product` with `price: 0`, `quantity: 0`, `categoryId` equal to
   an existing category's id → expect `200` and the `Product` persisted.
7. **Update rejected** — `PUT /api/Product/{id}` on an existing product with `quantity: -1` →
   expect `400`; confirm via `GET /api/Product/{id}` that the original record is unchanged.

## Unaffected endpoints (regression check)

- `GET /api/Category`, `GET /api/Category/{id}`, `GET /api/Category/{catId}/products`,
  `DELETE /api/Category/{id}` — confirm behavior is identical to before this feature.
- `GET /api/Product`, `GET /api/Product/{id}`, `DELETE /api/Product/{id}` — confirm behavior is
  identical to before this feature.

## Success criteria mapping

- SC-001 / SC-004 → scenarios 1–4 (Category) and 1–4 (Product) above, plus confirming no invalid
  row appears via `GET` afterward.
- SC-002 → every `400` response above must name the specific failing field(s).
- SC-003 → the "valid boundary" scenarios must continue to return `200` and persist correctly.
