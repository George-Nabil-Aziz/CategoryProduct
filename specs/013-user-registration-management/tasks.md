---

description: "Task list for User Registration & Management (retroactive; all delivered in commit 025d652)"
---

# Tasks: User Registration & Management

**Input**: Design documents from `/specs/013-user-registration-management/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: API tests included; MVC has no automated tests.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 register, US2 list users, US3 change role, US4 MVC pages

## Phase 1: Setup (Shared Infrastructure)

- [X] T001 Add `RateLimiting:RegisterPermitLimit` (default 5) and the `register` fixed-window policy in `CategoryProduct.API/Program.cs`
- [X] T002 [P] Create `CategoryProduct.API/Dtos/UserResponseDto.cs`
- [X] T003 [P] Create `CategoryProduct.API/Dtos/RegisterRequestDto.cs`
- [X] T004 [P] Create `CategoryProduct.API/Dtos/UpdateRoleRequestDto.cs`
- [X] T005 Verify no migration is needed: `IX_Users_UserName` in `CategoryProduct.API/Migrations/20260820083821_UpdateUsersTableForAuth.cs`, `Role` column in `CategoryProduct.API/Migrations/20261103173834_AddUserRole.cs`

## Phase 2: User Story 1 - Self-service registration (Priority: P1)

- [X] T006 [US1] Add `Register` action (`[EnableRateLimiting("register")]`, trim, case-insensitive duplicate check, hash, `409` on `DbUpdateException`, `201`) in `CategoryProduct.API/Controllers/AccountController.cs`
- [X] T007 [P] [US1] Set `RateLimiting:RegisterPermitLimit` to 1000 in `CategoryProduct.API.Tests/ApiFactory.cs`
- [X] T008 [P] [US1] Tests `Register_Succeeds_And_CanLogin`, `Register_DuplicateDifferentCase_Returns409`, `Register_ShortPassword_Returns400` in `CategoryProduct.API.Tests/UsersTests.cs`

## Phase 3: User Story 2 - Admin lists users (Priority: P2)

- [X] T009 [US2] Create `CategoryProduct.API/Controllers/UsersController.cs` with `[Authorize(Roles = "Admin")]` and paged/searchable `GetUsers`
- [X] T010 [P] [US2] Tests `Users_NonAdmin_Returns403`, `Users_Admin_ListsWithoutPasswordHash` in `CategoryProduct.API.Tests/UsersTests.cs`

## Phase 4: User Story 3 - Change role with last-admin protection (Priority: P2)

- [X] T011 [US3] Add `UpdateRole` (`400`/`404`/`409`/`200`) in `CategoryProduct.API/Controllers/UsersController.cs`
- [X] T012 [P] [US3] Tests `Admin_CanPromoteUser`, `LastAdminTests.DemotingLastAdmin_Returns409` in `CategoryProduct.API.Tests/UsersTests.cs`

## Phase 5: User Story 4 - MVC pages (Priority: P3)

- [X] T013 [P] [US4] Create `CategoryProduct.MVC/Models/RegisterViewModel.cs` (with `UserItem`)
- [X] T014 [P] [US4] Add `TooManyRequests` to `ApiResult` in `CategoryProduct.MVC/Models/ApiModels.cs`
- [X] T015 [US4] Add `RegisterAsync`, `GetUsersAsync`, `ChangeUserRoleAsync` and `429` mapping in `CategoryProduct.MVC/Services/CategoryProductApiClient.cs`
- [X] T016 [US4] Add `Register` GET/POST (auto sign-in after register) in `CategoryProduct.MVC/Controllers/AccountController.cs`
- [X] T017 [US4] Create `CategoryProduct.MVC/Controllers/UsersController.cs` (`Index`, `ChangeRole`)
- [X] T018 [P] [US4] Create `CategoryProduct.MVC/Views/Account/Register.cshtml`
- [X] T019 [P] [US4] Create `CategoryProduct.MVC/Views/Users/Index.cshtml`
- [X] T020 [P] [US4] Add Register link in `CategoryProduct.MVC/Views/Account/Login.cshtml` and Users/Register nav links in `CategoryProduct.MVC/Views/Shared/_Layout.cshtml`

## Dependencies & Execution Order

T001-T005 first; US1-US3 depend on T002-T004; US4 depends on the API endpoints existing. T008, T010,
T012 share one file with each other but cover different tests.

## Outstanding (not done)

- Manual browser verification of the MVC Register and Users pages.
