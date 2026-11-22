---

description: "Retroactive task list for API integration tests"
---

# Tasks: API Integration Tests

**Input**: Design documents from `/specs/005-api-integration-tests/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: This feature IS the test suite. Delivered in `b17b5ee` (T001-T008) and `f868aa7` (T009-T010).

**Organization**: One user story (US1).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story the task belongs to

## Path Conventions

New project `CategoryProduct.API.Tests/` at repository root, referencing `CategoryProduct.API/`.

---

## Phase 1: Setup

**Purpose**: Create the test project and make the API hostable

- [X] T001 Create `CategoryProduct.API.Tests/CategoryProduct.API.Tests.csproj` (xUnit, Mvc.Testing, EF InMemory, reference to API)
- [X] T002 [P] Add the project to `CategoryProduct.slnx`
- [X] T003 Create `CategoryProduct.API.Tests/ApiFactory.cs` (InMemory DB, test key, seeded user and categories; requires `public partial class Program { }` from 004 in `CategoryProduct.API/Program.cs`)

**Checkpoint**: `dotnet test` discovers the project and the host starts.

---

## Phase 2: User Story 1 - Automated API behavior checks (Priority: P1)

**Goal**: A developer gets pass/fail on login, write protection, paging and validation with no SQL Server.

**Independent Test**: `dotnet test CategoryProduct.API.Tests` passes.

- [X] T004 [US1] `Login_CorrectCredentials_ReturnsToken` in `CategoryProduct.API.Tests/ApiTests.cs`
- [X] T005 [US1] `Login_WrongPassword_Returns401` in `CategoryProduct.API.Tests/ApiTests.cs`
- [X] T006 [US1] `PostCategory_WithoutToken_Returns401` in `CategoryProduct.API.Tests/ApiTests.cs`
- [X] T007 [US1] `GetCategories_PageSizeAbove50_IsClamped` in `CategoryProduct.API.Tests/ApiTests.cs`
- [X] T008 [US1] `PostProduct_NonExistentCategory_Returns400` in `CategoryProduct.API.Tests/ApiTests.cs`

**Checkpoint**: 5 tests pass (state at `b17b5ee`).

---

## Phase 3: Roles (US1, delivered with 008 in `f868aa7`)

- [X] T009 [US1] Seed Admin (`tester`) and plain (`plainuser`) users in `CategoryProduct.API.Tests/ApiFactory.cs`
- [X] T010 [US1] `PostCategory_NonAdminToken_Returns403` in `CategoryProduct.API.Tests/ApiTests.cs` (same file as T004-T008, depends on T009)

**Checkpoint**: 6 tests pass.

---

## Dependencies & Execution Order

- T003 depends on T001 and on the `Program` partial class from 004 (T003 of that feature).
- T002 can run alongside T003 (different files).
- T004-T008 all edit `ApiTests.cs` and depend on T003; they are sequential edits to one file, not parallel.
- T009 precedes T010.

## Notes

- Tests share one in-memory database per class; keep write tests independent of read-count assertions.
- Later test classes (`ErrorHandlingTests`, `UsersTests`, `RefreshTokenTests`) belong to later features.
