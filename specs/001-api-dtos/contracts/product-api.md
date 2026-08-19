# Contract: Product endpoints (`CategoryProduct.API`)

Base route: `api/product` (unchanged). All routes, HTTP methods, and status codes below are
existing behavior — only the request/response bodies change to the DTO shapes defined in
`../data-model.md`.

## `GET /api/product?page={page}&pageSize={pageSize}`

- **Response 200 OK**: `ProductResponseDto[]`

```json
[
  { "id": 10, "name": "Cola", "price": 1.99, "quantity": 100, "categoryId": 1 }
]
```

## `GET /api/product/{id}`

- **Response 200 OK**: `ProductResponseDto`
- **Response 404 Not Found**: product with the given id does not exist

```json
{ "id": 10, "name": "Cola", "price": 1.99, "quantity": 100, "categoryId": 1 }
```

No nested `category` object is ever included (FR-002, clarified 2026-08-19).

## `POST /api/product`

- **Request body**: `ProductRequestDto`

```json
{ "name": "Cola", "price": 1.99, "quantity": 100, "categoryId": 1 }
```

- **Response 200 OK**: `ProductResponseDto` (server-assigned `id` included)

```json
{ "id": 10, "name": "Cola", "price": 1.99, "quantity": 100, "categoryId": 1 }
```

- Any `id` field present in the request body is ignored (FR-005; edge case in `spec.md`).

## `PUT /api/product/{id}`

- **Request body**: `ProductRequestDto` (same shape as create — FR-006)

```json
{ "name": "Cola", "price": 2.49, "quantity": 80, "categoryId": 1 }
```

- **Response 200 OK**: `ProductResponseDto` reflecting the update
- **Response 404 Not Found**: product with the given id does not exist

## `DELETE /api/product/{id}`

- **Response 200 OK**: no body (unchanged)
- **Response 404 Not Found**: product with the given id does not exist
