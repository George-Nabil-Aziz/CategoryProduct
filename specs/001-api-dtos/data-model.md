# Data Model: API DTOs for Category & Product

This feature does not change the persistence model (`Category`, `Product` entities and the
database schema are unchanged, per FR-010). It introduces API-facing DTOs that sit between the
existing entities and the HTTP layer. Entities are included below for reference/traceability only.

## Existing Entities (unchanged)

### Category (`CategoryProduct.API/Models/Category.cs`)

| Field | Type | Notes |
|---|---|---|
| `Id` | `int` | Primary key, server-assigned |
| `Name` | `string` | Category name |
| `Products` | `ICollection<Product>` | Navigation property — **not** exposed via any DTO (FR-001, FR-008) |

### Product (`CategoryProduct.API/Models/Product.cs`)

| Field | Type | Notes |
|---|---|---|
| `Id` | `int` | Primary key, server-assigned |
| `Name` | `string` | Product name |
| `Price` | `decimal` | Product price |
| `Quantity` | `int` | Stock quantity |
| `CategoryId` | `int` | Foreign key to `Category` |
| `Category` | `Category?` | Navigation property — **not** exposed via any DTO (FR-002, FR-008) |

## New DTOs (`CategoryProduct.API/Dtos/`)

### CategoryResponseDto

Returned by: `GET /api/category`, `GET /api/category/{id}`, `POST /api/category`,
`PUT /api/category/{id}` (per existing controller behavior of echoing back the resource).

| Field | Type | Source | Notes |
|---|---|---|---|
| `Id` | `int` | `Category.Id` | Server-assigned, always present |
| `Name` | `string` | `Category.Name` | |

No navigation data (FR-001).

### CategoryRequestDto

Accepted by: `POST /api/category` (create), `PUT /api/category/{id}` (update) — same shape for
both per the research decision (one shared request DTO).

| Field | Type | Notes |
|---|---|---|
| `Name` | `string` | Only field the client controls; `Id` is never accepted from the client (FR-003, FR-004) |

### ProductResponseDto

Returned by: `GET /api/product`, `GET /api/product/{id}`, `POST /api/product`,
`PUT /api/product/{id}`, and `GET /api/category/{id}/products` (FR-007).

| Field | Type | Source | Notes |
|---|---|---|---|
| `Id` | `int` | `Product.Id` | Server-assigned, always present |
| `Name` | `string` | `Product.Name` | |
| `Price` | `decimal` | `Product.Price` | |
| `Quantity` | `int` | `Product.Quantity` | |
| `CategoryId` | `int` | `Product.CategoryId` | No nested `Category` object and no category name (clarified 2026-08-19) |

### ProductRequestDto

Accepted by: `POST /api/product` (create), `PUT /api/product/{id}` (update) — same shape for both.

| Field | Type | Notes |
|---|---|---|
| `Name` | `string` | |
| `Price` | `decimal` | |
| `Quantity` | `int` | |
| `CategoryId` | `int` | References an existing category; referential-integrity checking is unchanged (out of scope — no new validation per feature description) |

## Mapping Rules

- **Entity → Response DTO**: direct 1:1 field copy for the fields listed above; navigation
  properties are simply not copied (there is no field on the DTO to copy them into).
- **Request DTO → Entity**:
  - *Create*: a new entity is constructed from the request DTO's fields; `Id` is left for EF Core /
    the database to assign (unchanged from today's behavior).
  - *Update*: the existing entity (loaded by `Id` from the route) has its fields overwritten from
    the request DTO's fields, mirroring the existing per-field assignment already present in
    `CategoryController.Put` / `ProductController.Put` — just reading from the DTO instead of from
    an `Category`/`Product` entity bound directly from the request body.
- No state transitions apply — both entities are simple CRUD records with no lifecycle/status
  field.
