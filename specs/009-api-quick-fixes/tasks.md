---

description: "Task list for API Quick Fixes (retroactive; all delivered)"
---

# Tasks: API Quick Fixes

**Input**: Design documents from `/specs/009-api-quick-fixes/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Automated tests exist for US1 and US2. US3/US4 and the MVC messages are manual.

**Organization**: By user story. Commits: US4 `59e6836`, US3 `94b68a8`, US1 `d643b40`, US2 `89d69df`.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story the task belongs to

## Path Conventions

`CategoryProduct.API/`, `CategoryProduct.API.Tests/`, `CategoryProduct.MVC/` at repository root.

---

## Phase 1: Setup

- [X] T001 [US4] Add `System.Security.Cryptography.Pkcs` 10.0.14 `PackageReference` in `CategoryProduct.API/CategoryProduct.API.csproj` (`59e6836`)

## Phase 2: Foundational

*No shared prerequisites; the stories are independent.*

---

## Phase 3: User Story 3 - Swagger Authorize button (Priority: P2)

**Goal**: Paste a JWT once in Swagger UI.

**Independent Test**: Manual (quickstart step 2).

- [X] T002 [US3] Add `Bearer` HTTP/JWT security definition and global security requirement inside `AddSwaggerGen` in `CategoryProduct.API/Program.cs` (`94b68a8`)

## Phase 4: User Story 1 - Block deleting categories with products (Priority: P1)

**Goal**: 409 on delete of a non-empty category, surfaced in MVC.

**Independent Test**: `DeleteCategory_WithProducts_Returns409`.

- [X] T003 [P] [US1] Add test `DeleteCategory_WithProducts_Returns409` and `SendAsync` helper in `CategoryProduct.API.Tests/ApiTests.cs`
- [X] T004 [US1] Add `AnyAsync` guard returning `Problem(statusCode: 409, ...)` in `CategoryProduct.API/Controllers/CategoryController.cs`
- [X] T005 [P] [US1] Add `Conflict` property to `ApiResult` in `CategoryProduct.MVC/Models/ApiModels.cs`
- [X] T006 [US1] Handle `409` (with `ConflictProblem` record) in `CategoryProduct.MVC/Services/CategoryProductApiClient.cs`
- [X] T007 [US1] Show `result.Conflict` in `AddErrors` in `CategoryProduct.MVC/Controllers/CategoriesController.cs`

## Phase 5: User Story 2 - Login rate limiting (Priority: P1)

**Goal**: Fixed-window per-IP limiter on login, 429 message in MVC.

**Independent Test**: `Login_ExceedingLimit_Returns429`.

- [X] T008 [P] [US2] Add `LoginPermitLimit` (default 1000) to `CategoryProduct.API.Tests/ApiFactory.cs` and set `RateLimiting:LoginPermitLimit`
- [X] T009 [P] [US2] Add `RateLimitTests` with `Login_ExceedingLimit_Returns429` (limit 2) in `CategoryProduct.API.Tests/ApiTests.cs`
- [X] T010 [US2] Register `AddRateLimiter` policy `login` and `app.UseRateLimiter()` in `CategoryProduct.API/Program.cs`
- [X] T011 [US2] Add `[EnableRateLimiting("login")]` to `Login` in `CategoryProduct.API/Controllers/AccountController.cs`
- [X] T012 [US2] Add `LoginResult.TooManyRequests` sentinel and 429 branch in `CategoryProduct.MVC/Services/CategoryProductApiClient.cs`
- [X] T013 [US2] Show "Too many login attempts. Try again in a minute." in `CategoryProduct.MVC/Controllers/AccountController.cs`

## Phase 6: Polish

- [X] T014 Document delivered work in `specs/009-api-quick-fixes/` (this folder)

## Dependencies & Execution Order

- T006 depends on T005; T007 depends on T005/T006.
- T010 before T011 (policy must exist); T008 before T009.
- User stories US1-US4 are independent of each other.
