---

description: "Retroactive task list for MVC API client"
---

# Tasks: MVC Consumes the API

**Input**: Design documents from `/specs/006-mvc-api-client/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: None automated; verified manually against the running API (see `quickstart.md`).

**Organization**: Grouped by user story; delivered in commit `c09170f`.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story the task belongs to (US1, US2)

## Path Conventions

Existing project `CategoryProduct.MVC/` at repository root.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Point MVC at the API and drop the database layer

- [X] T001 Add `Api:BaseUrl` and remove connection strings in `CategoryProduct.MVC/appsettings.json`
- [X] T002 Register `AddHttpClient<CategoryProductApiClient>` and remove the EF registration in `CategoryProduct.MVC/Program.cs`
- [X] T003 [P] Remove EF Core packages from `CategoryProduct.MVC/CategoryProduct.MVC.csproj`
- [X] T004 [P] Delete `CategoryProduct.MVC/Data/ApplicationDbContext.cs`

**Checkpoint**: MVC no longer references EF Core or a connection string (US2).

---

## Phase 2: Foundational - Client and models (blocks Phase 3)

**Purpose**: The client and models every controller depends on

- [X] T005 [US1] Create `CategoryProduct.MVC/Services/CategoryProductApiClient.cs` (list, get, create, update, delete, bearer hook)
- [X] T006 [P] [US1] Create `CategoryProduct.MVC/Models/ApiModels.cs`
- [X] T007 [P] [US2] Simplify `CategoryProduct.MVC/Models/Category.cs` and `CategoryProduct.MVC/Models/Product.cs`

**Checkpoint**: Client and models compile (T005 uses the types from T006/T007).

---

## Phase 3: User Story 1 - MVC pages work through the API (Priority: P1)

**Goal**: List, details and form flows run on the API.

**Independent Test**: With the API running, exercise every page per `quickstart.md`.

- [X] T008 [US1] Rewrite `CategoryProduct.MVC/Controllers/CategoriesController.cs` on the client
- [X] T009 [P] [US1] Rewrite `CategoryProduct.MVC/Controllers/ProductsController.cs` on the client
- [X] T010 [P] [US1] Adjust `CategoryProduct.MVC/Views/Categories/Create.cshtml`, `Delete.cshtml`, `Index.cshtml` (product-count column and product list removed from Index/Delete; validation scripts added to Create; validation summary added to Delete)
- [X] T011 [P] [US1] Adjust `CategoryProduct.MVC/Views/Products/Create.cshtml`, `Delete.cshtml`, `Details.cshtml`, `Index.cshtml` (use `CategoryName`; validation scripts added to Create; validation summary added to Delete)

**Checkpoint**: All MVC pages work through the API (writes need sign-in, see 007).

---

## Dependencies & Execution Order

- Phase 1 has no dependencies; T003 and T004 are parallel (different files).
- Phase 2 depends on Phase 1 (the app must compile without the `DbContext` types); T006 and T007 are parallel, T005 follows them.
- Phase 3 depends on Phase 2; T009-T011 are parallel (different files), T008 is also independent of them.

## Notes

- [P] tasks = different files, no dependencies.
- The Categories `Details.cshtml` view was not changed in this commit.
