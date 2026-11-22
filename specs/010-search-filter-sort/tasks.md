---

description: "Task list for Search, Filter and Sort (retroactive; all delivered in aebb0fc)"
---

# Tasks: Search, Filter and Sort

**Input**: Design documents from `/specs/010-search-filter-sort/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: API tests exist (4); MVC is manual.

**Organization**: By user story. All work shipped in commit `aebb0fc`.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story the task belongs to

## Path Conventions

`CategoryProduct.API/`, `CategoryProduct.API.Tests/`, `CategoryProduct.MVC/` at repository root.

---

## Phase 1: Setup

- [X] T001 Seed three products ("Widget A", "Widget B", "Gadget") in `CategoryProduct.API.Tests/ApiFactory.cs`

## Phase 2: Foundational

*No shared prerequisites beyond T001.*

---

## Phase 3: User Story 1 - Search products by name (Priority: P1)

**Goal**: Case-insensitive substring search with a correct `totalCount`.

**Independent Test**: `Products_Search_Filters_By_Name`.

- [X] T002 [P] [US1] Add test `Products_Search_Filters_By_Name` in `CategoryProduct.API.Tests/ApiTests.cs`
- [X] T003 [US1] Add `search` parameter and filtered count/paging to `Get` in `CategoryProduct.API/Controllers/ProductController.cs`

## Phase 4: User Story 2 - Filter and sort products (Priority: P1)

**Goal**: Category and price filters, name/price sort, inverted-range 400.

**Independent Test**: `Products_PriceRange_And_Sort_Desc`, `Products_MinPrice_Greater_Than_MaxPrice_Returns_400`.

- [X] T004 [P] [US2] Add tests `Products_PriceRange_And_Sort_Desc` and `Products_MinPrice_Greater_Than_MaxPrice_Returns_400` in `CategoryProduct.API.Tests/ApiTests.cs`
- [X] T005 [US2] Add `categoryId`, `minPrice`, `maxPrice`, `sortBy`, `desc` and the range validation to `CategoryProduct.API/Controllers/ProductController.cs`

## Phase 5: User Story 3 - Search categories (Priority: P2)

**Goal**: Name search on categories.

**Independent Test**: `Categories_Search_Returns_Matching_TotalCount`.

- [X] T006 [P] [US3] Add test `Categories_Search_Returns_Matching_TotalCount` in `CategoryProduct.API.Tests/ApiTests.cs`
- [X] T007 [US3] Add `search` parameter to `Get` in `CategoryProduct.API/Controllers/CategoryController.cs`

## Phase 6: User Story 4 - MVC pages (Priority: P2)

**Goal**: Filter forms and filter-preserving paging in the MVC index pages.

**Independent Test**: Manual (quickstart).

- [X] T008 [US4] Pass all parameters to the API in `GetProductsAsync`/`GetCategoriesAsync` in `CategoryProduct.MVC/Services/CategoryProductApiClient.cs`
- [X] T009 [P] [US4] Accept filters and fill `ViewBag` in `Index` of `CategoryProduct.MVC/Controllers/ProductsController.cs`
- [X] T010 [P] [US4] Accept `search` and fill `ViewBag` in `Index` of `CategoryProduct.MVC/Controllers/CategoriesController.cs`
- [X] T011 [P] [US4] Add filter form and filter-preserving paging links in `CategoryProduct.MVC/Views/Products/Index.cshtml`
- [X] T012 [P] [US4] Add search form and search-preserving paging links in `CategoryProduct.MVC/Views/Categories/Index.cshtml`

## Phase 7: Polish

- [X] T013 Document delivered work in `specs/010-search-filter-sort/` (this folder)

## Dependencies & Execution Order

- T001 before T002, T004, T006 (tests rely on the seed).
- T008 before T009/T010; views T011/T012 depend on the controllers.
- US1-US3 (API) are independent of US4 (MVC consumer).
