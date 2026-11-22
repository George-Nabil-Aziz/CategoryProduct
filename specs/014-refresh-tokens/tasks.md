---

description: "Task list for Refresh Tokens (retroactive; all delivered in commit 277a46b)"
---

# Tasks: Refresh Tokens

**Input**: Design documents from `/specs/014-refresh-tokens/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: API tests included; MVC has no automated tests.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 issue at login, US2 rotation, US3 reuse detection, US4 logout, US5 MVC session

## Phase 1: Setup (Shared Infrastructure)

- [X] T001 [P] Create `CategoryProduct.API/Models/RefreshToken.cs`
- [X] T002 Add `DbSet<RefreshToken>` and entity mapping (unique `TokenHash`, max lengths, cascade FK) in `CategoryProduct.API/Data/ApplicationDbContext.cs`
- [X] T003 Add migration `CategoryProduct.API/Migrations/20261103183150_AddRefreshTokens.cs` (and updated `ApplicationDbContextModelSnapshot.cs`)
- [X] T004 [P] Create `CategoryProduct.API/Services/TokenService.cs` (access token, refresh token, SHA-256 `Hash`)
- [X] T005 Register `TokenService` as scoped and add the `refresh` rate-limit policy (`RateLimiting:RefreshPermitLimit`, default 30) in `CategoryProduct.API/Program.cs`
- [X] T006 [P] Extend `CategoryProduct.API/Dtos/LoginResponseDto.cs` with refresh fields and add `RefreshRequestDto`

## Phase 2: User Story 1 - Refresh token at login (Priority: P1)

- [X] T007 [US1] Refactor `Login` in `CategoryProduct.API/Controllers/AccountController.cs` to issue via `TokenService` and a shared `IssueAsync`
- [X] T008 [P] [US1] Test `Login_ReturnsRefreshToken` in `CategoryProduct.API.Tests/RefreshTokenTests.cs`
- [X] T009 [P] [US1] Set `RateLimiting:RefreshPermitLimit` to 1000 in `CategoryProduct.API.Tests/ApiFactory.cs`

## Phase 3: User Story 2 - Rotation (Priority: P1)

- [X] T010 [US2] Add `Refresh` action (lookup by hash, expiry and user checks, rotate with `ReplacedByTokenHash`) in `CategoryProduct.API/Controllers/AccountController.cs`
- [X] T011 [P] [US2] Tests `Refresh_ReturnsNewPair_AndNewAccessTokenWorks`, `UnknownRefreshToken_Returns401` in `CategoryProduct.API.Tests/RefreshTokenTests.cs`

## Phase 4: User Story 3 - Reuse detection (Priority: P1)

- [X] T012 [US3] Revoke all of the user's active refresh tokens when a revoked token is presented, in `CategoryProduct.API/Controllers/AccountController.cs`
- [X] T013 [P] [US3] Test `ReusedRefreshToken_Returns401_AndRevokesFamily` in `CategoryProduct.API.Tests/RefreshTokenTests.cs`

## Phase 5: User Story 4 - Logout (Priority: P2)

- [X] T014 [US4] Add `Logout` action (idempotent `204`) in `CategoryProduct.API/Controllers/AccountController.cs`
- [X] T015 [P] [US4] Test `Logout_RevokesRefreshToken` in `CategoryProduct.API.Tests/RefreshTokenTests.cs`

## Phase 6: User Story 5 - MVC transparent refresh (Priority: P2)

- [X] T016 [P] [US5] Create `CategoryProduct.MVC/Services/AuthSessionService.cs`
- [X] T017 [P] [US5] Create `CategoryProduct.MVC/Services/TokenRefreshHandler.cs`
- [X] T018 [US5] Extend `LoginResult`, add `LogoutAsync` in `CategoryProduct.MVC/Services/CategoryProductApiClient.cs`
- [X] T019 [US5] Register `AuthSessionService`, `TokenRefreshHandler`, `api-refresh` client and chain the handler in `CategoryProduct.MVC/Program.cs`
- [X] T020 [US5] Use `AuthSessionService` for sign-in and revoke the refresh token on logout in `CategoryProduct.MVC/Controllers/AccountController.cs`

## Dependencies & Execution Order

T001 -> T002 -> T003; T004 -> T007; T006 -> T007; T007 -> T010 -> T012; US5 depends on the API
endpoints (T010, T014). Tests T008-T015 all live in `RefreshTokenTests.cs`.

## Outstanding (not done)

- Automated and browser verification of the MVC refresh flow.
- Tests for expired token, deleted user, `429` on refresh.
