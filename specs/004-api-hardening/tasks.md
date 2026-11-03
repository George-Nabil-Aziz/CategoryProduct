---

description: "Retroactive task list for API hardening"
---

# Tasks: API Hardening

**Input**: Design documents from `/specs/004-api-hardening/`

**Tests**: Added separately in feature 005.

## Format: `[ID] [P?] [Story] Description`

## Phase 1: Secrets (US2)

- [X] T001 [US2] Remove `Jwt:Key` from `CategoryProduct.API/appsettings.json`
- [X] T002 [US2] Add startup check (missing or under 32 chars throws with instructions) in `CategoryProduct.API/Program.cs`
- [X] T003 [US2] Add `public partial class Program { }` to `CategoryProduct.API/Program.cs` for test hosting

## Phase 2: Validation (US1)

- [X] T004 [US1] Check `CategoryId` exists in `Post` of `CategoryProduct.API/Controllers/ProductController.cs`, return `ValidationProblem`
- [X] T005 [US1] Same check in `Put` of `CategoryProduct.API/Controllers/ProductController.cs`

## Phase 3: Pagination (US3)

- [X] T006 [P] [US3] Create `CategoryProduct.API/Dtos/PagedResponseDto.cs`
- [X] T007 [US3] Clamp page/pageSize, order by Id, return envelope in `CategoryProduct.API/Controllers/CategoryController.cs`
- [X] T008 [US3] Same in `CategoryProduct.API/Controllers/ProductController.cs`

## Phase 4: Async (US4)

- [X] T009 [US4] Convert actions to async EF Core in `CategoryProduct.API/Controllers/CategoryController.cs`
- [X] T010 [P] [US4] Same in `CategoryProduct.API/Controllers/ProductController.cs`
- [X] T011 [P] [US4] Same in `CategoryProduct.API/Controllers/AccountController.cs`
