# Feature Specification: API DTOs for Category & Product

**Feature Branch**: `001-api-dtos`

**Created**: 2026-08-19

**Status**: Draft

**Input**: User description: "Add DTOs to the CategoryProduct API. Replace direct exposure of entity models in API requests and responses with appropriate request and response DTOs. Keep the scope limited to DTOs only; do not add authentication, validation, or other unrelated features."

## Clarifications

### Session 2026-08-19

- Q: When the API returns a product, should the response include the category's name in addition to its id, or only the category id? → A: Category id only — no category name or other denormalized category field in the product response.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Clean, stable read responses (Priority: P1)

As an API consumer, when I request a category or a product (single item or list), I receive a
response that contains only the fields relevant to that resource — not raw internal navigation
data that creates circular or redundant structures.

**Why this priority**: Read endpoints (list and get-by-id) are the most frequently used and are
where the current entity-shaped responses cause the most visible problems: a Category's response
carries its full Products collection, and each of those Products carries its Category back again,
producing bloated and potentially self-referencing payloads.

**Independent Test**: Can be fully tested by calling `GET /api/category`, `GET /api/category/{id}`,
`GET /api/product`, and `GET /api/product/{id}` and confirming each response contains only the
resource's own fields, with no nested navigation collections/objects.

**Acceptance Scenarios**:

1. **Given** categories exist in the system, **When** an API consumer requests the category list or
   a single category by id, **Then** each category in the response contains only its own
   identifying fields and does not include its associated products collection.
2. **Given** products exist in the system, **When** an API consumer requests the product list or a
   single product by id, **Then** each product in the response contains only its own fields
   (including the id of its category) and does not include a nested category object.

---

### User Story 2 - Well-defined write requests (Priority: P2)

As an API consumer, when I create or update a category or a product, I submit only the fields I am
allowed to control — the server, not my request body, decides the resource's identifier.

**Why this priority**: Today, create and update endpoints accept the full entity shape, meaning a
client could technically submit an `Id` (or other server-owned data) on a create request. Removing
that ambiguity from the request shape is the second most valuable change after fixing read
responses, since it clarifies the contract for anyone building against the API.

**Independent Test**: Can be fully tested by submitting `POST`/`PUT` requests to
`/api/category` and `/api/product` with only the intended fields, and confirming the created or
updated resource is persisted correctly and that any `Id` supplied in the request body has no
effect on the resulting resource's identifier.

**Acceptance Scenarios**:

1. **Given** no prior category exists, **When** an API consumer submits a create request containing
   only a category name, **Then** a new category is created and the server assigns its identifier.
2. **Given** an existing product, **When** an API consumer submits an update request containing the
   product's name, price, quantity, and category id, **Then** the product is updated with those
   values regardless of any other field the client may have attempted to send.

---

### User Story 3 - Consistent shape for related-data listing (Priority: P3)

As an API consumer, when I list the products that belong to a specific category, I receive the same
clean product representation used by the main product endpoints, not the raw entity shape.

**Why this priority**: This endpoint is a secondary, convenience listing (products scoped to a
category) rather than a primary resource endpoint, so it is lower priority than the core read/write
paths, but it must stay consistent with them.

**Independent Test**: Can be fully tested by calling `GET /api/category/{id}/products` and
confirming the returned products match the same representation used by `GET /api/product`.

**Acceptance Scenarios**:

1. **Given** a category with associated products, **When** an API consumer requests that category's
   products, **Then** each returned product uses the same response representation as the standalone
   product endpoints (no nested category navigation object).

### Edge Cases

- What happens when a client includes an `Id` field in a create request body? The server MUST
  ignore it and assign the identifier itself.
- What happens when serializing a category that has many associated products? The response MUST
  include only the category's own fields, so the previously possible circular
  category→products→category structure cannot occur.
- What happens when a client submits a create or update request with fields that no longer exist on
  the request shape (e.g., a nested category object on a product request)? The server MUST ignore
  unrecognized fields and process only the fields defined for that request.
- How is a product's category represented in a product response when the category has since been
  deleted or reassigned? The response MUST still include the product's category id; it MUST NOT
  include a nested category object in any case.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The API MUST return category data in list and get-by-id responses using a
  representation limited to the category's own fields (identifier and name), excluding its
  associated products collection.
- **FR-002**: The API MUST return product data in list and get-by-id responses using a
  representation limited to the product's own fields (identifier, name, price, quantity, and
  category id), excluding a nested category object and excluding any denormalized category field
  (e.g., category name); consumers needing category details make a separate category request.
- **FR-003**: The API MUST accept category create requests using a representation that contains
  only the category name; the server MUST assign the identifier.
- **FR-004**: The API MUST accept category update requests using a representation that contains
  only the category name.
- **FR-005**: The API MUST accept product create requests using a representation that contains
  name, price, quantity, and category id; the server MUST assign the identifier.
- **FR-006**: The API MUST accept product update requests using a representation that contains
  name, price, quantity, and category id.
- **FR-007**: The API MUST return the products-by-category listing using the same product response
  representation defined in FR-002.
- **FR-008**: The API MUST NOT expose entity navigation properties (category's products collection,
  product's category object) in any request or response body.
- **FR-009**: Existing endpoint routes, HTTP methods, and status codes (e.g., `Ok`, `NotFound`) MUST
  remain unchanged; only the request and response body shapes change.
- **FR-010**: The underlying data model and database schema MUST NOT change as part of this
  feature; only the API-facing request/response contract changes.

### Key Entities *(include if feature involves data)*

- **Category**: A named grouping of products. API-facing representation: identifier, name.
- **Product**: An item belonging to a category. API-facing representation: identifier, name,
  price, quantity, category id.
- **Request representation**: The shape of data an API consumer submits to create or update a
  Category or Product; excludes server-assigned fields (identifier) and excludes navigation data.
- **Response representation**: The shape of data returned to an API consumer for a Category or
  Product; excludes navigation data that would otherwise duplicate or circularly reference related
  resources.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of category and product read endpoints (list, get-by-id, and the
  products-by-category listing) return responses containing no navigation-based circular or
  duplicated resource data.
- **SC-002**: An API consumer can create a category or product by submitting only the fields
  relevant to that action, without needing to know or supply any server-assigned identifier.
- **SC-003**: Submitting a server-assigned identifier in a create request body has zero effect on
  the identifier of the resulting resource in 100% of cases.
- **SC-004**: All existing category and product endpoints continue to respond with the same routes,
  HTTP methods, and status codes as before this change, verified by exercising each endpoint.

## Assumptions

- Only the `CategoryController` and `ProductController` endpoints in `CategoryProduct.API` are in
  scope. `AccountController` currently exposes no endpoints, so no request/response representation
  for `User` is introduced by this feature.
- This feature is a request/response contract change only: no new endpoints, fields, validation
  rules, or authentication are introduced, per the feature description's explicit scope limit.
- `CategoryProduct.MVC` is unaffected, since it does not consume `CategoryProduct.API` and maintains
  its own independent data access.
- The field sets for each representation are based on the current entity properties: Category
  (name) and Product (name, price, quantity, category id); identifiers are server-assigned and
  therefore included only in responses, never required in create requests.
- Existing route patterns, HTTP verbs, and status-code behavior are preserved; only request and
  response body shapes change.
