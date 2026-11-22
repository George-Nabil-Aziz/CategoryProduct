---

description: "Task list for 024-email-notifications"
---

# Tasks: Email Notifications

**Input**: Design documents from `/specs/024-email-notifications/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Requested. Each story writes its failing tests first, then the change. No fixed sleeps: await the recording sender or `EmailQueue.WaitForIdleAsync`.

**Organization**: grouped by user story. US2 (transport, queue, worker) is the foundation and goes first; US1 (user email) is independent of it except for shared files (`Program.cs`, `ApiFactory.cs`); US3 and US4 build on both. The final verification comes last.

## File ownership (for parallel work)

| Story | Production files | Test files |
|---|---|---|
| US2 foundation | `API/Services/{EmailAddressRules,EmailMessage,IEmailSender,EmailOptions,PickupDirectoryEmailSender,SmtpEmailSender,EmailQueue,EmailDispatchWorker}.cs`, `API/Program.cs`, `appsettings*.json`, `API/Dockerfile`, `docker-compose.yml`, `.env.example` | `APIT/{EmailAddressRulesTests,PickupDirectoryEmailSenderTests,EmailQueueTests,EmailOptionsTests,RecordingEmailSender,ApiFactory}.cs` |
| US1 user email | `API/Models/User.cs`, `API/Data/ApplicationDbContext.cs`, `API/Migrations/*AddUserEmail*`, `API/Dtos/*`, `API/Controllers/{AccountController,UsersController}.cs`, MVC `Models`, `Services/CategoryProductApiClient.cs`, `Controllers/AccountController.cs`, `Views/Account/*`, `Views/Users/Index.cshtml`, `Views/Shared/_Layout.cshtml` | `APIT/UserEmailTests.cs`, `SQLT/UserEmailIndexSqlServerTests.cs`, `MVCT/{AccountTests,UsersTests,ProfileTests}.cs` |
| US3 order mails | `API/Services/NotificationService.cs` (order part), `API/Controllers/OrdersController.cs`, `API/Program.cs` (registration) | `APIT/OrderEmailTests.cs` |
| US4 low stock | `API/Services/NotificationService.cs` (low-stock part), `API/Controllers/{ProductController,OrdersController}.cs` | `APIT/LowStockEmailTests.cs` |

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency)
- **[Story]**: US1..US5 from spec.md
- Paths relative to the repository root. `API` = `CategoryProduct.API`, `APIT` = `CategoryProduct.API.Tests`, `SQLT` = `CategoryProduct.API.SqlServerTests`, `MVC` = `CategoryProduct.MVC`, `MVCT` = `CategoryProduct.MVC.Tests`.

---

## Phase 1: Setup

- [ ] T001 Record the baseline: `dotnet build CategoryProduct.slnx` and `dotnet test CategoryProduct.slnx` (expected 560 tests: 314 MVC, 246 API); `git status` clean; note the branch.
- [ ] T002 [P] Re-verify the spec's code claims still hold: `User` has no email; `RegisterRequestDto`/`UserResponseDto` shapes; `InventoryService.LogIfLowStock` rule (`< Threshold`); `OrdersController` `Cancel`/`SetStatus`/`Checkout` flow and that `order.User` is loaded in cancel and confirm; `ProductImageStorage` path resolution; `ApiFactory` override points; MVC `ApiResult`. Note any difference in spec Assumptions.

---

## Phase 2: User Story 2 - Safe email transport (P1, foundation)

**Independent Test**: `dotnet test CategoryProduct.API.Tests --filter "FullyQualifiedName~EmailAddressRulesTests|FullyQualifiedName~PickupDirectoryEmailSenderTests|FullyQualifiedName~EmailQueueTests|FullyQualifiedName~EmailOptionsTests"`

- [ ] T003 [P] [US2] Write `APIT/EmailAddressRulesTests.cs` FIRST: valid (`a@b.co`, upper case normalized to lower, trimmed), none (null, empty, whitespace -> null without error), invalid (`a`, `a@b`, `@b.co`, `A <a@b.co>`, 255 chars, inner space, CR, LF, tab). (FR-002)
- [ ] T004 [P] [US2] Write `APIT/PickupDirectoryEmailSenderTests.cs` FIRST (temp dir deleted in `Dispose`): file created in a missing directory; name pattern and uniqueness for two messages; headers and body content; UTF-8 body round trip; non-ASCII subject is encoded; relative path resolves against a fake `IHostEnvironment.ContentRootPath`, absolute path used as is; CR/LF subject rejected by `EmailMessage.Validate()`; a read-only/invalid directory throws (the worker's catch handles it). (FR-008, FR-012)
- [ ] T005 [P] [US2] Write `APIT/EmailQueueTests.cs` FIRST: `TryEnqueue` false when disabled, invalid message, or full (use a small capacity constructor/internal setting for the test); true otherwise; worker sends in order; a throwing sender does not stop later messages and `WaitForIdleAsync` still completes; the idle signal completes without delays; `StopAsync` drains queued messages within the limit; logs contain no body and a masked recipient (collecting logger like `BootstrapAdminTests.CollectingLogger`). (FR-011, FR-013)
- [ ] T006 [P] [US2] Write `APIT/EmailOptionsTests.cs` FIRST: provider selection (`Pickup`, `Smtp` with host, `Smtp` without host -> pickup + Error, unknown -> pickup + Error, case-insensitive); empty-string `Enabled`/`Port`/`EnableSsl` bind as defaults (research R4); defaults when the section is missing. (FR-007, FR-010)
- [ ] T007 [US2] Create `API/Services/EmailAddressRules.cs`, `EmailMessage.cs` (record + `Validate`), `IEmailSender.cs`, `EmailOptions.cs` (+ `SmtpOptions`) per `contracts/email-sender.md` and `contracts/email-config.md`. (FR-002, FR-006, FR-007, FR-012)
- [ ] T008 [US2] Create `API/Services/PickupDirectoryEmailSender.cs` per the contract (path resolution copied from `ProductImageStorage`, `FileMode.CreateNew`, RFC 2047 for non-ASCII subjects). (FR-008)
- [ ] T009 [US2] Create `API/Services/SmtpEmailSender.cs` (`System.Net.Mail`, no new package; credentials only when `UserName` set; no secret in logs or messages). Not covered by a network test; a unit test may build the `MailMessage` through an internal factory method if one is added. (FR-009)
- [ ] T010 [US2] Create `API/Services/EmailQueue.cs` and `EmailDispatchWorker.cs` per the contract (bounded 1000, `TryWrite`, pending counter, idle signal, 5 s shutdown drain, masked logging). (FR-011, FR-013)
- [ ] T011 [US2] `API/Program.cs`: bind `EmailOptions`; register the sender factory (selection and fallbacks, Information log of the choice), `EmailQueue` singleton, `AddHostedService<EmailDispatchWorker>()`. `appsettings.json` (Email section, `Enabled: false`) and `appsettings.Development.json` (`Enabled: true`). (FR-010, FR-020)
- [ ] T012 [US2] `APIT/RecordingEmailSender.cs` (thread-safe list, `WaitForCountAsync(n, timeout)`, optional `ThrowOnSend`) and `APIT/ApiFactory.cs`: `Email:Enabled=true`, `Email:PickupDirectory` under a temp path, `RemoveAll<IEmailSender>()` + register the recorder singleton, expose it (`Emails`), and expose the `EmailQueue` for `WaitForIdleAsync`. Default behavior of existing tests unchanged; run `dotnet test CategoryProduct.API.Tests` quickly to confirm. (FR-021)
- [ ] T013 [P] [US2] `docker-compose.yml` (api environment lines, `emailoutbox` volume and top-level volume), `API/Dockerfile` (`/data/email-outbox` mkdir + chown), `.env.example` (commented placeholders) per `contracts/email-config.md`. `docker compose config` may be unavailable: review the YAML by eye (indentation, no `:?`). (FR-019)
- [ ] T014 [US2] Run the filter above; all green; then the whole `CategoryProduct.API.Tests` project.

**Checkpoint (US2)**.

---

## Phase 3: User Story 1 - User email (P1)

**Independent Test**: `dotnet test CategoryProduct.API.Tests --filter "FullyQualifiedName~UserEmailTests"` and `dotnet test CategoryProduct.MVC.Tests --filter "FullyQualifiedName~ProfileTests|FullyQualifiedName~AccountTests|FullyQualifiedName~UsersTests"`

- [ ] T015 [P] [US1] Write `APIT/UserEmailTests.cs` FIRST per `contracts/profile-endpoints.md`: register with valid/absent/null/empty/whitespace email; invalid variants -> 400 `Email`; duplicate with a different letter case -> 409; me (own data, 401 anonymous); put (set, change, same value 200, clear, invalid, duplicate 409, 401); two users cannot read each other's email; Admin list includes email; plain user 403; JSON of orders, stock movements and login has no `email` property. (Scenarios 1-8)
- [ ] T016 [P] [US1] Write `SQLT/UserEmailIndexSqlServerTests.cs`: own database, `MigrateAsync`; two users with NULL email are accepted; two users with the same email fail with a unique violation; same email different case in the stored lower-case form fails; `HasPendingModelChanges()` false. Follow `SqlServerApiFactory`/`MigrationTests` helpers. (SC-005)
- [ ] T017 [P] [US1] MVC tests FIRST: `MVCT/AccountTests.cs` additions (register form has the Email input; valid email sent in the request JSON; empty email omitted or null; 400 and 409 email errors shown on the Email field; existing username 409 unchanged), new `MVCT/ProfileTests.cs` (anonymous redirect, GET shows current email and calls `api/account/me`, POST success -> redirect plus success message and request JSON, clear, 400/409 re-render with field error, 401 -> sign-out and login redirect, 429 -> `ApiRateLimit.Message`, antiforgery required), `MVCT/UsersTests.cs` (Email column renders values and empty cells). (Scenarios 9-11)
- [ ] T018 [US1] `API/Models/User.cs` (`Email`) and `API/Data/ApplicationDbContext.cs` (`HasMaxLength(254)`, unique filtered index). (FR-001)
- [ ] T019 [US1] Create the migration: `dotnet ef migrations add AddUserEmail --project CategoryProduct.API`. Confirm it only adds the column and `IX_Users_Email` (with the `IS NOT NULL` filter); the snapshot diff is limited to the same; nothing else edited; `Down` reverses. (FR-001, Principle II)
- [ ] T020 [US1] DTOs: `RegisterRequestDto` (+ `Email`), `UserResponseDto` (+ `Email`), new `API/Dtos/ProfileDtos.cs`. Fix compile sites of `UserResponseDto` (positional constructor calls) in `AccountController` and `UsersController`. (FR-003, FR-004, FR-005)
- [ ] T021 [US1] `API/Controllers/AccountController.cs`: register email validation/duplicate/409, `GET me`, `PUT email` (`[EnableRateLimiting("profile")]`); `API/Program.cs`: `profile` policy with `RateLimiting:ProfilePermitLimit` (default 30); `APIT/ApiFactory.cs`: `RateLimiting:ProfilePermitLimit=1000`. Email uniqueness via query plus `DbUpdateException` catch. (FR-003, FR-005)
- [ ] T022 [US1] `API/Controllers/UsersController.cs`: list projection and role response carry `Email`. (FR-004)
- [ ] T023 [P] [US1] MVC models and client: `RegisterViewModel.Email`, `UserItem.Email`, `ProfileViewModel`; `CategoryProductApiClient.RegisterAsync(userName, password, email)`, `GetMeAsync`, `SetEmailAsync` (existing `GetAsync`/`SendAsync` helpers and `ApiResult` mapping); update the callers of `RegisterAsync`. (FR-018)
- [ ] T024 [US1] MVC UI: `AccountController` Register error mapping for the Email field, `Profile` GET/POST; `Views/Account/Register.cshtml` (Email input), `Views/Account/Profile.cshtml`, `Views/Users/Index.cshtml` (Email column), `Views/Shared/_Layout.cshtml` ("Profile" link). (FR-018)
- [ ] T025 [US1] Run the API and MVC filters above; all green, including the whole `CategoryProduct.MVC.Tests` project (existing register/users tests keep their expectations) and `dotnet build CategoryProduct.API.SqlServerTests` (the new SQL Server test compiles).

**Checkpoint (US1)**.

---

## Phase 4: User Story 3 - Order emails (P1)

**Independent Test**: `dotnet test CategoryProduct.API.Tests --filter "FullyQualifiedName~OrderEmailTests"`

- [ ] T026 [US3] Write `APIT/OrderEmailTests.cs` FIRST per spec scenarios 1-9 using `ApiFactory` + recorder: user with email (set through `PUT api/account/email` or a seeded user), checkout -> one email with subject/body content (order id, items, total, Pending); no email for a user without one; failed checkouts queue nothing; Admin confirm -> mail to the owner only; user cancel and Admin cancel (both endpoints) -> mail to the owner; rejected transitions and 404 queue nothing; throwing recorder (`ThrowOnSend`) still gives 201/200; a product name with markup/newline stays in the body and does not appear in the subject; negative checks use `WaitForIdleAsync`. (Scenarios 1-9)
- [ ] T027 [US3] Create `API/Services/NotificationService.cs` (order part: `OrderPlacedAsync`, `OrderStatusChangedAsync`, builders per `contracts/email-messages.md`, catch-all) and register it scoped in `API/Program.cs`. (FR-014, FR-017)
- [ ] T028 [US3] `API/Controllers/OrdersController.cs`: inject `NotificationService`; call `OrderPlacedAsync` once after the commit and `order.User` fix-up in `Checkout`; call `OrderStatusChangedAsync` for a successful cancel (shared by `Cancel` and the Cancelled branch of `SetStatus`) and for a successful confirm. No call on failure paths. (FR-015)
- [ ] T029 [US3] Run the filter above; all green; then the whole `CategoryProduct.API.Tests` project (existing order tests keep passing).

**Checkpoint (US3)**.

---

## Phase 5: User Story 4 - Low-stock alert (P2)

**Independent Test**: `dotnet test CategoryProduct.API.Tests --filter "FullyQualifiedName~LowStockEmailTests"`

- [ ] T030 [US4] Write `APIT/LowStockEmailTests.cs` FIRST per spec scenarios 1-9: two Admins with email plus one without (create extra Admins through the DB scope or `PUT api/users/{id}/role`), crossing 6 -> 4 with threshold 5; already below -> none; exactly at threshold -> none; increase -> none; threshold 0 -> none (factory subclass overriding `LowStockThreshold`); checkout crossing two products -> one digest per Admin; restock above then cross again -> new mail; no Admin with email -> none; order cancel restock -> none. (Scenarios 1-9)
- [ ] T031 [US4] `API/Services/NotificationService.cs`: add `LowStockAsync` (crossing filter with `InventoryOptions.Effective`, single Admin query only when a crossing exists, one digest message per Admin, `Product` navigation for names). (FR-014, FR-016)
- [ ] T032 [US4] `API/Controllers/ProductController.cs` (`PostStockMovement` Ok path) and `API/Controllers/OrdersController.cs` (`Checkout`, with the `movements` list) call `LowStockAsync` after the save; `InventoryService.LogIfLowStock` untouched. Run the filter, then the whole API test project. (FR-016)

**Checkpoint (US4)**.

---

## Phase 6: Review and secret hygiene

- [ ] T033 Grep the diff and tracked files for secrets and hosts: no SMTP user/password/host value anywhere; `.env.example` placeholders are commented; `appsettings*.json` Smtp values empty; no `.env`, `logs/`, `App_Data/` or pickup files added. (SC-004, Principle IV)
- [ ] T034 `git status` review against the "Source changes" list of plan.md: only the listed files; existing migrations untouched; `CategoryProduct.slnx` and the CI workflow unchanged; the new migration only adds the column and index; no existing test expectation changed (only additions and helper edits). (FR-022, SC-006)

---

## Phase 7: User Story 5 - Final verification and Known Gaps (P3)

- [ ] T035 Run `dotnet build CategoryProduct.slnx` (0 errors, no new warnings in touched files) and `dotnet build CategoryProduct.API.SqlServerTests`.
- [ ] T036 Run `dotnet test CategoryProduct.slnx` THREE times in a row; all green each time; record the test counts (more than the 560 baseline).
- [ ] T037 Complete `specs/024-email-notifications/spec.md` Known Gaps with the final facts (test counts, whether the SQL Server test ran locally or only on CI, migration name, deviations) and tick `checklists/requirements.md`.
- [ ] T038 After pushing (when the user pushes), confirm `build-test`, `docker-build`, `sqlserver-tests` (the new index test) and `compose-smoke` are green and record the run in Known Gaps.

---

## Dependencies and execution order

- T001 first; T002 any time.
- US2 (T003-T014) before US3/US4. Inside US2: tests T003-T006 first; T007 before T008-T010; T011 after T007-T010; T012 after T010-T011; T013 independent.
- US1 (T015-T025) is independent of US2 except for `Program.cs` (T011, T021) and `ApiFactory.cs` (T012, T021): do those edits sequentially. Inside US1: tests T015-T017 first; T018 before T019; T019 before T020; T020 before T021/T022; T023 before T024.
- US3 (T026-T029) after US2 and after T021 (tests need a way to give a user an email). US4 (T030-T032) after T029 (same controller and service files).
- T033-T034 after all code; T035-T038 last.

## Parallel example

Agent A: T003-T014 (transport). Agent B: T015-T025 (user email; coordinate the `Program.cs` and `ApiFactory.cs` edits with A). Then one agent: T026-T032, then T033-T038.

## Implementation strategy

MVP = US2 + US1 + US3 (an address, a safe transport, order mails). US4 adds the Admin alert. Keep every existing test green at each checkpoint; they are the regression net for the DTO and controller changes.
