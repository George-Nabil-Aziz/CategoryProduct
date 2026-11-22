---

description: "Task list for 026-payments"
---

# Tasks: Payments

**Input**: Design documents from `/specs/026-payments/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Requested. Each story writes its failing tests first, then the change. No fixed sleeps: races use the interceptor pattern, emails use `EmailQueue.WaitForIdleAsync`.

**Organization**: grouped by user story. Primitives (options, tokens, gateway) and the model/migration are the foundation; US1+US2 (pay) build on them; US3 (refund) extends `CancelCoreAsync`; US4 splits into API reads (Phase 6) and MVC (Phase 8); US5 (emails) after the pay and refund flows; US6 (provider safety, Known Gaps) is spread through Phase 2 and Phase 9.

## File ownership (for parallel work)

| Area | Production files | Test files |
|---|---|---|
| Primitives | `API/Services/{PaymentOptions,PaymentTokenRules,IPaymentGateway,FakePaymentGateway}.cs`, `API/Program.cs` (registration), `appsettings.json`, `docker-compose.yml`, `.env.example` | `APIT/{PaymentOptionsTests,PaymentTokenRulesTests,FakePaymentGatewayTests}.cs` |
| Model + migration | `API/Models/{Payment,OrderPaymentStatus,Order}.cs`, `API/Data/ApplicationDbContext.cs`, `API/Migrations/*AddPayments*` | (covered by later tests and SQLT) |
| Pay (US1, US2) | `API/Services/PaymentService.cs`, `API/Dtos/PaymentDtos.cs`, `API/Controllers/OrdersController.cs` (Pay), `API/Program.cs` (limiter, service) | `APIT/{PaymentTests,PaymentIdempotencyTests,PaymentConcurrencyTests}.cs`, `APIT/ApiFactory.cs`, `APIT/CartOrderTestHelpers.cs` |
| Refund (US3) | `API/Services/PaymentService.cs` (refund part), `API/Controllers/OrdersController.cs` (`CancelCoreAsync`) | `APIT/PaymentRefundTests.cs` |
| Reads (US4 API) | `API/Dtos/OrderResponseDto.cs`, `API/Infrastructure/OrderMappingExtensions.cs`, `API/Controllers/OrdersController.cs` (`ListAsync`, `GetAll`, `GetPayments`) | `APIT/PaymentReadTests.cs` |
| Emails (US5) | `API/Services/NotificationService.cs` (payment part), `API/Services/PaymentService.cs` (calls) | `APIT/PaymentEmailTests.cs` |
| MVC (US4 UI) | `MVC/Models/OrderModels.cs`, `MVC/Services/CategoryProductApiClient.cs`, `MVC/Controllers/OrdersController.cs`, `MVC/Views/Orders/*`, `MVC/Views/Shared/_PaymentStatusBadge.cshtml` | `MVCT/PaymentTests.cs`, `MVCT/Support/ApiJson.cs` |
| SQL Server | none | `SQLT/PaymentIndexSqlServerTests.cs` |

Shared files (`Program.cs`, `ApiFactory.cs`, `OrdersController.cs`, `NotificationService.cs`, `ApplicationDbContext.cs`) are edited sequentially by one agent at a time.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency)
- **[Story]**: US1..US6 from spec.md
- Paths relative to the repository root. `API` = `CategoryProduct.API`, `APIT` = `CategoryProduct.API.Tests`, `SQLT` = `CategoryProduct.API.SqlServerTests`, `MVC` = `CategoryProduct.MVC`, `MVCT` = `CategoryProduct.MVC.Tests`.

---

## Phase 1: Setup

- [X] T001 Record the baseline: `dotnet build CategoryProduct.slnx` and `dotnet test CategoryProduct.slnx` (note the test counts); `git status`; note the branch and whether feature 025 (password management) is committed yet.
- [X] T002 [P] Re-read the files 025 may have changed (`API/Program.cs`, `APIT/ApiFactory.cs`, `API/Controllers/AccountController.cs`, `API/Services/NotificationService.cs`, `API/Data/ApplicationDbContext.cs`, `appsettings.json`, `docker-compose.yml`, `.env.example`) and re-verify the spec's code claims: `Order`/`OrderStatus` shapes; `OrdersController` `Checkout`/`Cancel`/`SetStatus`/`CancelCoreAsync` flow (retry loop, `IsRelational()` transaction, order attached after products staged); `OrderResponseDto`/`OrderSummaryDto` are only constructed in `OrderMappingExtensions.ToResponse` and `ListAsync` (grep `new OrderResponseDto|new OrderSummaryDto|OrderSummaryDto(`); `CartRaceInterceptor` trigger conditions; `NotificationService` guards; `profile` rate-limit policy partition key; existing latest migration name; MVC `ApiJson.Order`.

---

## Phase 2: Primitives - options, tokens, gateway (foundation; US6 provider safety)

**Independent Test**: `dotnet test CategoryProduct.API.Tests --filter "FullyQualifiedName~PaymentOptionsTests|FullyQualifiedName~PaymentTokenRulesTests|FullyQualifiedName~FakePaymentGatewayTests"`

- [X] T003 [P] [US6] Write `APIT/PaymentOptionsTests.cs` FIRST: defaults when the section is missing (`Fake`, `USD`); case-insensitive `fake`; empty `Provider`/`Currency` bind to defaults; `usd` upper-cased; invalid currency (`US`, `USDX`, `12A`) -> `InvalidOperationException`; unknown provider (`Stripe`) -> `InvalidOperationException` containing `Supported: Fake`; a `WebApplicationFactory` start with `Payments:Provider=Stripe` fails to start. (FR-004, FR-013, research R3, R12)
- [X] T004 [P] [US1] Write `APIT/PaymentTokenRulesTests.cs` FIRST: valid `tok_visa`, `tok_a`, 60-char suffix; invalid empty, null, whitespace, `visa`, `tok_`, 61-char suffix, `tok_a b`, `tok_é`; PAN-like `4242424242424242`, `4242 4242 4242 4242`, `4242-4242-4242-4242` -> the card-number message; messages never contain the input; key rules (null -> generated 32-hex, valid kept, 65 chars, bad characters rejected). (FR-003, contracts/payment-config.md)
- [X] T005 [P] [US1] Write `APIT/FakePaymentGatewayTests.cs` FIRST: approve default (`tok_visa`, `tok_mastercard`); `tok_decline`, `tok_insufficient_funds` decline with codes; `tok_error` throws; same idempotency key -> same reference twice; different keys -> different references; `tok_refund_fail` reference starts `fake_chf_` and its refund fails; normal refund succeeds and is deterministic per refund key; unknown reference refund fails; invalid token declined `invalid_token`; concurrent calls (Parallel.For) give identical results. (FR-001, FR-002)
- [X] T006 [US6] Create `API/Services/PaymentOptions.cs` (`Provider`, `Currency`, normalization, `Validate` throwing `InvalidOperationException`) per `contracts/payment-config.md`. (FR-004, FR-013)
- [X] T007 [US1] Create `API/Services/PaymentTokenRules.cs` (token and idempotency key validation returning the error message or the normalized value). (FR-003)
- [X] T008 [US1] Create `API/Services/IPaymentGateway.cs` (interface + the four records) per `contracts/payment-gateway.md`. (FR-001)
- [X] T009 [US1] Create `API/Services/FakePaymentGateway.cs` per the contract (SHA256 references, prefixes, no clock/random). (FR-002)
- [X] T010 [US6] Edit `API/Program.cs` (re-read it first, see the final note): bind `PaymentOptions`, validate at startup, register the gateway singleton by provider switch (unknown -> throw, Fake -> log Warning once), register `PaymentService` scoped (after T020). (FR-004)
- [X] T011 [P] [US6] Edit `API/appsettings.json` (`"Payments": { "Provider": "Fake", "Currency": "USD" }`), `docker-compose.yml` and `.env.example` (optional `Payments__Provider`/`Payments__Currency`, empty or commented, no secrets). (FR-020)

---

## Phase 3: Model, DbContext and migration (foundation)

- [X] T012 [US1] Create `API/Models/Payment.cs` (+ `PaymentState` enum), `API/Models/OrderPaymentStatus.cs` (+ `TryParseName` like `OrderStatusExtensions`); edit `API/Models/Order.cs` (`PaymentStatus = Unpaid`, `Payments` list) per `data-model.md`. (FR-005, FR-018)
- [X] T013 [US2] Edit `API/Data/ApplicationDbContext.cs` (re-read it first): `DbSet<Payment>`; `Payment` config (string conversions, `decimal(18,2)`, max lengths, `ConcurrencyStamp` concurrency token, FK Restrict, unique `IdempotencyKey`, filtered unique `OrderId` where `[Status] IN ('Pending','Succeeded')`, list index); `Order.PaymentStatus` string conversion max 20, `HasDefaultValue(OrderPaymentStatus.Unpaid)`. (FR-005, FR-007)
- [X] T014 [US1] Create `API/Dtos/PaymentDtos.cs` (`PayOrderRequestDto`, `PaymentResponseDto`) and a `Payment.ToResponse()` mapping (in `OrderMappingExtensions` or a new `PaymentMappingExtensions`). (FR-006, FR-009)
- [X] T015 [US1] Build `dotnet build CategoryProduct.slnx`; existing tests must still compile and pass before the migration (`dotnet test CategoryProduct.API.Tests` quick run).
- [X] T016 [US2] Generate the migration: `dotnet ef migrations add AddPayments --project CategoryProduct.API` (never hand-edit). (FR-015, Principle II)
- [X] T017 [US2] Review the generated migration: creates `Payments` with both unique indexes (filter text correct), adds `Orders.PaymentStatus` with `defaultValue: "Unpaid"`, touches nothing else; snapshot diff only adds these; `Database.HasPendingModelChanges()` false (verified later by the SQL Server test).

---

## Phase 4: User Story 1 + 2 - Pay once (P1)

**Independent Test**: `dotnet test CategoryProduct.API.Tests --filter "FullyQualifiedName~PaymentTests|FullyQualifiedName~PaymentIdempotencyTests|FullyQualifiedName~PaymentConcurrencyTests"`

- [X] T018 [US2] Edit `APIT/ApiFactory.cs` (re-read first): `builder.UseSetting("RateLimiting:PaymentsPermitLimit", "1000")`; a `protected virtual` hook (e.g. `ConfigureGateway(IServiceCollection)`) default registering nothing, plus a small `CountingPaymentGateway` decorator class (counts charge/refund calls, wraps the real `FakePaymentGateway`, optional `BeforeCharge` delegate to block/throw) in `APIT/`. Edit `APIT/CartOrderTestHelpers.cs` additively: `PayAsync(client, orderId, token, key?)`, `PayOkAsync`, `PaymentStatusAsync(client, orderId)`, `CartRaceInterceptor.RacingPayOrderId` (a competing writer adds a Pending payment and rotates the order stamp, like the other racing fields, and resets in `Disarm`).
- [X] T019 [P] [US1] Write `APIT/PaymentTests.cs` FIRST (use `CartOrderTestHelpers`, `RegisterAndLoginAsync`, `CheckoutOkAsync`): pay success (200, Succeeded, `paymentStatus` Paid, `status` still Pending, `providerReference` set); amount equals the order total and body `amount` is ignored; currency `USD`; decline `tok_decline` and `tok_insufficient_funds` (402, `Failed` row with reason, order Unpaid, then a new key + `tok_visa` succeeds); default approve for another well-formed token; invalid/missing/PAN token and bad key (400 with entries, no payment row, value not in captured logs); order Confirmed/Cancelled -> 409; already paid -> 409; other user -> 404; Admin non-owner -> 404; unknown id -> 404; anonymous -> 401; `tok_error` -> 502 and `Failed` row `provider_error` with no exception text in the body; Admin confirming an unpaid order leaves Unpaid; product price change after checkout does not change the charged amount; no `Payments` column holds the token (reflection over the entity). (US1, FR-003, FR-006, FR-014)
- [X] T020 [P] [US2] Write `APIT/PaymentIdempotencyTests.cs` FIRST: same key twice -> same payment id and reference, one row, gateway charge count 1, no second email; declined replay -> 402 again, one row; new key after decline -> new attempt; no key -> generated, works; key reused for another order -> 409 with no leak; key reused by another user's order -> 409; Pending payment re-drive with the same key completes (seed a Pending row directly). (US2.1-US2.5)
- [X] T021 [P] [US2] Write `APIT/PaymentConcurrencyTests.cs` FIRST with `CartRacingFactory`: two concurrent pays (different keys, started with a barrier) -> exactly one `Succeeded`, charge count 1, loser 409; `RacingPayOrderId` armed so a competing claim lands before ours -> 409 and one charge; finalize race with `RaceStatus = Cancelled` (order cancelled between claim and finalize) -> charge refunded, payment `Refunded`, order `Cancelled`/`Refunded`, response 409; pay racing an Admin confirm -> still Paid. (US2.6, US2.7, FR-019)
- [X] T022 [US1] Create `API/Services/PaymentService.cs`: `PayAsync` per research R5 (claim with Pending payment + stamp rotation, 3-attempt retry on `DbUpdateConcurrencyException`/`DbUpdateException` unique violation, charge outside any transaction, finalize, compensation refund on cancel race), outcome enums per `data-model.md`, Information logs without token/key, receipt call placeholder. (FR-006, FR-007, FR-014, FR-017)
- [X] T023 [US1] Edit `API/Controllers/OrdersController.cs` (re-read it first): inject `PaymentService`; `[HttpPost("{id:int}/pay")] [EnableRateLimiting("payments")] Pay(int id, PayOrderRequestDto)` mapping outcomes to the codes in `contracts/payment-endpoints.md` (`StatusCode(402, ProblemDetails)` with `paymentId`/`reason` extensions, 502, `ConflictProblem`, `ValidationProblem`, `NotFound`).
- [X] T024 [US1] Edit `API/Program.cs`: add the `payments` per-user fixed-window limiter (copy the `profile` policy's partition key logic; `RateLimiting:PaymentsPermitLimit`, default 10, 1 minute); register `PaymentService`. (FR-012)
- [X] T025 [US1] Run the Phase 4 tests; make all pass; run the existing `Order*`/`Cart*` API tests unchanged.

---

## Phase 5: User Story 3 - Cancel refunds (P1)

**Independent Test**: `dotnet test CategoryProduct.API.Tests --filter "FullyQualifiedName~PaymentRefundTests"`

- [X] T026 [P] [US3] Write `APIT/PaymentRefundTests.cs` FIRST: owner cancels a paid order -> 200, Cancelled, `paymentStatus` Refunded, payment `Refunded`, refund count 1 for the full amount, stock restored and `OrderCancelled` movements identical to an unpaid cancel; Admin `POST cancel` and `PUT status Cancelled` same; unpaid cancel never calls the gateway; `tok_refund_fail` -> cancel returns 502, order still Pending/Paid, stock unchanged, payment `Succeeded`; a pending (in-flight) payment seeded directly -> cancel 409; retried cancel after a commit conflict calls refund with the same refund key (use the interceptor `RaceStatus`/stock race to force one conflict on the cancel save, assert one refund reference and no double refund); Confirmed paid order cannot be cancelled (409) and stays Paid; a second cancel of a cancelled order -> 409 with no gateway call. (US3, FR-008)
- [X] T027 [US3] Add to `API/Services/PaymentService.cs`: `RefundForCancelAsync(orderId)` returning `RefundOutcome` (`NotNeeded`, `Refunded` with the tracked payment, `InProgress`, `ProviderError`) using refund key `"refund-" + paymentId`, and `MarkRefunded` applied inside the cancel save. (research R6)
- [X] T028 [US3] Edit `CancelCoreAsync` in `API/Controllers/OrdersController.cs`: before the retry loop's transaction call the refund step once (memoized across attempts); on `InProgress` return conflict, on `ProviderError` return a 502 result (extend `CancelStatus`/`CancelResult` and `ToActionResultAsync` additively); inside the existing save set the payment `Refunded` (attach, rotate its stamp) and `order.PaymentStatus = Refunded`; stock logic, attach order and ordering of saves unchanged. (FR-008)

---

## Phase 6: User Story 4 - Payment status in the API (P2)

**Independent Test**: `dotnet test CategoryProduct.API.Tests --filter "FullyQualifiedName~PaymentReadTests"`

- [X] T029 [P] [US4] Write `APIT/PaymentReadTests.cs` FIRST: `paymentStatus` present (`Unpaid`/`Paid`/`Refunded`) in `GET api/orders`, `GET api/orders/all`, `GET api/orders/{id}`; existing fields unchanged; `GET api/orders/all?paymentStatus=Paid` filters, combined with `status`, unknown value -> 400 with `PaymentStatus` entry, omitted = today's behavior; `GET api/orders/{id}/payments` owner/Admin 200 newest first and paged (`pageSize` clamp), non-owner 404, anonymous 401, no idempotency key or token in the JSON. (US4.1, US4.2, FR-009)
- [X] T030 [US4] Edit `API/Dtos/OrderResponseDto.cs` (append `string PaymentStatus` to both records), `API/Infrastructure/OrderMappingExtensions.cs` (`ToResponse`), `OrdersController.ListAsync` projection, `GetAll` (`paymentStatus` filter using `OrderPaymentStatus.TryParseName`), new `GetPayments(int id, int page, int pageSize)`. (FR-009)
- [X] T031 [US4] Run all existing API tests; fix any constructor call sites found by the compiler without changing expectations.

---

## Phase 7: User Story 5 - Receipt and refund emails (P2)

**Independent Test**: `dotnet test CategoryProduct.API.Tests --filter "FullyQualifiedName~PaymentEmailTests"`

- [X] T032 [P] [US5] Write `APIT/PaymentEmailTests.cs` FIRST (pattern of `OrderEmailTests`: reset recorder, `EmailQueue.WaitForIdleAsync`): receipt to a user with email (subject, body fields, no token/key); none without email, with `Email:Enabled=false` (separate factory), on decline, on idempotent replay; refund mail on cancel of a paid order plus the unchanged 024 cancellation mail; sender throwing still returns 200. (US5, FR-010)
- [X] T033 [US5] Edit `API/Services/NotificationService.cs` (re-read first; 025 changes it): add `PaymentReceiptAsync(Order, Payment)` and `PaymentRefundedAsync(Order, Payment)` per `contracts/payment-emails.md` with the 024 guards and exception swallowing; call them from `PaymentService` after the successful finalize / refund commit (order `User` and `Items` loaded). (FR-010)

---

## Phase 8: User Story 4 (UI) - MVC Pay now and badges (P2)

**Independent Test**: `dotnet test CategoryProduct.MVC.Tests --filter "FullyQualifiedName~PaymentTests"`

- [X] T034 [P] [US4] Write `MVCT/PaymentTests.cs` FIRST (`FakeApi`, `MvcTestBase`, `HtmlHelpers`; add `paymentStatus` as an optional parameter to `ApiJson.Order`/`OrderSummary` defaulting to `Unpaid`): details of a Pending Unpaid order shows the Pay now form (anti-forgery field, select with the six test tokens, hidden `idempotencyKey` GUID that differs between two renders, the "Test payments only" note, no free-text card input); Paid -> badge and no form; Refunded -> badge; Confirmed/Cancelled unpaid -> no form; POST success -> redirect to details with "Payment received." and the API request has the token and key; 402 -> error message with the reason; 409 -> its detail; 404/400/429/502 -> messages; 401 -> sign-out redirect (existing pattern); Index and Manage show the Payment column; scripted JSON without `paymentStatus` still renders (existing `OrdersTests`/`ManageOrdersTests` unchanged and green). (US4.3-US4.7)
- [X] T035 [US4] Edit `MVC/Models/OrderModels.cs` (`string PaymentStatus = "Unpaid"` last parameter on `OrderModel` and `OrderSummaryItem`; `PaymentModel`; `PayOrderRequest`) and `MVC/Services/CategoryProductApiClient.cs` (`PayOrderAsync(id, token, key)` -> `SendAsync<PaymentModel>` POST `api/orders/{id}/pay`; ensure a 402 and 502 surface as an `ApiResult` with a message the controller can show: read how `ApiResult` handles `ProblemDetails` and extend it additively, e.g. a `Detail` property, without changing existing results). (FR-011)
- [X] T036 [US4] Edit `MVC/Controllers/OrdersController.cs`: `[HttpPost("Orders/{id:int}/Pay")] [ValidateAntiForgeryToken] Pay(int id, string? paymentToken, string? idempotencyKey)`; success/error `TempData`, 401 -> `SessionExpiredAsync`, redirect to `Details`; extend `ErrorMessage` for 402/502 without changing existing outputs.
- [X] T037 [US4] Create `MVC/Views/Shared/_PaymentStatusBadge.cshtml`; edit `Views/Orders/Details.cshtml` (badge, Pay now form for `Status == "Pending" && PaymentStatus == "Unpaid"` shown only when the viewer is the owner: compare `Model.UserName` with `User.Identity?.Name` (the API answers 404 to an Admin paying someone else's order, so do not offer the form to Admins viewing other users' orders)), `Index.cshtml` and `Manage.cshtml` (Payment column). Fixed token list in the view; no free-text card field. (FR-011)
- [X] T038 [US4] Run MVC tests; existing ones unchanged.

---

## Phase 9: Review, SQL Server test, final verification and Known Gaps (US6)

- [X] T039 [P] [US2] Write `SQLT/PaymentIndexSqlServerTests.cs` (pattern of `UserEmailIndexSqlServerTests`, full migrate, never migrate to an older migration here): unique violation on a duplicate `IdempotencyKey`; two `Succeeded`/`Pending` payments for one order fail with a unique violation; many `Failed` payments for one order are accepted; a `Failed` plus one `Succeeded` is accepted; `Refunded` plus a new `Pending` is accepted; `Orders.PaymentStatus` defaults to `Unpaid` for an order inserted without it; `ModelHasNoPendingChanges`. Also confirm `SeededTestUserNeutralizationSqlServerTests` (migrates to `PreviousMigration`) and any other earlier-migration test do not read full `Order` or `Payment` entities (use projections/raw SQL if they do). (FR-007, FR-021)
- [X] T040 `git status` review against the "Source changes" list of plan.md: only the listed files; existing migrations untouched; `CategoryProduct.slnx` and the CI workflow unchanged; the new migration only adds `Payments`, the column and indexes; no existing test expectation changed (only additions and helper edits); 025's own files untouched by this work. (FR-016)
- [X] T041 Secret and card hygiene grep over the diff and tracked files: no provider key/secret/webhook secret, no 13-19 digit card-like literal outside the PAN-rejection tests (which use the well-known test number only inside tests), no token or idempotency key in log statements, no `.env`, `logs/`, `App_Data/` files added; `appsettings*.json` hold only `Provider` and `Currency`. (SC-004, Principle IV)
- [X] T042 Run `dotnet build CategoryProduct.slnx` (0 errors, no new warnings in touched files) and `dotnet build CategoryProduct.API.SqlServerTests`.
- [X] T043 Run `dotnet test CategoryProduct.slnx` THREE times in a row; all green each time; record the test counts (more than the T001 baseline).
- [X] T044 Run the SQL Server tests on LocalDB (`dotnet test CategoryProduct.API.SqlServerTests`, at least `PaymentIndexSqlServerTests`, `MigrationTests`, `SeededTestUserNeutralizationSqlServerTests`); all green. If LocalDB is unavailable, record that in Known Gaps.
- [X] T045 Complete `specs/026-payments/spec.md` Known Gaps with the final facts (test counts, whether the SQL Server tests ran on LocalDB or only CI, migration name, deviations from the plan) and tick anything left in `checklists/requirements.md`; set the spec Status line.
- [X] T046 After pushing (when the user pushes), confirm `build-test`, `docker-build`, `sqlserver-tests` (the new index test) and `compose-smoke` are green and record the run in Known Gaps. LEFT OPEN until the user pushes.

> **Final note on feature 025**: 025 (password management) was being implemented in the working tree while this feature was specified. Its changes may touch `CategoryProduct.API/Program.cs`, `CategoryProduct.API.Tests/ApiFactory.cs`, `CategoryProduct.API/Controllers/AccountController.cs`, and also `NotificationService.cs`, `ApplicationDbContext.cs`, `appsettings.json`, `docker-compose.yml`, `.env.example` and the migrations folder (a 025 migration may already exist, so `AddPayments` must be generated AFTER it so its snapshot includes 025's model). The implementer MUST re-read all of these files at implementation time (T002, T010, T013, T018, T023, T024, T033) and never rely on the line numbers or text quoted in these documents; if 025 is still uncommitted, coordinate so the two features do not edit the same file concurrently.

---

## Dependencies and execution order

- T001 first; T002 any time before code.
- Phase 2: tests T003-T005 first; T006-T009 after their tests; T010 after T006-T009 (and registers `PaymentService` after T022); T011 independent.
- Phase 3 needs T006-T008 only for compile of later code; T012 -> T013 -> T014 -> T015 -> T016 -> T017 strictly in order (the migration is generated after the model and DbContext are final and after any 025 migration).
- Phase 4: T018 first (helpers), then tests T019-T021 in parallel, then T022 -> T023 -> T024 -> T025.
- Phase 5 after Phase 4 (same controller and service files): T026 -> T027 -> T028.
- Phase 6 can start after Phase 3 but edits the controller: do it after Phase 5 to avoid conflicts. Phase 7 after Phases 4 and 5.
- Phase 8 (MVC) is independent of Phases 4-7 except that it needs the contract (`contracts/payment-endpoints.md`); it can run in parallel with them on a different agent.
- T039 after T016-T017; T040-T041 after all code; T042-T046 last.

## Parallel example

Agent A: T003-T011 then T012-T017 (primitives, model, migration). Agent B: T034-T038 (MVC, against the contract and `FakeApi`). Then Agent A: T018-T033, T039; then one agent: T040-T046.

## Implementation strategy

MVP = Phase 2 + 3 + 4 (pay once, idempotent, with the Fake gateway) + Phase 6 (see the status). Phase 5 (refund) is required before release because a paid order must be cancellable without stranding money. Phase 7 and the MVC UI add convenience. Keep every existing test green at each checkpoint; they are the regression net for the DTO, controller and cancel changes.
