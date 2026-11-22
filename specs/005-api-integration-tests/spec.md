# Feature Specification: API Integration Tests

**Feature Branch**: `005-api-integration-tests` (retroactive; built directly on `master`)

**Created**: 2026-11-03

**Status**: Implemented (commit `b17b5ee` delivered 5 tests; a 6th was added in `f868aa7` with feature 008)

**Input**: Retroactive spec. An automated xUnit integration test project for the API.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Run API behavior checks automatically (Priority: P1)

A developer runs `dotnet test` and gets a pass/fail result for the API's core security and validation behavior, with no SQL Server needed.

**Why this priority**: Until this feature, the constitution's Development Workflow relied on manual Swagger checks only.

**Independent Test**: Run `dotnet test` on `CategoryProduct.API.Tests`; all tests pass.

**Acceptance Scenarios**:

1. **Given** correct credentials, **When** logging in, **Then** a token is returned (`Login_CorrectCredentials_ReturnsToken`).
2. **Given** a wrong password, **Then** 401 (`Login_WrongPassword_Returns401`).
3. **Given** no token, **When** `POST /api/category`, **Then** 401 (`PostCategory_WithoutToken_Returns401`).
4. **Given** a non-Admin token, **When** `POST /api/category`, **Then** 403 (`PostCategory_NonAdminToken_Returns403`, added in 008 / commit `f868aa7`).
5. **Given** `pageSize` above 50, **Then** it is clamped to 50 and `totalCount` equals the seeded count (`GetCategories_PageSizeAbove50_IsClamped`).
6. **Given** a valid token and a non-existent `CategoryId`, **When** creating a product, **Then** 400 (`PostProduct_NonExistentCategory_Returns400`).

### Edge Cases

- All tests in `ApiTests` share one in-memory database via `IClassFixture<ApiFactory>`; the tests do not depend on each other's writes.
- The factory supplies its own JWT key and connection-string placeholder so the fail-fast key check (004) passes and no SQL Server connection is attempted.
- The factory builds a separate in-memory database name (a new GUID) per factory instance, so test runs do not leak data into each other.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A test project `CategoryProduct.API.Tests` MUST be part of `CategoryProduct.slnx`.
- **FR-002**: Tests MUST host the API in memory with `WebApplicationFactory<Program>` and replace SQL Server with EF Core InMemory.
- **FR-003**: The factory MUST seed a known number of categories (3) and a login user with a hashed password; from 008 it seeds an Admin user (`tester`) and a plain user (`plainuser`).
- **FR-004**: Tests MUST cover login success/failure, write protection (401, and 403 from 008), pagination clamp, and CategoryId validation.

## Success Criteria *(mandatory)*

- **SC-001**: `dotnet test` runs 5 tests (6 from `f868aa7`) with no external database and no committed secrets.

## Assumptions

- Scope is the API only. The MVC app and the browser login-to-write flow are NOT covered by automated tests (known gap).
- The InMemory provider does not enforce relational constraints (unique index, FK), so those are not tested here.
- The test password and JWT key are test-only constants inside the test project, not real credentials.

## Later changes

Later commits grew the suite (`d643b40` category delete block, `89d69df` login rate limit, `aebb0fc` search/filter, `025d652` registration and users, `277a46b` refresh tokens, `f47b61e` error handling) with new test classes; these are described in the newer specs 009-014.
