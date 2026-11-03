---

description: "Retroactive task list for API integration tests"
---

# Tasks: API Integration Tests

**Input**: Design documents from `/specs/005-api-integration-tests/`

## Format: `[ID] [P?] [Story] Description`

## Phase 1: Setup

- [X] T001 Create `CategoryProduct.API.Tests/CategoryProduct.API.Tests.csproj` (xUnit, Mvc.Testing, EF InMemory, reference to API)
- [X] T002 Add the project to `CategoryProduct.slnx`
- [X] T003 Create `CategoryProduct.API.Tests/ApiFactory.cs` (InMemory DB, test key, seeded user and categories)

## Phase 2: Tests (US1)

- [X] T004 [US1] `Login_CorrectCredentials_ReturnsToken` in `CategoryProduct.API.Tests/ApiTests.cs`
- [X] T005 [US1] `Login_WrongPassword_Returns401` in `CategoryProduct.API.Tests/ApiTests.cs`
- [X] T006 [US1] `PostCategory_WithoutToken_Returns401` in `CategoryProduct.API.Tests/ApiTests.cs`
- [X] T007 [US1] `GetCategories_PageSizeAbove50_IsClamped` in `CategoryProduct.API.Tests/ApiTests.cs`
- [X] T008 [US1] `PostProduct_NonExistentCategory_Returns400` in `CategoryProduct.API.Tests/ApiTests.cs`

## Phase 3: Roles (see 008)

- [X] T009 Seed Admin and plain users in `CategoryProduct.API.Tests/ApiFactory.cs`
- [X] T010 `PostCategory_NonAdminToken_Returns403` in `CategoryProduct.API.Tests/ApiTests.cs`
