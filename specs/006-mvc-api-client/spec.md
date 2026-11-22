# Feature Specification: MVC Consumes the API

**Feature Branch**: `006-mvc-api-client` (retroactive; built directly on `master`)

**Created**: 2026-11-03

**Status**: Implemented (commit `c09170f`)

**Input**: Retroactive spec. `CategoryProduct.MVC` stops using EF Core directly and reads and writes Category/Product data through `CategoryProduct.API` over HTTP.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - MVC pages work through the API (Priority: P1)

A user browsing the MVC site sees category and product lists and details as before, and the create/edit/delete forms send their data to the API instead of a local database.

**Why this priority**: The MVC site must keep working once it no longer owns a database.

**Independent Test**: With the API running, open the MVC site and exercise list and details for both entities, and submit a create/edit/delete form.

**Acceptance Scenarios**:

1. **Given** the API is running, **When** a user opens Categories or Products Index, **Then** the list is fetched from the API's paged endpoints (5 per page by default) and shown with a pager.
2. **Given** valid input, **When** a user creates or edits an item, **Then** it is sent to the API and, on success, the user lands back on the Index list. At this commit MVC attaches no token, so while the API requires authentication (003) a write is rejected with 401 and the form shows "You must be signed in to make changes."; successful writes become possible with sign-in in 007.
3. **Given** an API validation error (400), **When** a form is submitted, **Then** the user sees the error on the form instead of a crash.
4. **Given** an item that does not exist (API 404), **When** its details, edit or delete page is opened, **Then** MVC returns 404 Not Found.

---

### User Story 2 - MVC has no database access (Priority: P1)

The MVC project has no EF Core, no `DbContext`, and no connection string; the API is the only component that touches the database.

**Why this priority**: Constitution Principle V (API/MVC separation).

**Independent Test**: Inspect `CategoryProduct.MVC.csproj`, `appsettings.json` and the `Data/` folder.

**Acceptance Scenarios**:

1. **Given** the MVC project, **When** its references and config are inspected, **Then** no EF Core package, `ApplicationDbContext`, or connection string remains.
2. **Given** only `Api:BaseUrl` is configured, **When** MVC starts, **Then** it works against that API.

### Edge Cases

- API unreachable: the MVC call fails with an unhandled `HttpRequestException` (no friendly offline page yet; known gap).
- Writes made without a signed-in user are rejected by the API with 401, which MVC shows as the form error above; sign-in is handled in 007.
- A 403 from the API is treated the same as 401 at this commit (both set `ApiResult.Unauthorized`); roles arrive in 008.
- The category product-count column was removed from the Categories Index, because the API's category DTO does not supply product counts. The Category Delete confirmation also no longer lists products.
- Any other non-success status from a write (for example 500) is not handled and surfaces as an exception.
- The product's category name is resolved by MVC from the API (all categories fetched at 50 per page for the Products Index; one category lookup for Details and Delete).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: MVC MUST call the API through a typed `HttpClient` wrapper, `CategoryProductApiClient`, with the base address read from `Api:BaseUrl`.
- **FR-002**: MVC MUST NOT reference EF Core or hold a database connection string.
- **FR-003**: MVC models MUST be plain view/API models (`Models/ApiModels.cs`, simplified `Category` and `Product`) with no navigation to a database context.
- **FR-004**: MVC controllers MUST use the client for all list, get, create, update, and delete operations.
- **FR-005**: The client MUST expose a single place (`ApplyBearerToken`) where a bearer token can be attached to outgoing requests (wired to real tokens in 007).
- **FR-006**: API 400 validation errors MUST be shown as model errors on the form fields they name.

## Success Criteria *(mandatory)*

- **SC-001**: 100% of MVC data operations go through the API; the MVC project builds with no EF Core dependency.
- **SC-002**: All existing MVC pages remain reachable and functional, except the removed product-count column.

## Assumptions

- The API is already running and reachable at `Api:BaseUrl` (default `https://localhost:7183/`).
- The API's paged list contract (004) is the source for index pages.
- No automated tests cover the MVC app (known gap); verification is manual.

## Later changes

`aebb0fc` added search/filter/sort to the MVC lists, `d643b40` surfaced the API's 409 on deleting a category with products, `f47b61e` added a friendly service-unavailable page for API failures, and `277a46b` added token refresh in the MVC client. See the newer specs 009-014.
