---

description: "Retroactive task list for MVC login"
---

# Tasks: MVC Login

**Input**: Design documents from `/specs/007-mvc-login/`

**Tests**: None; verified manually in the browser.

## Format: `[ID] [P?] [Story] Description`

## Phase 1: Setup

- [X] T001 Register `AddHttpContextAccessor`, cookie auth, and `UseAuthentication` in `CategoryProduct.MVC/Program.cs`
- [X] T002 [P] Create `CategoryProduct.MVC/Models/LoginViewModel.cs`

## Phase 2: Sign-in (US1)

- [X] T003 [US1] Add `LoginAsync` and bearer-from-claim logic in `CategoryProduct.MVC/Services/CategoryProductApiClient.cs`
- [X] T004 [US1] Create `CategoryProduct.MVC/Controllers/AccountController.cs` (Login GET/POST, Logout)
- [X] T005 [P] [US1] Create `CategoryProduct.MVC/Views/Account/Login.cshtml`
- [X] T006 [P] [US1] Add sign in / sign out UI to `CategoryProduct.MVC/Views/Shared/_Layout.cshtml`

## Phase 3: Protected writes (US2)

- [X] T007 [US2] Add `[Authorize]` and 401 sign-out handling in `CategoryProduct.MVC/Controllers/CategoriesController.cs`
- [X] T008 [US2] Same in `CategoryProduct.MVC/Controllers/ProductsController.cs`
- [X] T009 [P] [US2] Minor view updates in `CategoryProduct.MVC/Views/Categories/Index.cshtml` and `CategoryProduct.MVC/Views/Products/Index.cshtml`
