---

description: "Retroactive task list for API hardening"
---

# Tasks: API Hardening

**Input**: Design documents from `/specs/004-api-hardening/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Added in the same commit as feature 005 (`CategoryProduct.API.Tests/ApiTests.cs`); other scenarios are verified manually per `quickstart.md`.

**Organization**: Grouped by user story (US1-US4). Delivered in commits `b17b5ee` (US1-US3) and `3149197` (US4).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story the task belongs to

## Path Conventions

Single existing project: `CategoryProduct.API/` at repository root.

---

## Phase 1: Setup (US2 - secrets)

**Purpose**: Take the signing key out of source control and make the API testable in process

- [X] T001 [US2] Remove the `Jwt:Key` entry from `CategoryProduct.API/appsettings.json` (keep `Issuer`, `Audience`, `ExpiryMinutes`)
- [X] T002 [US2] Add startup check (missing, blank or under 32 chars throws `InvalidOperationException` with instructions) in `CategoryProduct.API/Program.cs`
- [X] T003 [US2] Add `public partial class Program { }` to `CategoryProduct.API/Program.cs` for test hosting (same file as T002)

**Checkpoint**: No key in tracked config; API refuses to start without one.

---

## Phase 2: User Story 1 - Reject missing category (Priority: P1)

**Goal**: Product create/update with an unknown `CategoryId` returns 400 and saves nothing.

- [X] T004 [US1] Check `CategoryId` exists in `Post` of `CategoryProduct.API/Controllers/ProductController.cs`, return `ValidationProblem`
- [X] T005 [US1] Same check in `Put` of `CategoryProduct.API/Controllers/ProductController.cs` (same file as T004)

**Checkpoint**: `PostProduct_NonExistentCategory_Returns400` passes (005).

---

## Phase 3: User Story 3 - Paged list responses (Priority: P2)

**Goal**: List endpoints return `PagedResponseDto<T>` with clamped paging and stable order.

- [X] T006 [P] [US3] Create `CategoryProduct.API/Dtos/PagedResponseDto.cs`
- [X] T007 [US3] Clamp page/pageSize, order by Id, return envelope in `CategoryProduct.API/Controllers/CategoryController.cs` (depends on T006)
- [X] T008 [P] [US3] Same in `CategoryProduct.API/Controllers/ProductController.cs` (different file from T007; depends on T006)

**Checkpoint**: `GetCategories_PageSizeAbove50_IsClamped` passes (005).

---

## Phase 4: User Story 4 - Async data access (Priority: P3, commit `3149197`)

**Goal**: Every API controller action uses async EF Core calls with no behavior change.

- [X] T009 [P] [US4] Convert actions to async EF Core in `CategoryProduct.API/Controllers/CategoryController.cs`
- [X] T010 [P] [US4] Same in `CategoryProduct.API/Controllers/ProductController.cs`
- [X] T011 [P] [US4] Same in `CategoryProduct.API/Controllers/AccountController.cs`

**Checkpoint**: Integration tests (005) still pass.

---

## Dependencies & Execution Order

- Phase 1 has no dependencies; T002 and T003 edit the same file, so they are sequential.
- US1 (T004-T005) and US3 (T006-T008) both touch `ProductController.cs`, so run them one after the other.
- US4 (T009-T011) depends on US1 and US3 being in place (it rewrites the same controllers) and shipped as a separate commit.
- T006 blocks T007 and T008, which can then run in parallel.

## Notes

- [P] tasks = different files, no dependencies.
- The sync-to-async change changed only call style; status codes and bodies are identical (except the paging envelope from US3).
