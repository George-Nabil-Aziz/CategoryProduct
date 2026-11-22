---

description: "Task list for Error Handling and Logging (retroactive; all delivered in f47b61e)"
---

# Tasks: Error Handling and Logging

**Input**: Design documents from `/specs/011-error-handling-logging/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: One automated API test (US1); logging output and the MVC outage page are manual.

**Organization**: By user story. All work shipped in commit `f47b61e`.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story the task belongs to

## Path Conventions

`CategoryProduct.API/`, `CategoryProduct.API.Tests/`, `CategoryProduct.MVC/` at repository root.

---

## Phase 1: Setup

- [X] T001 [P] Add `Serilog.AspNetCore` 10.0.0 to `CategoryProduct.API/CategoryProduct.API.csproj`
- [X] T002 [P] Add `Serilog.AspNetCore` 10.0.0 to `CategoryProduct.MVC/CategoryProduct.MVC.csproj`
- [X] T003 [P] Add `logs/` to `.gitignore`

## Phase 2: Foundational

*None; Setup is sufficient.*

---

## Phase 3: User Story 1 - Safe, standard API error responses (Priority: P1)

**Goal**: 500 ProblemDetails with `traceId` and no leaked detail.

**Independent Test**: `UnhandledException_Returns500ProblemJson`.

- [X] T004 [P] [US1] Add `ThrowingFactory` and `UnhandledException_Returns500ProblemJson` in `CategoryProduct.API.Tests/ErrorHandlingTests.cs`
- [X] T005 [P] [US1] Create `GlobalExceptionHandler` in `CategoryProduct.API/Infrastructure/GlobalExceptionHandler.cs`
- [X] T006 [US1] Register `AddProblemDetails`, `AddExceptionHandler<GlobalExceptionHandler>`, `UseExceptionHandler`, `UseStatusCodePages` in `CategoryProduct.API/Program.cs`

## Phase 4: User Story 2 - Structured logging (Priority: P1)

**Goal**: Serilog console + rolling file logs in both apps.

**Independent Test**: Manual (quickstart steps 1-2).

- [X] T007 [P] [US2] Add `Serilog` section to `CategoryProduct.API/appsettings.json`
- [X] T008 [P] [US2] Add `Serilog` section to `CategoryProduct.MVC/appsettings.json`
- [X] T009 [US2] Add `UseSerilog` and `UseSerilogRequestLogging` in `CategoryProduct.API/Program.cs`
- [X] T010 [US2] Add `UseSerilog` and `UseSerilogRequestLogging` in `CategoryProduct.MVC/Program.cs`

## Phase 5: User Story 3 - Friendly page when the API is unavailable (Priority: P1)

**Goal**: 503 "Service unavailable" page for outages and 5xx.

**Independent Test**: Manual (quickstart step 3).

- [X] T011 [P] [US3] Create `ApiUnavailableException`, `ApiErrorHandler`, `ApiUnavailableFilter` in `CategoryProduct.MVC/Services/ApiErrorHandler.cs`
- [X] T012 [P] [US3] Create `CategoryProduct.MVC/Views/Shared/ServiceUnavailable.cshtml`
- [X] T013 [US3] Register `ApiErrorHandler` on the typed client and `ApiUnavailableFilter` globally in `CategoryProduct.MVC/Program.cs`

## Phase 6: Polish

- [X] T014 Document delivered work in `specs/011-error-handling-logging/` (this folder)

## Dependencies & Execution Order

- T001/T002 before T009/T010; T005 before T006; T011 before T013.
- US1, US2 and US3 are independent apart from sharing `Program.cs` edits.
