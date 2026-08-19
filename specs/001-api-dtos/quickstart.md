# Quickstart: Validating API DTOs for Category & Product

This guide proves the feature works end-to-end using the running API and Swagger UI, per the
constitution's Development Workflow (no automated test project exists yet for
`CategoryProduct.API`).

## Prerequisites

- .NET 10 SDK installed
- SQL Server instance reachable via the connection string in
  `CategoryProduct.API/appsettings.json` (or `appsettings.Development.json`), with migrations
  already applied (`dotnet ef database update` from `CategoryProduct.API/`)
- At least one existing category in the database (create one via the steps below if empty)

## Run the API

```bash
dotnet run --project CategoryProduct.API
```

Open the Swagger UI shown in the console output (development environment only).

## Scenario 1 — Clean read responses (User Story 1, FR-001/FR-002/FR-008)

1. `GET /api/category` → confirm each item in the array is exactly `{ "id": ..., "name": ... }`
   with **no** `products` field.
2. `GET /api/category/{id}` for an existing id → same shape as above, single object.
3. `GET /api/product` → confirm each item is exactly
   `{ "id": ..., "name": ..., "price": ..., "quantity": ..., "categoryId": ... }` with **no**
   nested `category` object and **no** category name field.
4. `GET /api/product/{id}` for an existing id → same shape as above, single object.

**Expected outcome**: no response body contains a `products` array on a category or a `category`
object/`categoryName` field on a product — see `contracts/category-api.md` and
`contracts/product-api.md` for exact shapes.

## Scenario 2 — Well-defined write requests (User Story 2, FR-003–FR-006)

1. `POST /api/category` with body `{ "name": "Test Category" }` → response is
   `{ "id": <new-id>, "name": "Test Category" }`.
2. Repeat the same `POST /api/category`, but this time include an `id` in the body (e.g.,
   `{ "id": 999, "name": "Ignored Id Test" }`) → response's `id` is a new server-assigned value,
   **not** `999`.
3. `PUT /api/category/{id}` (using the id from step 1) with body
   `{ "name": "Renamed Category" }` → response reflects the rename.
4. `POST /api/product` with body
   `{ "name": "Test Product", "price": 9.99, "quantity": 5, "categoryId": <id from step 1> }` →
   response includes a server-assigned `id` and echoes the submitted fields.
5. `PUT /api/product/{id}` (using the id from step 4) with an updated body → response reflects the
   update.

**Expected outcome**: creation/update succeed using only the documented request fields; a
client-supplied `id` never determines the resulting resource's identifier.

## Scenario 3 — Products-by-category listing consistency (User Story 3, FR-007)

1. `GET /api/category/{id}/products` (using the category id from Scenario 2) → confirm the
   returned product(s) use the exact same shape as `GET /api/product` (no nested category object).

**Expected outcome**: the products-by-category listing and the standalone product list return
identically-shaped items.

## Cleanup

Delete any test category/product created above via `DELETE /api/category/{id}` and
`DELETE /api/product/{id}` if you want to leave the database as you found it.
