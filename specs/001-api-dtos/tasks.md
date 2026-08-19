---

description: "Task list template for feature implementation"
---

# Tasks: API DTOs for Category & Product

**Input**: Design documents from `/specs/001-api-dtos/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md (all present)

**Tests**: Not included — no automated test project exists for `CategoryProduct.API` yet, and the
feature spec/description did not request tests or TDD. Verification is manual, via
`quickstart.md` against the running API (Polish phase, T011).

**Organization**: Tasks are grouped by user story (from `spec.md`: US1 = P1, US2 = P2, US3 = P3) to
enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

Single existing project modified in place: `CategoryProduct.API/`. New DTOs live in
`CategoryProduct.API/Dtos/`; controllers modified in `CategoryProduct.API/Controllers/`. No new
project or `tests/` directory is introduced (see `plan.md` Project Structure).

---

## Phase 1: Setup

**Purpose**: Establish the new DTO location before any DTO types are added.

- [X] T001 Create the `CategoryProduct.API/Dtos/` folder as the home for all new DTO types (no
  code yet — this is the container for T002, T003, T006, T007)

**Checkpoint**: Folder exists; ready for Foundational DTO types.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Response DTOs are shared by all three user stories (US1 reads them directly, US2's
create/update actions return them, US3's products-by-category listing reuses the product one) —
so they must exist before any user story phase begins.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T002 [P] Create `CategoryResponseDto` record (`Id`, `Name`) in
  `CategoryProduct.API/Dtos/CategoryResponseDto.cs`, per `data-model.md`
- [X] T003 [P] Create `ProductResponseDto` record (`Id`, `Name`, `Price`, `Quantity`,
  `CategoryId`) in `CategoryProduct.API/Dtos/ProductResponseDto.cs`, per `data-model.md` (no
  category name/nested category field — see Clarifications in `spec.md`)

**Checkpoint**: Both response DTOs exist — user story implementation can now begin.

---

## Phase 3: User Story 1 - Clean, stable read responses (Priority: P1) 🎯 MVP

**Goal**: `GET` endpoints for categories and products return only the response DTO shape — no
navigation collections/objects (FR-001, FR-002, FR-008).

**Independent Test**: Call `GET /api/category`, `GET /api/category/{id}`, `GET /api/product`, and
`GET /api/product/{id}` and confirm each response matches `contracts/category-api.md` /
`contracts/product-api.md` exactly, with no `products` array on a category or `category`
object/name on a product (`quickstart.md` Scenario 1).

### Implementation for User Story 1

- [X] T004 [P] [US1] Update `CategoryController`'s `Get(page, pageSize)` (list) and `Get(id)`
  (single) actions to map each `Category` to a `CategoryResponseDto` before returning, in
  `CategoryProduct.API/Controllers/CategoryController.cs` (depends on T002)
- [X] T005 [P] [US1] Update `ProductController`'s `Get(page, pageSize)` (list) and `Get(id)`
  (single) actions to map each `Product` to a `ProductResponseDto` before returning, in
  `CategoryProduct.API/Controllers/ProductController.cs` (depends on T003)

**Checkpoint**: User Story 1 is fully functional and independently testable — read endpoints no
longer leak entity navigation data.

---

## Phase 4: User Story 2 - Well-defined write requests (Priority: P2)

**Goal**: `POST`/`PUT` endpoints for categories and products accept only the request DTO shape —
the server always assigns the identifier, regardless of what the client submits (FR-003–FR-006).

**Independent Test**: Submit `POST`/`PUT` requests to `/api/category` and `/api/product` with only
the documented request fields (and, separately, with an extra `id` field) and confirm the server
assigns/keeps its own identifier and the response matches the response DTO shape
(`quickstart.md` Scenario 2).

### Implementation for User Story 2

- [X] T006 [P] [US2] Create `CategoryRequestDto` record (`Name`) in
  `CategoryProduct.API/Dtos/CategoryRequestDto.cs`, per `data-model.md` (shared shape for create
  and update — see `research.md`)
- [X] T007 [P] [US2] Create `ProductRequestDto` record (`Name`, `Price`, `Quantity`,
  `CategoryId`) in `CategoryProduct.API/Dtos/ProductRequestDto.cs`, per `data-model.md` (shared
  shape for create and update)
- [X] T008 [P] [US2] Update `CategoryController`'s `Post` and `Put(id)` actions to accept a
  `CategoryRequestDto` request body, map it to/from the `Category` entity, and return a
  `CategoryResponseDto`, in `CategoryProduct.API/Controllers/CategoryController.cs` (depends on
  T006; touches the same file as T004 — apply after T004 if working sequentially)
- [X] T009 [P] [US2] Update `ProductController`'s `Post` and `Put(id)` actions to accept a
  `ProductRequestDto` request body, map it to/from the `Product` entity, and return a
  `ProductResponseDto`, in `CategoryProduct.API/Controllers/ProductController.cs` (depends on
  T007; touches the same file as T005 — apply after T005 if working sequentially)

**Checkpoint**: User Stories 1 AND 2 both work independently — write endpoints now have a
well-defined, server-controlled request contract.

---

## Phase 5: User Story 3 - Consistent shape for related-data listing (Priority: P3)

**Goal**: The products-by-category listing returns the exact same `ProductResponseDto` shape as
the standalone product endpoints (FR-007).

**Independent Test**: Call `GET /api/category/{id}/products` and confirm each returned item
matches `contracts/product-api.md` exactly — the same shape as `GET /api/product`
(`quickstart.md` Scenario 3).

### Implementation for User Story 3

- [X] T010 [US3] Update `CategoryController`'s `GetAllProducts(catId)` action to map each
  `Product` to a `ProductResponseDto` before returning, in
  `CategoryProduct.API/Controllers/CategoryController.cs` (depends on T003; touches the same file
  as T004/T008 — apply last if working sequentially)

**Checkpoint**: All three user stories are independently functional — no endpoint in
`CategoryProduct.API` exposes a raw `Category` or `Product` entity anymore.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final validation that the feature is complete and consistent.

- [X] T011 [P] Run all three `quickstart.md` validation scenarios against the running API via
  Swagger UI, confirming responses match `contracts/category-api.md` and
  `contracts/product-api.md`
- [X] T012 Review `CategoryProduct.API/Controllers/CategoryController.cs` and
  `CategoryProduct.API/Controllers/ProductController.cs` to confirm no action still returns or
  accepts a bare `Category`/`Product` entity (FR-008)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2)**: Depends on Setup (T001) — BLOCKS all user stories
- **User Stories (Phase 3-5)**: All depend on Foundational (Phase 2) completion
  - US1 and US2 touch different DTOs but the *same* controller files, so treat them as sequential
    per entity (Category, Product) unless coordinated carefully; US1 and US2 are otherwise
    logically independent
  - US3 depends only on the Foundational `ProductResponseDto` (T003), but its one task shares a
    file with US1/US2's Category work, so apply it after both for a clean sequential diff
- **Polish (Phase 6)**: Depends on all three user stories being complete

### User Story Dependencies

- **User Story 1 (P1)**: Can start after Foundational (Phase 2) — no dependency on US2/US3
- **User Story 2 (P2)**: Can start after Foundational (Phase 2) — no functional dependency on US1,
  but shares files with it (see above)
- **User Story 3 (P3)**: Can start after Foundational (Phase 2) — no functional dependency on
  US1/US2, but shares `CategoryController.cs` with both

### Parallel Opportunities

- T002 and T003 (Foundational response DTOs) — different files, run in parallel
- T004 (Category controller GET) and T005 (Product controller GET) — different files, run in
  parallel
- T006 and T007 (request DTOs) — different files, run in parallel
- T008 (Category controller POST/PUT) and T009 (Product controller POST/PUT) — different files,
  run in parallel
- Within a single agent/session working sequentially, apply all Category-file edits
  (T004 → T008 → T010) in order and all Product-file edits (T005 → T009) in order, since each
  group shares one file

---

## Parallel Example: Foundational Phase

```bash
Task: "Create CategoryResponseDto record in CategoryProduct.API/Dtos/CategoryResponseDto.cs"
Task: "Create ProductResponseDto record in CategoryProduct.API/Dtos/ProductResponseDto.cs"
```

## Parallel Example: User Story 1

```bash
Task: "Update CategoryController GET endpoints to return CategoryResponseDto in CategoryProduct.API/Controllers/CategoryController.cs"
Task: "Update ProductController GET endpoints to return ProductResponseDto in CategoryProduct.API/Controllers/ProductController.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001)
2. Complete Phase 2: Foundational (T002, T003) — CRITICAL, blocks all stories
3. Complete Phase 3: User Story 1 (T004, T005)
4. **STOP and VALIDATE**: Run `quickstart.md` Scenario 1 against the running API
5. This alone already fixes the entity-navigation leak on every read endpoint

### Incremental Delivery

1. Setup + Foundational → foundation ready
2. Add User Story 1 → validate Scenario 1 → clean read responses shipped (MVP)
3. Add User Story 2 → validate Scenario 2 → well-defined write requests shipped
4. Add User Story 3 → validate Scenario 3 → consistent products-by-category shape shipped
5. Polish (T011, T012) → final full-scenario pass and code review confirmation

---

## Notes

- [P] tasks touch different files and have no dependency on each other
- [Story] label maps each task to its user story for traceability
- No tests are included per the "Tests" note above; `quickstart.md` (T011) is the verification
  mechanism
- Category and Product controller files are each edited across two or three phases (US1, US2, and
  for Category also US3) — this is a natural consequence of both actions living in the same small
  controller class, not a violation of story independence (each story still adds an
  independently-testable, self-contained capability)
- Commit after each task or logical group
