---

description: "Retroactive task list for MVC login"
---

# Tasks: MVC Login

**Input**: Design documents from `/specs/007-mvc-login/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: None automated; verified manually in the browser (see `quickstart.md`).

**Organization**: Grouped by user story; delivered in commit `142ace4`.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story the task belongs to (US1, US2)

## Path Conventions

Existing project `CategoryProduct.MVC/` at repository root.

---

## Phase 1: Setup (Shared Infrastructure)

- [X] T001 Register `AddHttpContextAccessor`, cookie auth (`LoginPath`, `LogoutPath`, `AccessDeniedPath`, HttpOnly, Secure=Always, SameSite=Lax) and `UseAuthentication` in `CategoryProduct.MVC/Program.cs`
- [X] T002 [P] Create `CategoryProduct.MVC/Models/LoginViewModel.cs`

**Checkpoint**: Cookie authentication is configured.

---

## Phase 2: User Story 1 - Sign in and out (Priority: P1)

**Goal**: A user can sign in with API credentials and sign out.

**Independent Test**: Sign in, see the greeting; sign out, see the Login link.

- [X] T003 [US1] Add `LoginAsync` and bearer-from-claim logic in `CategoryProduct.MVC/Services/CategoryProductApiClient.cs` (takes `IHttpContextAccessor`; per-request header)
- [X] T004 [US1] Create `CategoryProduct.MVC/Controllers/AccountController.cs` (Login GET/POST, Logout POST; depends on T002, T003)
- [X] T005 [P] [US1] Create `CategoryProduct.MVC/Views/Account/Login.cshtml`
- [X] T006 [P] [US1] Add sign in / sign out UI to `CategoryProduct.MVC/Views/Shared/_Layout.cshtml`

**Checkpoint**: Sign-in and sign-out work (US1).

---

## Phase 3: User Story 2 - Protected writes (Priority: P1)

**Goal**: Write pages need sign-in and the API receives the token.

**Independent Test**: Anonymous `/Categories/Create` redirects to login; signed-in create succeeds.

- [X] T007 [US2] Add `[Authorize]` and 401 sign-out handling in `CategoryProduct.MVC/Controllers/CategoriesController.cs` (depends on T001, T003)
- [X] T008 [P] [US2] Same in `CategoryProduct.MVC/Controllers/ProductsController.cs`
- [X] T009 [P] [US2] Show "Create New" only to authenticated users in `CategoryProduct.MVC/Views/Categories/Index.cshtml` and `CategoryProduct.MVC/Views/Products/Index.cshtml`

**Checkpoint**: Anonymous users cannot complete an MVC write (US2).

---

## Dependencies & Execution Order

- T001 and T002 are independent; both precede Phase 2.
- T003 precedes T004 (the controller calls `LoginAsync`) and T007/T008 (they rely on the bearer logic).
- T005, T006, T008, T009 touch different files and can run in parallel once their prerequisites exist.

## Notes

- [P] tasks = different files, no dependencies.
- The layout file also had its BOM removed in this commit; no functional change.
