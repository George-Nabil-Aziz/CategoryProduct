# Feature Specification: Request Validation

**Feature Branch**: `002-request-validation`

**Created**: 2026-08-20

**Status**: Draft

**Input**: User description: "Add request validation to the CategoryProduct.API project.

Scope:
- Validate CategoryRequestDto.
- Validate ProductRequestDto.
- Validation applies to POST and PUT requests only.
- Do not modify Response DTOs.
- Do not add JWT or authentication as part of this feature.
- Follow the existing project architecture and constitution.
- Keep the implementation simple and consistent with the current codebase.

CategoryRequestDto:
- Name is required.
- Name must be between 2 and 100 characters.

ProductRequestDto:
- Name is required.
- Name must be between 2 and 100 characters.
- Price must be greater than or equal to 0.
- Quantity must be greater than or equal to 0.
- CategoryId must be greater than 0.

Do not implement the feature.
Only create/update the specification and stop."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Reject invalid Category submissions (Priority: P1)

An API consumer creates or updates a Category by submitting a name. If the name is missing,
blank, or outside the allowed length, the request is rejected instead of being saved, and the
consumer is told which field was invalid and why.

**Why this priority**: Category is the simpler of the two resources and shares its name rule with
Product, so getting Category validation right first establishes the pattern for everything else.

**Independent Test**: Can be fully tested by submitting Create/Update Category requests with a
missing name, a 1-character name, a 101+-character name, and a valid 2–100 character name, and
confirming only the last one succeeds and is persisted.

**Acceptance Scenarios**:

1. **Given** a new Category request with no name provided, **When** the consumer submits a create
   request, **Then** the request is rejected and no Category is created.
2. **Given** a new Category request with a name of only 1 character, **When** the consumer submits
   a create request, **Then** the request is rejected and no Category is created.
3. **Given** a new Category request with a name of 101 or more characters, **When** the consumer
   submits a create request, **Then** the request is rejected and no Category is created.
4. **Given** a new Category request with a name between 2 and 100 characters, **When** the
   consumer submits a create request, **Then** the Category is created successfully.
5. **Given** an existing Category, **When** the consumer submits an update request with an invalid
   name (missing or out of length range), **Then** the request is rejected and the existing
   Category is left unchanged.

---

### User Story 2 - Reject invalid Product submissions (Priority: P2)

An API consumer creates or updates a Product by submitting a name, price, quantity, and category
reference. If any of these values violate the stated rules (name length, negative price, negative
quantity, or a non-positive category reference), the request is rejected instead of being saved,
and the consumer is told which field(s) were invalid and why.

**Why this priority**: Product has more fields and rules than Category, and depends on the same
validation approach established by User Story 1, so it is built next.

**Independent Test**: Can be fully tested by submitting Create/Update Product requests that each
violate exactly one rule (bad name, negative price, negative quantity, non-positive category id)
plus one fully valid request, and confirming only the valid one succeeds and is persisted.

**Acceptance Scenarios**:

1. **Given** a new Product request with no name or an out-of-range name length, **When** the
   consumer submits a create request, **Then** the request is rejected and no Product is created.
2. **Given** a new Product request with a negative price, **When** the consumer submits a create
   request, **Then** the request is rejected and no Product is created.
3. **Given** a new Product request with a negative quantity, **When** the consumer submits a
   create request, **Then** the request is rejected and no Product is created.
4. **Given** a new Product request with a category reference of 0 or a negative number, **When**
   the consumer submits a create request, **Then** the request is rejected and no Product is
   created.
5. **Given** a new Product request with a valid name, a price of 0 or greater, a quantity of 0 or
   greater, and a category reference greater than 0, **When** the consumer submits a create
   request, **Then** the Product is created successfully.
6. **Given** an existing Product, **When** the consumer submits an update request violating one or
   more of the above rules, **Then** the request is rejected and the existing Product is left
   unchanged.

---

### Edge Cases

- What happens when a Category or Product name is provided but consists only of whitespace? It
  MUST be treated the same as a missing name and rejected.
- What happens when a name is exactly 2 characters or exactly 100 characters? Both are valid
  boundary values and MUST be accepted.
- What happens when Product price or quantity is exactly 0? Both MUST be accepted, since the rule
  is "greater than or equal to 0."
- What happens when a Product request has more than one invalid field at once (e.g., bad name and
  negative price together)? The request MUST be rejected once, and the consumer MUST be informed
  of every field that failed, not just the first one found.
- What happens on GET or DELETE requests with the same underlying resource? Validation introduced
  by this feature MUST NOT apply to GET or DELETE operations.
- What happens to the existing Response DTOs (`CategoryResponseDto`, `ProductResponseDto`)? They
  remain exactly as they are today; this feature only affects data submitted by the consumer.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST reject a Category create (POST) or update (PUT) request when the
  submitted name is missing, empty, or contains only whitespace.
- **FR-002**: System MUST reject a Category create (POST) or update (PUT) request when the
  submitted name is shorter than 2 characters or longer than 100 characters.
- **FR-003**: System MUST reject a Product create (POST) or update (PUT) request when the
  submitted name is missing, empty, contains only whitespace, or is outside the 2–100 character
  range.
- **FR-004**: System MUST reject a Product create (POST) or update (PUT) request when the
  submitted price is less than 0.
- **FR-005**: System MUST reject a Product create (POST) or update (PUT) request when the
  submitted quantity is less than 0.
- **FR-006**: System MUST reject a Product create (POST) or update (PUT) request when the
  submitted category reference is less than or equal to 0.
- **FR-007**: When a request is rejected for failing one or more rules, the system MUST NOT
  persist any data from that request, and MUST return a response identifying every field that
  failed validation and the reason.
- **FR-008**: Validation introduced by this feature MUST apply only to create (POST) and update
  (PUT) operations for Category and Product; read (GET) and delete (DELETE) operations MUST be
  unaffected.
- **FR-009**: The existing Category and Product response data returned to consumers MUST remain
  unchanged by this feature.
- **FR-010**: This feature MUST NOT introduce any authentication or authorization requirement as a
  side effect of adding validation.

### Key Entities

- **Category Request**: Data submitted by a consumer to create or update a Category. Relevant
  attribute: name (required, 2–100 characters).
- **Product Request**: Data submitted by a consumer to create or update a Product. Relevant
  attributes: name (required, 2–100 characters), price (must be zero or greater), quantity (must
  be zero or greater), category reference (must be greater than zero).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of Category or Product create/update requests that violate a stated rule are
  rejected without creating or modifying any stored data.
- **SC-002**: 100% of rejected requests return enough information for the consumer to identify
  every invalid field and the reason, without needing server-side logs.
- **SC-003**: 100% of Category and Product create/update requests that satisfy all stated rules
  continue to succeed exactly as they did before this feature, with no change in behavior for
  compliant consumers.
- **SC-004**: After this feature ships, 0 Category or Product records can be found in storage with
  a blank/whitespace/out-of-range name, a negative price, a negative quantity, or a category
  reference of 0 or less.

## Assumptions

- "Required" for a name means non-blank after trimming whitespace; a whitespace-only value does
  not satisfy the requirement.
- Validating that a Product's category reference is a positive number (greater than 0) is the full
  extent of this feature's category-reference rule; confirming that a Category with that exact
  identifier actually exists is not part of this feature's scope, since it was not requested.
- Read (GET) and delete (DELETE) endpoints for Category and Product are out of scope and receive
  no behavior changes.
- The exact wording and structure of the rejection response is left to the implementation phase, as
  long as it satisfies FR-007 (identifies every failing field and reason) and is consistent with
  standard API error conventions already used elsewhere in the project.
- This feature is scoped to CategoryProduct.API only; CategoryProduct.MVC's own validation (if any)
  is unaffected.
