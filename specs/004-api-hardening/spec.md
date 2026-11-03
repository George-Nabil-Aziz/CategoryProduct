# Feature Specification: API Hardening

**Feature Branch**: `004-api-hardening` (retroactive; built directly on `master`)

**Created**: 2026-11-03

**Status**: Implemented (commits `b17b5ee`, `3149197`)

**Input**: Retroactive spec. Product CategoryId validation, JWT signing key moved out of tracked config, pagination metadata on list endpoints, and async API controllers.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Reject products that point at a missing category (Priority: P1)

An authenticated consumer who creates or updates a Product with a `CategoryId` that does not exist gets a clear validation error instead of a database failure.

**Independent Test**: POST/PUT a product with a non-existent `CategoryId` and confirm a 400 response and no data change.

**Acceptance Scenarios**:

1. **Given** no category with Id X, **When** a product is created with `CategoryId = X`, **Then** the API returns 400 ValidationProblem with a `CategoryId` error "Category does not exist." and nothing is saved.
2. **Given** an existing product, **When** it is updated to a non-existent `CategoryId`, **Then** the same 400 is returned and the product is unchanged.
3. **Given** an existing category, **When** a product is created or updated with it, **Then** behavior is unchanged.

---

### User Story 2 - JWT signing key is never committed (Priority: P1)

The API refuses to start unless a strong JWT signing key is supplied from user-secrets (development) or the `Jwt__Key` environment variable.

**Independent Test**: Start the API with no key configured and confirm startup fails with a message explaining how to set it.

**Acceptance Scenarios**:

1. **Given** `Jwt:Key` is missing, empty, or shorter than 32 characters, **When** the API starts, **Then** startup throws `InvalidOperationException` with instructions (user-secrets / `Jwt__Key`).
2. **Given** a valid key from user-secrets or environment, **When** the API starts, **Then** it runs normally and logins issue tokens.

---

### User Story 3 - Paged list responses with metadata (Priority: P2)

Category and Product list endpoints return a page of items plus paging metadata so clients can build pagers.

**Acceptance Scenarios**:

1. **Given** N items, **When** `GET` list is called with `page` and `pageSize`, **Then** the response is `PagedResponseDto<T>`: `items`, `page`, `pageSize`, `totalCount`, `totalPages`.
2. **Given** `pageSize` above 50 or below 1, **Then** it is clamped to 1..50; `page` below 1 becomes 1.
3. **Given** any page, **Then** items are ordered by `Id` so paging is stable.

---

### User Story 4 - Async data access (Priority: P3)

All API controller actions use async EF Core calls so requests do not block threads. No observable behavior change.

**Acceptance Scenarios**:

1. **Given** any existing endpoint, **When** called, **Then** status codes and response shapes are identical to before (except the paging envelope in Story 3).

### Edge Cases

- `page` beyond the last page returns an empty `items` list with correct `totalCount`/`totalPages`.
- Empty table returns `totalCount = 0`, `totalPages = 0`.
- A key of 32+ characters is accepted; no other strength checks are performed.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Product create and update MUST return 400 ValidationProblem when `CategoryId` does not exist.
- **FR-002**: The API MUST fail fast at startup when `Jwt:Key` is missing or shorter than 32 characters.
- **FR-003**: The JWT signing key MUST NOT be present in tracked configuration (`appsettings.json`).
- **FR-004**: List endpoints MUST return `PagedResponseDto<T>` with `Items`, `Page`, `PageSize`, `TotalCount`, `TotalPages`.
- **FR-005**: `pageSize` MUST be clamped to 1..50 and `page` to a minimum of 1; results MUST be ordered by `Id`.
- **FR-006**: Controller actions MUST use async EF Core operations.

### Key Entities

- **PagedResponseDto&lt;T&gt;**: record of `Items`, `Page`, `PageSize`, `TotalCount`, `TotalPages`.

## Success Criteria *(mandatory)*

- **SC-001**: 100% of product writes with an unknown `CategoryId` return 400 and change no data.
- **SC-002**: The API cannot start without a valid key, and no key value exists in the repository.
- **SC-003**: List responses always carry correct paging metadata and never exceed 50 items.

## Assumptions

- Category deletion rules and Product field validation are unchanged (see 002).
- The list-endpoint response shape change is breaking for list consumers; the MVC app was adapted in 006.
- Paging count queries use `CountAsync()`; all EF Core calls in the API controllers are async.
- Developers must set the key once: `dotnet user-secrets set "Jwt:Key" "<key>"` in `CategoryProduct.API`.
