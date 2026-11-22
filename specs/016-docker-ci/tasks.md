---

description: "Task list for 016-docker-ci"
---

# Tasks: Docker and CI

**Input**: Design documents from `/specs/016-docker-ci/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/environment-and-compose.md

**Tests**: One API integration test file is required (FR-011). Docker and workflow files cannot be run locally (Known Gaps); validate by review.

## Format: `[ID] [P?] [Story] Description`

## Phase 1: Setup

- [ ] T001 [P] Add `.env` (and `.env.*` except `.env.example`) ignore rules to `.gitignore`
- [ ] T002 [P] Create root `.dockerignore` excluding `**/bin/`, `**/obj/`, `.git/`, `.vs/`, `.env`, `**/logs/`, `specs/`, `.specify/`, `.claude/`
- [ ] T003 [P] Create `.env.example` with placeholder `SA_PASSWORD`, `JWT_KEY`, `API_HOST_PORT`, `MVC_HOST_PORT` and comments on complexity rules and forbidden characters

---

## Phase 2: Foundational (Blocking Prerequisites)

- [ ] T004 Add `"Database": { "MigrateOnStartup": false }` to `CategoryProduct.API/appsettings.json` (FR-005, FR-006)
- [ ] T005 [P] Create `CategoryProduct.API/Data/DatabaseMigrator.cs` with `MigrateIfEnabledAsync` (flag check, `IsRelational()` guard, 10 attempts x 3 s retry, logging) (FR-005)

**Checkpoint**: `dotnet build CategoryProduct.slnx` succeeds.

---

## Phase 3: User Story 2 - Migrations auto-apply only inside the container (Priority: P1)

**Goal**: Flag-controlled startup migration, skipped for non-relational providers.

**Independent Test**: `dotnet test` passes with the new tests.

- [ ] T006 [US2] Wire `await DatabaseMigrator.MigrateIfEnabledAsync(app.Services, app.Configuration, app.Logger)` in `CategoryProduct.API/Program.cs` after `app.MapHealthChecks(...)` and before `app.Run()`
- [ ] T007 [P] [US2] Create `CategoryProduct.API.Tests/MigrationStartupTests.cs`: (a) helper called with flag true over an InMemory DbContext completes without exception and without touching a relational database; (b) a factory subclass of `ApiFactory` setting `Database:MigrateOnStartup=true` boots and `/health` returns 200; (c) default `ApiFactory` (flag unset) boots and `/health` returns 200
- [ ] T008 [US2] Run `dotnet test CategoryProduct.slnx -c Release` and confirm all existing and new tests pass

---

## Phase 4: User Story 3 - Container-friendly HTTP hosting (Priority: P2)

**Goal**: No HTTPS redirect/HSTS and a working cookie over HTTP in the container, unchanged defaults locally.

- [ ] T009 [P] [US3] Gate `app.UseHttpsRedirection()` with `Hosting:UseHttpsRedirection` (default true) in `CategoryProduct.API/Program.cs`
- [ ] T010 [P] [US3] In `CategoryProduct.MVC/Program.cs` gate `UseHttpsRedirection()` and `UseHsts()` with `Hosting:UseHttpsRedirection` (default true) and read `Cookie:SecurePolicy` (default `Always`) for `o.Cookie.SecurePolicy`
- [ ] T011 [P] [US3] Create `CategoryProduct.API/Dockerfile` (multi-stage sdk:10.0 to aspnet:10.0, repo-root context, `ASPNETCORE_HTTP_PORTS=8080`, `EXPOSE 8080`, `/app/logs` writable, `USER $APP_UID`)
- [ ] T012 [P] [US3] Create `CategoryProduct.MVC/Dockerfile` (same pattern, MVC project only)

---

## Phase 5: User Story 1 - Run the whole stack with one command (Priority: P1) MVP

**Goal**: `docker compose up --build` yields a working stack.

- [ ] T013 [US1] Create `docker-compose.yml` with `sql` (healthcheck, `sqldata` volume), `api` (depends on `sql` healthy; env per contract), `mvc` (`Api__BaseUrl=http://api:8080/`; depends on `api` started), using `${VAR:?}` for required secrets (FR-003, FR-004, FR-007, FR-008, FR-009)
- [ ] T014 [US1] Review compose + Dockerfiles against `contracts/environment-and-compose.md` (service names, ports, env keys, no secrets baked in); record in the PR that Docker was not run locally

---

## Phase 6: User Story 4 - Continuous integration (Priority: P2)

- [ ] T015 [US4] Create `.github/workflows/ci.yml`: triggers `push` and `pull_request`; job `build-test` (checkout, `actions/setup-dotnet` `10.0.x`, restore, build Release `--no-restore`, test Release `--no-build`); job `docker-build` (checkout, `docker build -f CategoryProduct.API/Dockerfile .`, same for MVC, no push) (FR-010)
- [ ] T016 [US4] Validate YAML syntax of `ci.yml` and `docker-compose.yml` with a parser (Docker/gh unavailable)

---

## Phase 7: Polish

- [ ] T017 [P] Search tracked files for real secrets (JWT key, passwords); confirm only placeholders exist and `git check-ignore .env` reports ignored (FR-012)
- [ ] T018 [P] Confirm `launchSettings.json`, user-secrets flow, and existing tests are unchanged in behavior (FR-013, SC-005)
- [ ] T019 Walk through `quickstart.md` by review; tick `checklists/requirements.md`

---

## Dependencies & Execution Order

- Setup (T001-T003) has no dependencies and runs in parallel.
- Foundational (T004-T005) precedes US2 (T006-T008).
- US3 (T009-T012) is independent of US2 except that T006 and T009 both edit `CategoryProduct.API/Program.cs`; do them sequentially.
- US1 (T013-T014) needs the Dockerfiles (T011, T012) and the flags (T004, T009, T010).
- US4 (T015-T016) needs the Dockerfiles.
- Polish last.

## Implementation Strategy

MVP is Setup, Foundational, US2, US3, US1. Then CI. Because Docker cannot be run here, every Docker/CI task is validated by review only; the C# tasks are verified with `dotnet build` and `dotnet test`.
