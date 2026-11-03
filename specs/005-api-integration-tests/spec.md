# Feature Specification: API Integration Tests

**Feature Branch**: `005-api-integration-tests` (retroactive; built directly on `master`)

**Created**: 2026-11-03

**Status**: Implemented (commit `b17b5ee`; extended to 6 tests in `f868aa7`)

**Input**: Retroactive spec. An automated xUnit integration test project for the API.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Run API behavior checks automatically (Priority: P1)

A developer runs `dotnet test` and gets a pass/fail result for the API's core security and validation behavior, with no SQL Server needed.

**Independent Test**: Run `dotnet test` on `CategoryProduct.API.Tests`; all tests pass.

**Acceptance Scenarios**:

1. **Given** correct credentials, **When** logging in, **Then** a token is returned (`Login_CorrectCredentials_ReturnsToken`).
2. **Given** a wrong password, **Then** 401 (`Login_WrongPassword_Returns401`).
3. **Given** no token, **When** `POST /api/category`, **Then** 401 (`PostCategory_WithoutToken_Returns401`).
4. **Given** a non-Admin token, **When** `POST /api/category`, **Then** 403 (`PostCategory_NonAdminToken_Returns403`, added in 008).
5. **Given** `pageSize` above 50, **Then** it is clamped and `totalCount` equals the seeded count (`GetCategories_PageSizeAbove50_IsClamped`).
6. **Given** a non-existent `CategoryId`, **When** creating a product, **Then** 400 (`PostProduct_NonExistentCategory_Returns400`).

### Edge Cases

- All tests share one in-memory database via `IClassFixture<ApiFactory>`; tests must not depend on each other's writes.
- The factory supplies its own JWT key so the fail-fast check (004) passes.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A test project `CategoryProduct.API.Tests` MUST be part of `CategoryProduct.slnx`.
- **FR-002**: Tests MUST host the API in memory with `WebApplicationFactory<Program>` and replace SQL Server with EF Core InMemory.
- **FR-003**: The factory MUST seed an Admin user, a plain user, and a known number of categories.
- **FR-004**: Tests MUST cover login success/failure, write protection (401, 403), pagination clamp, and CategoryId validation.

## Success Criteria *(mandatory)*

- **SC-001**: `dotnet test` runs 6 tests with no external database and no committed secrets.

## Assumptions

- Scope is the API only. The MVC app and the browser login-to-write flow are NOT covered by automated tests (known gap).
- The InMemory provider does not enforce relational constraints (unique index, FK), so those are not tested here.
- Test password and key are test-only constants inside the test project.
