# Contract: Category endpoints (`CategoryProduct.API`)

Base route: `api/category` (unchanged). All routes, HTTP methods, and status codes below are
existing behavior — only the request/response bodies change to the DTO shapes defined in
`../data-model.md`.

## `GET /api/category?page={page}&pageSize={pageSize}`

- **Response 200 OK**: `CategoryResponseDto[]`

```json
[
  { "id": 1, "name": "Beverages" },
  { "id": 2, "name": "Snacks" }
]
```

## `GET /api/category/{id}`

- **Response 200 OK**: `CategoryResponseDto`
- **Response 404 Not Found**: category with the given id does not exist

```json
{ "id": 1, "name": "Beverages" }
```

## `POST /api/category`

- **Request body**: `CategoryRequestDto`

```json
{ "name": "Beverages" }
```

- **Response 200 OK**: `CategoryResponseDto` (server-assigned `id` included)

```json
{ "id": 1, "name": "Beverages" }
```

- Any `id` field present in the request body is ignored (FR-003; edge case in `spec.md`).

## `PUT /api/category/{id}`

- **Request body**: `CategoryRequestDto` (same shape as create — FR-004)

```json
{ "name": "Beverages (renamed)" }
```

- **Response 200 OK**: `CategoryResponseDto` reflecting the update
- **Response 404 Not Found**: category with the given id does not exist

## `DELETE /api/category/{id}`

- **Response 200 OK**: no body (unchanged)
- **Response 404 Not Found**: category with the given id does not exist

## `GET /api/category/{id}/products`

- **Response 200 OK**: `ProductResponseDto[]` — same shape as `GET /api/product` (FR-007); see
  `./product-api.md`

```json
[
  { "id": 10, "name": "Cola", "price": 1.99, "quantity": 100, "categoryId": 1 }
]
```
