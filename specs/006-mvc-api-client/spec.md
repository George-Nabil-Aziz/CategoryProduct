# Feature Specification: MVC Consumes the API

**Feature Branch**: `006-mvc-api-client` (retroactive; built directly on `master`)

**Created**: 2026-11-03

**Status**: Implemented (commit `c09170f`)

**Input**: Retroactive spec. `CategoryProduct.MVC` stops using EF Core directly and reads and writes Category/Product data through `CategoryProduct.API` over HTTP.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - MVC pages work through the API (Priority: P1)

A user browsing the MVC site sees, creates, edits, and deletes categories and products exactly as before, but the data now flows through the API.

**Independent Test**: With the API running, open the MVC site and exercise list, details, create, edit, and delete for both entities.

**Acceptance Scenarios**:

1. **Given** the API is running, **When** a user opens Categories or Products Index, **Then** the list is fetched from the API's paged endpoints and shown.
2. **Given** valid input, **When** a user creates or edits an item, **Then** it is sent to the API and the user lands back on the list.
3. **Given** an API validation error (400), **When** a form is submitted, **Then** the user sees the error on the form instead of a crash.

---

### User Story 2 - MVC has no database access (Priority: P1)

The MVC project has no EF Core, no `DbContext`, and no connection string; the API is the only component that touches the database.

**Acceptance Scenarios**:

1. **Given** the MVC project, **When** its references and config are inspected, **Then** no EF Core package, `ApplicationDbContext`, or connection string remains.
2. **Given** only `Api:BaseUrl` is configured, **When** MVC starts, **Then** it works against that API.

### Edge Cases

- API unreachable: the MVC call fails with an HTTP error (no friendly offline page yet; known gap).
- Writes made without a signed-in user are rejected by the API (401); sign-in is handled in 007.
- The category product-count column was removed from the Categories Index, because the API's category DTO does not supply product counts.
- Delete confirmation pages show API data (Category, Product Delete views adjusted).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: MVC MUST call the API through a typed `HttpClient` wrapper, `CategoryProductApiClient`, with the base address read from `Api:BaseUrl`.
- **FR-002**: MVC MUST NOT reference EF Core or hold a database connection string.
- **FR-003**: MVC models MUST be plain view/API models (`Models/ApiModels.cs`, simplified `Category` and `Product`) with no navigation to a database context.
- **FR-004**: MVC controllers MUST use the client for all list, get, create, update, and delete operations.
- **FR-005**: The client MUST expose a single place where a bearer token is attached to outgoing requests (wired to real tokens in 007).

## Success Criteria *(mandatory)*

- **SC-001**: 100% of MVC data operations go through the API; the MVC project builds with no EF Core dependency.
- **SC-002**: All existing MVC pages remain reachable and functional, except the removed product-count column.

## Assumptions

- The API is already running and reachable at `Api:BaseUrl` (default `https://localhost:7183/`).
- The API's paged list contract (004) is the source for index pages.
- No automated tests cover the MVC app (known gap); verification is manual.
