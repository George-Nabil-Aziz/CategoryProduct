---

description: "Retroactive task list for role-based authorization"
---

# Tasks: Role-Based Authorization

**Input**: Design documents from `/specs/008-role-authorization/`

## Format: `[ID] [P?] [Story] Description`

## Phase 1: Data

- [X] T001 Add `Role` to `CategoryProduct.API/Models/User.cs`
- [X] T002 Configure `Role` in `CategoryProduct.API/Data/ApplicationDbContext.cs`
- [X] T003 Add migration `CategoryProduct.API/Migrations/20261103173834_AddUserRole.cs` (testuser to Admin); apply with `dotnet ef database update --project CategoryProduct.API`

## Phase 2: API (US1)

- [X] T004 [US1] Add role claim to JWT in `CategoryProduct.API/Controllers/AccountController.cs`
- [X] T005 [P] [US1] Add `Role` to `CategoryProduct.API/Dtos/LoginResponseDto.cs`
- [X] T006 [US1] `[Authorize(Roles = "Admin")]` on writes in `CategoryProduct.API/Controllers/CategoryController.cs`
- [X] T007 [P] [US1] Same in `CategoryProduct.API/Controllers/ProductController.cs`
- [X] T008 [US1] Seed plain user in `CategoryProduct.API.Tests/ApiFactory.cs` and add `PostCategory_NonAdminToken_Returns403` in `CategoryProduct.API.Tests/ApiTests.cs`

## Phase 3: MVC (US2)

- [X] T009 [US2] Read `Role` in `CategoryProduct.MVC/Models/ApiModels.cs` and `CategoryProduct.MVC/Services/CategoryProductApiClient.cs`
- [X] T010 [US2] Store role claim and add `AccessDenied` action in `CategoryProduct.MVC/Controllers/AccountController.cs`
- [X] T011 [US2] Set `AccessDeniedPath` in `CategoryProduct.MVC/Program.cs`
- [X] T012 [US2] Admin-only writes and 403 handling in `CategoryProduct.MVC/Controllers/CategoriesController.cs` and `CategoryProduct.MVC/Controllers/ProductsController.cs`
- [X] T013 [P] [US2] Create `CategoryProduct.MVC/Views/Account/AccessDenied.cshtml`
- [X] T014 [P] [US2] Hide write links in `CategoryProduct.MVC/Views/Categories/Index.cshtml`, `Details.cshtml` and `CategoryProduct.MVC/Views/Products/Index.cshtml`, `Details.cshtml`
