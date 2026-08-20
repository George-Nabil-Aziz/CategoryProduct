---

description: "Task list template for feature implementation"
---

# Tasks: JWT Authentication

**Input**: Design documents from `/specs/003-jwt-authentication/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Not requested in the feature specification. No automated test project exists in this
solution; verification is manual against the running API via Swagger UI (see `quickstart.md`),
per the constitution's Development Workflow. Manual-verification tasks are included per story
instead of automated test tasks.

**Organization**: Tasks are grouped by user story (both P1) to enable independent implementation
and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2)
- Include exact file paths in descriptions

## Path Conventions

Single existing project: `CategoryProduct.API/` at repository root. No new projects created.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Remove the pre-existing committed secret before any auth code is written against it

- [X] T001 Remove the committed literal `Jwt:Key` value from `CategoryProduct.API/appsettings.json`, leaving `Issuer`, `Audience`, and a new `ExpiryMinutes: 60` setting, with `Key` set to an empty string placeholder (per `research.md` Decision 3 — Constitution Principle IV)

**Checkpoint**: No real signing secret remains committed to source control.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure that MUST be complete before either user story can be implemented

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T002 Register JWT Bearer authentication in `CategoryProduct.API/Program.cs`: add `builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(...)` with `TokenValidationParameters` (`ValidateIssuer`, `ValidateAudience`, `ValidateIssuerSigningKey`, `ValidateLifetime` all `true`, `ClockSkew = TimeSpan.Zero`, key/issuer/audience read from the `Jwt` config section) and `builder.Services.AddAuthorization()` (per `research.md` Decisions 6–7)
- [X] T003 Fix the request pipeline in `CategoryProduct.API/Program.cs`: move `app.UseAuthentication()` then `app.UseAuthorization()` to run before `app.MapControllers()` and before `app.Run()`, and delete the existing unreachable `app.UseAuthentication()`/`app.UseAuthorization()` lines currently placed after `app.Run()` (depends on T002, same file)

**Checkpoint**: Authentication/authorization services and pipeline are correctly wired; `[Authorize]` attributes added later will now actually take effect.

---

## Phase 3: User Story 1 - Log in to obtain access (Priority: P1) 🎯 MVP

**Goal**: A consumer with a pre-provisioned account can submit username/password and receive a
JWT access credential; incorrect password or unknown username is rejected generically with no
credential issued.

**Independent Test**: Log in with a known valid username/password and confirm a usable access
credential is returned; separately confirm an incorrect password or unknown username is rejected
without issuing a credential, using the same generic response for both.

### Implementation for User Story 1

- [X] T004 [P] [US1] Rename `Password` to `PasswordHash` on the `User` model in `CategoryProduct.API/Models/User.cs` (per `data-model.md`)
- [X] T005 [US1] Add a new EF Core migration (`dotnet ef migrations add UpdateUsersTableForAuth` from `CategoryProduct.API/`) that renames the `Users.Password` column to `PasswordHash` and adds a unique index on `UserName` (depends on T004; per `research.md` Decision 5, Constitution Principle II)
- [X] T006 [P] [US1] Create `LoginRequestDto` record in `CategoryProduct.API/Dtos/LoginRequestDto.cs` with required `UserName` and `Password` string fields (`DataAnnotations`, matching the pattern in `CategoryRequestDto.cs`)
- [X] T007 [P] [US1] Create `LoginResponseDto` record in `CategoryProduct.API/Dtos/LoginResponseDto.cs` with `Token` (string) and `ExpiresAtUtc` (`DateTime`) fields
- [X] T008 [US1] Implement `POST /api/Account/login` in `CategoryProduct.API/Controllers/AccountController.cs`: inject `ApplicationDbContext`, look up the `User` by case-insensitive `UserName` match, verify the submitted password against `PasswordHash` using `Microsoft.AspNetCore.Identity.PasswordHasher<User>.VerifyHashedPassword`, and on success build a JWT via `JwtSecurityTokenHandler`/`JwtSecurityToken` with `ClaimTypes.NameIdentifier`/`ClaimTypes.Name` claims, `Jwt:Key`/`Issuer`/`Audience`/`ExpiryMinutes` from configuration, returning `200 OK` with `LoginResponseDto`; on any lookup or verification failure return `401 Unauthorized` with a generic body (no distinguishing detail) (depends on T004–T007; per `contracts/account-api.md`, `research.md` Decisions 1, 2, 6)
- [X] T009 [US1] Seed one pre-provisioned test account (hash a known password with `PasswordHasher<User>.HashPassword` and insert the row) for manual verification, per `quickstart.md` Prerequisites (depends on T005, T008)
- [X] T010 [US1] Manually verify all Login scenarios in `quickstart.md` (valid login, wrong password, unknown username, missing fields) against the running API via Swagger UI (depends on T008, T009)

**Checkpoint**: User Story 1 is fully functional and independently testable — login issues a valid credential; bad credentials are rejected generically.

---

## Phase 4: User Story 2 - Protected write access to Category and Product data (Priority: P1)

**Goal**: Category/Product `POST`/`PUT`/`DELETE` endpoints require a valid, unexpired access
credential; `GET` endpoints remain fully public.

**Independent Test**: Call any Category/Product create/update/delete operation with no
credential, with an invalid credential, and with a valid one obtained from User Story 1, and
confirm only the last case succeeds — while read access continues to work in all cases.

### Implementation for User Story 2

- [X] T011 [P] [US2] Add `[Authorize]` to the `Post`, `Put`, and `Delete` actions only in `CategoryProduct.API/Controllers/CategoryController.cs` (leave `Get`/`GetAllProducts` unattributed and public; per `contracts/protected-write-endpoints.md`, `research.md` Decision 4)
- [X] T012 [P] [US2] Add `[Authorize]` to the `Post`, `Put`, and `Delete` actions only in `CategoryProduct.API/Controllers/ProductController.cs` (leave `Get` actions unattributed and public; per `contracts/protected-write-endpoints.md`, `research.md` Decision 4)
- [X] T013 [US2] Manually verify all Protected write-endpoint scenarios in `quickstart.md` (no header, malformed token, valid token from User Story 1, expired token) for both `Category` and `Product`, plus the read-endpoint regression check, against the running API via Swagger UI (depends on T002, T003, T011, T012, and a valid token from US1/T010)

**Checkpoint**: Both user stories work independently and together — writes are protected, reads remain open, and the full flow (login → authenticated write) succeeds end-to-end.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately.
- **Foundational (Phase 2)**: Depends on Setup (T001, since `AddJwtBearer` reads the same `Jwt`
  config section) — BLOCKS User Story 2 (the `[Authorize]` attributes in US2 have no effect until
  T002/T003 are done). Does not block User Story 1 (login only issues tokens; it does not need
  the authentication middleware registered).
- **User Story 1 (Phase 3)**: Depends on Setup (T001) only. Can proceed in parallel with Phase 2.
- **User Story 2 (Phase 4)**: Depends on Foundational (Phase 2) for its `[Authorize]` attributes
  to take effect, and on User Story 1 (T010) for a valid token to test against (per the spec's own
  Independent Test wording for US2).

### Within Each User Story

- US1: model rename (T004) → migration (T005) → DTOs (T006, T007, parallel with T004/T005) →
  controller action (T008) → seed data (T009) → manual verification (T010).
- US2: `[Authorize]` additions (T011, T012, parallel, different files) → manual verification
  (T013).

### Parallel Opportunities

- T004, T006, T007 can run in parallel (different files, no shared dependency yet).
- T011 and T012 can run in parallel (different controller files).
- Phase 2 (Foundational) and Phase 3 (User Story 1) can proceed in parallel, since neither
  depends on the other's files.

---

## Parallel Example: User Story 1

```bash
# Launch independent file edits for User Story 1 together:
Task: "Rename Password to PasswordHash on the User model in CategoryProduct.API/Models/User.cs"
Task: "Create LoginRequestDto record in CategoryProduct.API/Dtos/LoginRequestDto.cs"
Task: "Create LoginResponseDto record in CategoryProduct.API/Dtos/LoginResponseDto.cs"
```

## Parallel Example: User Story 2

```bash
# Launch both controller edits together:
Task: "Add [Authorize] to Post/Put/Delete in CategoryProduct.API/Controllers/CategoryController.cs"
Task: "Add [Authorize] to Post/Put/Delete in CategoryProduct.API/Controllers/ProductController.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001).
2. Complete Phase 3: User Story 1 (T004–T010).
3. **STOP and VALIDATE**: Login issues and rejects credentials correctly, per `quickstart.md`.
4. This is a usable increment on its own, though write endpoints remain unprotected until Phase 4.

### Incremental Delivery

1. Setup (T001) → Foundational (T002–T003) can start immediately alongside User Story 1.
2. User Story 1 (T004–T010) → validate login independently.
3. User Story 2 (T011–T013) → validate write-endpoint protection, using a token from Story 1.
4. Both stories together satisfy the full feature (SC-001 through SC-004).

---

## Notes

- [P] tasks = different files, no dependencies.
- [Story] label maps task to specific user story for traceability.
- Both user stories are Priority P1 — Story 2 has a soft ordering dependency on Story 1 only for
  obtaining a token to test with, per the spec's own Independent Test wording; the underlying code
  changes (Foundational + US2 attributes) do not require US1's code to compile or exist.
- Commit after each task or logical group.
- Stop at either checkpoint to validate that story independently.
