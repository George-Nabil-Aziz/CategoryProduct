---

description: "Retroactive task list for role-based authorization"
---

# Tasks: Role-Based Authorization

**Input**: Design documents from `/specs/008-role-authorization/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: One automated API test (`PostCategory_NonAdminToken_Returns403`, see T008); MVC verified manually per `quickstart.md`.

**Organization**: Data foundation first, then US1 (API), then US2 (MVC). Delivered in commit `f868aa7`.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story the task belongs to (US1, US2)

## Path Conventions

Existing projects `CategoryProduct.API/`, `CategoryProduct.API.Tests/` and `CategoryProduct.MVC/` at repository root.

---

## Phase 1: Foundational - Data (blocks both stories)

**Purpose**: Users must have a role before it can be put in a token

- [X] T001 Add `Role` (default `"User"`) to `CategoryProduct.API/Models/User.cs`
- [X] T002 Configure `Role` (required, max length 20, default `"User"`) in `CategoryProduct.API/Data/ApplicationDbContext.cs` (depends on T001)
- [X] T003 Add migration `CategoryProduct.API/Migrations/20261103173834_AddUserRole.cs` (with designer and snapshot update; adds column, promotes `testuser` to Admin); apply with `dotnet ef database update --project CategoryProduct.API` (depends on T001, T002)

**Checkpoint**: `Users.Role` exists in the database.

---

## Phase 2: User Story 1 - Only Admins can write through the API (Priority: P1)

**Goal**: Admin tokens can write, other valid tokens get 403, reads stay public.

**Independent Test**: Call a write endpoint with an Admin and a non-Admin token.

- [X] T004 [US1] Add role claim to JWT in `CategoryProduct.API/Controllers/AccountController.cs` (depends on T005 for the DTO shape)
- [X] T005 [P] [US1] Add `Role` to `CategoryProduct.API/Dtos/LoginResponseDto.cs`
- [X] T006 [P] [US1] `[Authorize(Roles = "Admin")]` on writes in `CategoryProduct.API/Controllers/CategoryController.cs`
- [X] T007 [P] [US1] Same in `CategoryProduct.API/Controllers/ProductController.cs`
- [X] T008 [US1] Seed Admin (`tester`) and plain (`plainuser`) users in `CategoryProduct.API.Tests/ApiFactory.cs` and add `PostCategory_NonAdminToken_Returns403` in `CategoryProduct.API.Tests/ApiTests.cs`

**Checkpoint**: `dotnet test CategoryProduct.API.Tests` passes with 6 tests.

---

## Phase 3: User Story 2 - MVC reflects the role (Priority: P1)

**Goal**: MVC offers writes only to Admins and explains refusals.

**Independent Test**: Sign in as Admin and as non-Admin and compare pages (see `quickstart.md`).

- [X] T009 [US2] Read `Role` in `CategoryProduct.MVC/Services/CategoryProductApiClient.cs` (`LoginResult.Role`, 403 mapped to `Forbidden`) and add `Forbidden` to `CategoryProduct.MVC/Models/ApiModels.cs` (depends on T005)
- [X] T010 [US2] Store role claim and add `AccessDenied` action in `CategoryProduct.MVC/Controllers/AccountController.cs` (depends on T009)
- [X] T011 [US2] Set `AccessDeniedPath` in `CategoryProduct.MVC/Program.cs`
- [X] T012 [US2] Admin-only writes and 403 form error in `CategoryProduct.MVC/Controllers/CategoriesController.cs` and `CategoryProduct.MVC/Controllers/ProductsController.cs` (depends on T009)
- [X] T013 [P] [US2] Create `CategoryProduct.MVC/Views/Account/AccessDenied.cshtml`
- [X] T014 [P] [US2] Hide write links in `CategoryProduct.MVC/Views/Categories/Index.cshtml`, `CategoryProduct.MVC/Views/Categories/Details.cshtml`, `CategoryProduct.MVC/Views/Products/Index.cshtml`, `CategoryProduct.MVC/Views/Products/Details.cshtml`

**Checkpoint**: Both stories work; Admin flows unchanged.

---

## Dependencies & Execution Order

- Phase 1 blocks both stories (T001 -> T002 -> T003).
- US1: T005, T006, T007 are parallel (different files); T004 uses the new DTO constructor so follows T005; T008 follows T001-T007.
- US2 depends on US1's login response (T005) for `Role`; T013 and T014 are parallel with each other and with T011.

## Notes

- [P] tasks = different files, no dependencies.
- Deployment: run the migration before starting the new API build, or login fails.
- Roles are changed in the database only; no UI at this commit.
