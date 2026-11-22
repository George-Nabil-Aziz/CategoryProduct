---

description: "Task list for Health Checks (retroactive; all delivered in commit 68c11e4)"
---

# Tasks: Health Checks

**Input**: Design documents from `/specs/012-health-checks/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: One automated test (healthy path) is included.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to

## Phase 1: Setup (Shared Infrastructure)

- [X] T001 Add `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` 10.0.10 to `CategoryProduct.API/CategoryProduct.API.csproj`

## Phase 2: User Story 1 - Probe service and database health (Priority: P1)

- [X] T002 [P] [US1] Create `CategoryProduct.API/Infrastructure/HealthCheckResponseWriter.cs` (JSON body: status, checks, totalDurationMs)
- [X] T003 [US1] Register `AddHealthChecks().AddDbContextCheck<ApplicationDbContext>("database")` in `CategoryProduct.API/Program.cs`
- [X] T004 [US1] Map `/health` with the custom writer and `.AllowAnonymous()` in `CategoryProduct.API/Program.cs`
- [X] T005 [P] [US1] Add `Health_ReturnsHealthyWithDatabaseCheck` in `CategoryProduct.API.Tests/HealthTests.cs`

## Phase 3: User Story 2 - Quiet probe logging (Priority: P2)

- [X] T006 [US2] Configure `UseSerilogRequestLogging` `GetLevel` (Error for failures, Verbose for `/health`, else Information) in `CategoryProduct.API/Program.cs`

## Dependencies & Execution Order

T001 -> T003; T002 -> T004; T003 -> T004 -> T005; T006 is independent of T002-T005.
