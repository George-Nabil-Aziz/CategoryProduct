---

description: "Task list template for feature implementation"
---

# Tasks: Request Validation

**Input**: Design documents from `/specs/002-request-validation/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md (all present)

**Tests**: No automated test project exists in `CategoryProduct.slnx` (see research.md Decision 3
and constitution "Development Workflow"). Verification tasks below are manual, against the running
API via Swagger UI, per `quickstart.md` — no automated test tasks are included.

**Organization**: Tasks are grouped by user story (Category = US1, Product = US2) to enable
independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2)
- Include exact file paths in descriptions

## Path Conventions

Single existing project, no new directories: all implementation tasks touch files under
`CategoryProduct.API/Dtos/`.

---

## Phase 1: Setup

**Purpose**: Baseline confirmation before any changes are made.

- [X] T001 Run `dotnet build CategoryProduct.slnx` from the repository root and confirm it
  succeeds, establishing a clean baseline before `CategoryProduct.API/Dtos/CategoryRequestDto.cs`
  and `CategoryProduct.API/Dtos/ProductRequestDto.cs` are modified

---

## Phase 2: Foundational (Blocking Prerequisites)

**Not applicable to this feature.** There is no shared infrastructure, dependency, or base
type that both user stories need — each story edits its own single, independent DTO file
(`CategoryRequestDto.cs` for US1, `ProductRequestDto.cs` for US2), and ASP.NET Core's automatic
`[ApiController]` model-validation behavior (already active on both controllers, per
`research.md` Decision 1) requires no wiring. No tasks in this phase; proceed directly to Phase 3.

---

## Phase 3: User Story 1 - Reject invalid Category submissions (Priority: P1) 🎯 MVP

**Goal**: `POST /api/Category` and `PUT /api/Category/{id}` reject requests whose `Name` is
missing, blank/whitespace-only, or outside the 2–100 character range, returning a field-level
error and persisting nothing; valid requests continue to succeed unchanged.

**Independent Test**: Submit Create/Update Category requests with a missing name, a 1-character
name, a 101+-character name, a whitespace-only name, and a valid 2–100 character name (including
the 2 and 100 boundary lengths); confirm only the valid ones return `200` and are persisted, and
every invalid one returns `400` naming the `Name` field.

### Implementation for User Story 1

- [X] T002 [US1] In `CategoryProduct.API/Dtos/CategoryRequestDto.cs`, add
  `[Required(AllowEmptyStrings = false)]` and `[StringLength(100, MinimumLength = 2)]` directly to
  the `Name` parameter of the `CategoryRequestDto` positional record (per `data-model.md`; note —
  applied without a `property:` target, since ASP.NET Core's record model-validation requires
  attributes on the constructor parameter itself — see Notes below), so that `[ApiController]`
  automatic model validation rejects a missing, blank/whitespace-only, or out-of-range `Name` with
  `400 Bad Request` before `CategoryController`'s `Post`/`Put` actions run

### Verification for User Story 1 (manual, per quickstart.md)

- [X] T003 [US1] Manually run Category scenarios 1–6 from
  `specs/002-request-validation/quickstart.md` against the running `CategoryProduct.API`
  (missing name, 1-char name, 101-char name, whitespace-only name, 2-char and 100-char boundary
  names on `POST /api/Category`, and an invalid-name `PUT /api/Category/{id}`), confirming `400`
  with a `Name` error for each invalid case, `200` + persistence for both boundary values, and the
  existing record unchanged after the rejected `PUT`

**Checkpoint**: User Story 1 is fully functional and independently testable — Category validation
matches FR-001, FR-002, FR-007, FR-008.

---

## Phase 4: User Story 2 - Reject invalid Product submissions (Priority: P2)

**Goal**: `POST /api/Product` and `PUT /api/Product/{id}` reject requests whose `Name`, `Price`,
`Quantity`, or `CategoryId` violate their stated rules, returning every failing field in one
response and persisting nothing; valid requests (including `price`/`quantity` of exactly `0`)
continue to succeed unchanged.

**Independent Test**: Submit Create/Update Product requests that each violate exactly one rule
(bad name, negative price, negative quantity, non-positive `categoryId`), one request violating
two rules at once, and one fully valid boundary request (`price: 0`, `quantity: 0`, valid
`categoryId`); confirm only the valid one succeeds and is persisted, each single-violation request
returns `400` naming only that field, and the two-violation request returns `400` naming both
fields in one response.

### Implementation for User Story 2

- [X] T004 [US2] In `CategoryProduct.API/Dtos/ProductRequestDto.cs`, add
  `[Required(AllowEmptyStrings = false)]` and `[StringLength(100, MinimumLength = 2)]` to the
  `Name` parameter, `[Range(typeof(decimal), "0", "79228162514264337593543950335")]` to the
  `Price` parameter, `[Range(0, int.MaxValue)]` to the `Quantity` parameter, and
  `[Range(1, int.MaxValue)]` to the `CategoryId` parameter of the `ProductRequestDto` positional
  record — all applied directly to the constructor parameters, no `property:` target (per
  `data-model.md`; see Notes below) — so that `[ApiController]` automatic model validation rejects
  any violation, surfacing every failing field at once, with `400 Bad Request` before
  `ProductController`'s `Post`/`Put` actions run

### Verification for User Story 2 (manual, per quickstart.md)

- [X] T005 [US2] Manually run Product scenarios 1–7 from
  `specs/002-request-validation/quickstart.md` against the running `CategoryProduct.API` (bad
  name, negative price, negative quantity, `categoryId` of `0` and `-5`, combined
  name+price failure, `price: 0`/`quantity: 0` boundary success, and an invalid `PUT
  /api/Product/{id}`), confirming `400` naming the correct field(s) for each invalid case
  (including both fields in the combined-failure case), `200` + persistence for the boundary
  request, and the existing record unchanged after the rejected `PUT`

**Checkpoint**: User Stories 1 AND 2 both work independently — Product validation matches FR-003
through FR-007, FR-008.

---

## Phase 5: Polish & Cross-Cutting Concerns

**Purpose**: Confirm the two independent DTO changes compose correctly and nothing else regressed.

- [X] T006 [P] Run `dotnet build CategoryProduct.slnx` from the repository root and confirm it
  still succeeds after both `CategoryRequestDto.cs` and `ProductRequestDto.cs` changes
- [X] T007 Manually verify the "Unaffected endpoints" regression list in
  `specs/002-request-validation/quickstart.md` — `GET`/`DELETE` for both `Category` and `Product`,
  plus `GET /api/Category/{catId}/products` — behave identically to before this feature, and that
  `CategoryResponseDto`/`ProductResponseDto` shapes are unchanged (FR-009, FR-010)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — run first.
- **Foundational (Phase 2)**: Not applicable — no tasks, nothing blocks the user stories.
- **User Story 1 (Phase 3)**: Depends only on Setup (T001). No dependency on User Story 2.
- **User Story 2 (Phase 4)**: Depends only on Setup (T001). No dependency on User Story 1 — can be
  implemented before, after, or concurrently with US1 since it edits a different file.
- **Polish (Phase 5)**: Depends on both User Story 1 (T002) and User Story 2 (T004) being complete.

### Within Each User Story

- T003 (US1 verification) depends on T002 (US1 implementation).
- T005 (US2 verification) depends on T004 (US2 implementation).

### Parallel Opportunities

- T002 (`CategoryRequestDto.cs`) and T004 (`ProductRequestDto.cs`) touch different files and have
  no dependency on each other — they can be done in parallel (e.g., by different developers, or
  in either order by one developer).
- T006 is marked `[P]` relative to T007 (build check vs. manual regression check are independent
  activities), though both require T002 and T004 complete first.

---

## Parallel Example: User Stories 1 and 2

```bash
# After T001 (baseline build), both story implementations can proceed in parallel:
Task: "Add Required/StringLength attributes to Name in CategoryProduct.API/Dtos/CategoryRequestDto.cs"   # T002 [US1]
Task: "Add Required/StringLength/Range attributes in CategoryProduct.API/Dtos/ProductRequestDto.cs"       # T004 [US2]
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001).
2. Skip Phase 2 (not applicable).
3. Complete Phase 3: User Story 1 (T002–T003).
4. **STOP and VALIDATE**: Category create/update requests are correctly rejected or accepted per
   FR-001/FR-002, independent of any Product change.
5. This alone is a shippable increment — Category validation with zero Product changes.

### Incremental Delivery

1. Setup (T001) → baseline confirmed.
2. Add User Story 1 (T002–T003) → validate independently → ships Category validation.
3. Add User Story 2 (T004–T005) → validate independently → ships Product validation.
4. Polish (T006–T007) → confirm the combined build and full regression sweep.

### Parallel Team Strategy

With two developers: after T001, one takes US1 (T002–T003) and the other takes US2 (T004–T005)
simultaneously, since the two DTO files are fully independent; both converge on Phase 5.

---

## Notes

- Only 7 tasks total — this feature is intentionally minimal (2 files, ~7 validation rules, no new
  services/controllers/dependencies), per Constitution Principle III and `plan.md`'s Summary.
- No automated test tasks: no test project exists yet in the solution: verification tasks (T003,
  T005, T007) are manual, against the running API, per `quickstart.md` and constitution
  "Development Workflow."
- `[P]` markers are limited to T002/T004 (cross-story, different files) and T006 (independent of
  T007) — no two tasks in this feature ever touch the same file, so there are no same-file
  conflicts to avoid.
- Commit after each task or logical group; stop at either checkpoint to validate that story
  independently before continuing.
- **Correction found during implementation (T002/T004)**: `research.md` Decision 1 originally
  specified the `[property: ...]` attribute-target syntax for positional-record validation
  attributes. Running the API surfaced a `500` (`InvalidOperationException`: "Record type ...
  has validation metadata defined on property 'Name' that will be ignored ... validation
  metadata must be associated with the constructor parameter") on every `POST`/`PUT`. ASP.NET
  Core's record model-validation reads attributes from the primary constructor parameter, not the
  synthesized property, so the fix is to apply the attributes with no explicit target (e.g.
  `[Required] [StringLength(100, MinimumLength = 2)] string Name`), which C# then binds to the
  parameter by default. Both DTOs were implemented this way, and all quickstart scenarios (T003,
  T005, T007) were re-verified end-to-end against the running API afterward and passed.
