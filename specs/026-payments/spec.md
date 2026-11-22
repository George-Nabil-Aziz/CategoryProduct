# Feature Specification: Payments (fake gateway, order payment status, refunds)

**Feature Branch**: `026-payments`

**Created**: 2026-11-22

**Status**: Implemented (local verification green; CI verification open)

**Input**: User description: "Add payments for orders with NO real payment provider and NO real keys. An `IPaymentGateway` abstraction with a deterministic `FakePaymentGateway` default (approves by default; a fake token such as `tok_decline` simulates a decline). Never accept or store card numbers. A `Payment` entity/table. A user pays a Pending order of their own with `POST api/orders/{id}/pay`; the amount is computed server-side from the order total. Double-pay is prevented (idempotency key + concurrency). Cancelling a paid order refunds it through the gateway. A receipt email goes through the 024 notification service. Admins see payment status in the orders list and details, users on their order page; the MVC order page has a Pay now button with a fake token selector."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Pay a Pending order (Priority: P1)

A signed-in user pays one of their own Pending orders by sending a fake payment token. The server charges the order total through the configured gateway. On approval the order is marked Paid; on a decline nothing is marked paid and the user may try again.

**Why this priority**: it is the feature. Today `Order` has `Id, UserId, Status (Pending/Confirmed/Cancelled), Total, CreatedAtUtc, UpdatedAtUtc, ConcurrencyStamp, Items` and no payment concept (verified in `CategoryProduct.API/Models/Order.cs`, `OrderStatus.cs`).

**Independent Test**: new API tests `PaymentTests` (`CategoryProduct.API.Tests`) using the in-memory factory; no network, no real provider.

**Acceptance Scenarios**:

1. **Given** a user's own Pending, unpaid order and `POST api/orders/{id}/pay` with `{ "paymentToken": "tok_visa" }`, **Then** 200 with a payment `{ id, orderId, amount, currency, status: "Succeeded", providerReference, failureReason: null, createdAtUtc }`; `GET api/orders/{id}` then has `paymentStatus: "Paid"`; `status` is still `Pending` (see Assumptions A1).
2. **Given** the same call, **Then** the charged `amount` equals the order `Total` computed on the server; any `amount`, `currency` or `total` field in the request body is ignored (the request DTO has none).
3. **Given** `paymentToken: "tok_decline"`, **Then** 402 `ProblemDetails` (`Title: "Payment declined"`, `Detail` with the reason, extensions `paymentId`, `reason`); a `Payment` row with status `Failed` and a `FailureReason` exists; the order stays `Unpaid` and `Pending`; a later call with a new token (and a new idempotency key) can succeed.
4. **Given** `tok_insufficient_funds`, **Then** 402 as above with reason `insufficient_funds`. Any other well-formed `tok_...` token approves (the default).
5. **Given** a missing/empty `paymentToken`, a token that is not of the form `tok_` + 1..60 letters, digits or `_`, or a token that looks like a card number (only digits, spaces or dashes, 12..19 digits), **Then** 400 `ValidationProblem` with a `PaymentToken` entry; nothing is stored or logged from the value; no `Payment` row is created.
6. **Given** an order that is Confirmed or Cancelled, **Then** 409 with detail `Only a Pending order can be paid.`; no charge.
7. **Given** an order that is already Paid, **Then** 409 with detail `The order is already paid.`; no charge.
8. **Given** an order of another user, **Then** 404 (the existing pattern: `GetById` and `Cancel` answer 404 for a non-owner non-Admin); an unknown id is also 404; anonymous is 401. An Admin paying someone else's order is also 404 (only the owner pays, see Assumptions A5).
9. **Given** a gateway that throws or times out (token `tok_error`), **Then** 502 `ProblemDetails` (`Title: "Payment provider error"`), the `Payment` row is `Failed` with reason `provider_error`, the order stays Unpaid; no exception text reaches the client.

**Out-of-band**: the Pay now button on the MVC order page is User Story 4.

---

### User Story 2 - A payment is charged at most once (Priority: P1)

Retrying the same request (network retry, double click) never charges twice, and two different requests racing for the same order produce exactly one charge.

**Why this priority**: money. Existing orders already use optimistic concurrency (`Order.ConcurrencyStamp`, retry loops in `OrdersController`) and a deterministic racing interceptor (`CartRaceInterceptor` in `CategoryProduct.API.Tests/CartOrderTestHelpers.cs`); payments follow the same pattern.

**Independent Test**: `PaymentIdempotencyTests` and `PaymentConcurrencyTests` (API, in-memory + interceptor); `PaymentIndexSqlServerTests` (SQL Server, CI and LocalDB).

**Acceptance Scenarios**:

1. **Given** a request with `idempotencyKey: "k1"` that succeeded, **When** the identical request is repeated, **Then** 200 with the same payment (same `id` and `providerReference`), `Payment` count for the order is 1, and the gateway charge count is 1; no second receipt email.
2. **Given** a declined request with key `k2`, **When** it is repeated with the same key, **Then** the same 402 replay, still one `Payment` row; a new key starts a new attempt.
3. **Given** no `idempotencyKey` in the body, **Then** the server generates one (a GUID) and the response works the same; such a request is not replayable (documented; the MVC always sends a key).
4. **Given** an `idempotencyKey` longer than 64 characters or with characters outside `A-Z a-z 0-9 _ -`, **Then** 400 `ValidationProblem` with an `IdempotencyKey` entry.
5. **Given** a key that was already used for a different order (any user), **Then** 409 `Idempotency key already used.`; the other order's payment is not disclosed.
6. **Given** two concurrent pays for the same order with different keys, **Then** exactly one `Payment` becomes `Succeeded`, the gateway is charged once, and the other request gets 409 (`A payment for this order is already in progress or completed.`).
7. **Given** a pay racing with a cancel of the same order (the order is cancelled between the claim and the finalization), **Then** the charge is refunded through the gateway, the payment ends `Refunded`, the order is Cancelled with `paymentStatus: "Refunded"` and the pay request answers 409.
8. **Given** SQL Server, **Then** `Payments.IdempotencyKey` has a unique index (a second row with the same key fails with a unique violation) and at most one `Pending`/`Succeeded` payment per order can exist (filtered unique index); many `Failed` rows per order are allowed.

---

### User Story 3 - Cancelling a paid order refunds it (Priority: P1)

Cancelling a Pending order that was paid (by its owner or an Admin, via `POST api/orders/{id}/cancel` or `PUT api/orders/{id}/status` with `Cancelled`) refunds the payment through the gateway; stock restore is unchanged.

**Why this priority**: otherwise paying locks the customer's money into a cancelled order.

**Independent Test**: `PaymentRefundTests` (API, in-memory).

**Acceptance Scenarios**:

1. **Given** a Paid, Pending order, **When** the owner cancels it, **Then** 200, `status: "Cancelled"`, `paymentStatus: "Refunded"`, the `Payment` is `Refunded`, the gateway refund was called once for the full amount, stock is restored exactly as for an unpaid cancel (OrderCancelled movements, unchanged).
2. **Given** the same as an Admin (`POST cancel` or `PUT status {"status":"Cancelled"}`), **Then** identical outcome.
3. **Given** an unpaid order, **Then** cancel behaves exactly as today and the gateway is never called (all existing cancel tests unchanged).
4. **Given** a gateway refund failure (token `tok_refund_fail`: the charge succeeds, the refund fails), **Then** the cancel is NOT applied: 502 `ProblemDetails` (`Payment provider error`), order stays Pending and Paid, stock unchanged, the payment stays `Succeeded`; a later cancel can be retried (and succeeds once the provider recovers; with the fake provider the same token keeps failing, so the test asserts the no-change outcome).
5. **Given** a retried cancel after a refund succeeded but the database commit failed, **Then** the refund call is idempotent for the same payment reference and nothing is refunded twice (Fake: deterministic refund reference; real providers: refund keyed by payment reference).
6. **Given** a Confirmed paid order, **Then** it cannot be cancelled (409, as today) and stays Paid; there is no refund API in this feature (Known Gaps).
7. **Given** a cancel while the order has a `Pending` (in-flight) payment, **Then** 409 `A payment is in progress for this order.`; nothing changes.

---

### User Story 4 - Payment status visible; MVC Pay now (Priority: P2)

Admins see each order's payment status in `GET api/orders/all`, the order details and the MVC Manage list; users see it on their own order list and order page. On a Pending, unpaid order the MVC details page shows a "Pay now" form with a fake token selector.

**Why this priority**: makes the feature usable and testable by hand; builds on US1-US3.

**Independent Test**: API read tests in `PaymentReadTests`; MVC tests `PaymentTests` (`CategoryProduct.MVC.Tests`, `FakeApi`, `MvcTestBase`).

**Acceptance Scenarios**:

1. **Given** `GET api/orders`, `GET api/orders/all`, `GET api/orders/{id}`, **Then** every order/summary carries `paymentStatus` (`Unpaid`, `Paid` or `Refunded`); the `status` field and all existing fields keep their shape and meaning. `GET api/orders/all?paymentStatus=Paid` filters (400 `ValidationProblem` for an unknown value); omitted keeps today's behavior.
2. **Given** `GET api/orders/{id}/payments` (owner or Admin; non-owner 404), **Then** 200 with a paged list (`page`, `pageSize` 1..50, `PagedResponseDto`) of the payment attempts of the order, newest first (`id, status, amount, currency, providerReference, failureReason, createdAtUtc, updatedAtUtc`); no idempotency key is returned.
3. **Given** the MVC order details page for a Pending, Unpaid order, **Then** a "Pay now" form (anti-forgery token, `<select name="paymentToken">` with the test tokens `tok_visa`, `tok_mastercard`, `tok_decline`, `tok_insufficient_funds`, `tok_refund_fail`, `tok_error`, a hidden `idempotencyKey` GUID generated per render, a text note "Test payments only. Do not enter card numbers.") posting to `Orders/{id}/Pay`; no free-text card field exists.
4. **Given** the details page of a Paid order, **Then** a "Paid" badge, no Pay now form; of a Refunded order a "Refunded" badge; of a Cancelled/Confirmed unpaid order no form.
5. **Given** the MVC `POST Orders/{id}/Pay` succeeds, **Then** redirect to details with `TempData["Success"]` ("Payment received."); on a 402 `TempData["Error"]` with the decline reason ("Payment declined: ..."); on 409/400/404/429/502 an error message following the existing `ErrorMessage` pattern; 401 signs out (existing `SessionExpiredAsync`).
6. **Given** the Manage and Index tables, **Then** a Payment column with the badge; the existing status badge is unchanged.
7. **Given** existing MVC tests whose scripted JSON has no `paymentStatus`, **Then** they keep passing (the MVC model defaults the missing field to `Unpaid`).

---

### User Story 5 - Receipt email (Priority: P2)

When a payment succeeds and the user has an email address, a plain-text receipt is sent through the 024 `NotificationService`/queue; a refund sends a refund notice. Neither can fail or slow the request.

**Independent Test**: `PaymentEmailTests` (API) using `ApiFactory.Emails` (`RecordingEmailSender`) and `EmailQueue.WaitForIdleAsync`.

**Acceptance Scenarios**:

1. **Given** a user with an email and `Email:Enabled=true`, **When** a payment succeeds, **Then** exactly one email to that address, subject `Payment received for order #<id>`, body with order id, amount with currency, provider reference, date (UTC); no token, no idempotency key.
2. **Given** a user without an email, or `Email:Enabled=false`, **Then** no mail and the payment still succeeds. A declined payment sends nothing. An idempotent replay sends nothing.
3. **Given** a refund (cancel of a paid order), **Then** one email `Payment refunded for order #<id>` to the owner (if any), after the cancellation mail logic of 024 (the existing "order cancelled" mail is still sent unchanged).
4. **Given** the sender throws, **Then** the payment request still returns 200 (the NotificationService already swallows exceptions).

---

### User Story 6 - Safe provider selection and Known Gaps (Priority: P3)

`Payments:Provider` selects the gateway; only `Fake` exists. No real keys anywhere. Final verification and Known Gaps are recorded.

**Acceptance Scenarios**:

1. **Given** no configuration or `Payments:Provider=Fake` (any casing), **Then** `FakePaymentGateway` is used and the API logs one Warning at startup (`Fake payment gateway active: no real money moves`).
2. **Given** any other provider value (e.g. `Stripe`), **Then** the API fails at startup with a clear `InvalidOperationException` (`Unknown Payments:Provider 'Stripe'. Supported: Fake.`) instead of silently using the fake (see research R3).
3. **Given** the repository, **Then** no tracked file contains a provider key, secret, webhook secret or card number; `appsettings*.json` carry only `Provider` and `Currency`.
4. Final build, three full test runs, SQL Server tests on LocalDB, Known Gaps written (see tasks, Phase 9).

---

### Edge Cases

- Payment `Amount` is rounded to 2 decimals and must be > 0; an order with Total <= 0 answers 409 `The order has nothing to pay.` (checkout totals are > 0 today, defensive).
- A product price change after checkout never changes the charge: the amount is `Order.Total`, which stores the checkout-time snapshot (verified: `Checkout` sets `order.Total = order.Items.Sum(LineTotal)` from snapshotted `UnitPrice`).
- An Admin confirming an unpaid Pending order (the existing `PUT status Confirmed`) is allowed and leaves it Unpaid (offline/cash payment); nothing in the existing status machine changes (A1).
- Paying a Pending order that an Admin confirmed concurrently: the claim step conflicts on `ConcurrencyStamp`; retry re-reads and answers 409 `Only a Pending order can be paid.`.
- A crash between "Pending payment saved" and "charge finished" leaves a `Pending` payment: a retry with the same idempotency key re-drives the (idempotent) gateway call and completes; with a different key the user gets 409 (in progress). A stale Pending payment is a Known Gap (no sweeper).
- The `tok_error` provider exception: message is logged at Warning without the token; response is generic.
- Rate limit: `POST api/orders/{id}/pay` has a per-user limiter `payments` (default 10 per minute, `RateLimiting:PaymentsPermitLimit`); 429 `ProblemDetails`.
- Deleted users/orders: orders are never deleted today; `Payments.OrderId` FK is `Restrict`.
- SQL Server migration tests that migrate to an earlier migration must not read full `Order` entities (the new column does not exist there): see FR-021.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: `IPaymentGateway` with `ChargeAsync(ChargeRequest, CancellationToken)` and `RefundAsync(RefundRequest, CancellationToken)`; results carry provider reference, status and failure code; contracts in `contracts/payment-gateway.md`.
- **FR-002**: `FakePaymentGateway` is deterministic and stateless, approves by default, and follows the token conventions in `contracts/payment-config.md`; the same idempotency key yields the same reference and outcome.
- **FR-003**: The API never accepts, stores, logs or returns a card number (PAN), CVV or expiry; it accepts only an opaque `tok_...` token, rejects card-number-looking values, never persists the token, and never logs it (PCI scope: out, see Out of Scope).
- **FR-004**: Provider selection by `Payments:Provider` (default `Fake`); an unknown value fails startup with a clear error (R3).
- **FR-005**: `Payment` entity and `Payments` table per `data-model.md`; `Order.PaymentStatus` (`Unpaid`, `Paid`, `Refunded`) added; the existing `OrderStatus` machine is unchanged (A1).
- **FR-006**: `POST api/orders/{id}/pay` with `{ paymentToken, idempotencyKey? }` as in US1; only the owner; only a Pending, Unpaid order; amount and currency from the server.
- **FR-007**: Idempotency and double-pay prevention as in US2: unique idempotency key, a claim step that atomically creates the `Pending` payment and rotates `Order.ConcurrencyStamp`, a filtered unique index (one `Pending`/`Succeeded` payment per order), bounded retries (3 attempts, like `OrdersController`).
- **FR-008**: Cancelling a Paid order (both `Cancel` and `SetStatus Cancelled`, which share `CancelCoreAsync`) refunds before the order is changed; on refund failure the cancel is not applied (US3); stock restore logic unchanged.
- **FR-009**: Order DTOs (`OrderResponseDto`, `OrderSummaryDto`) gain `paymentStatus`; new payment DTOs; `GET api/orders/{id}/payments`; `GET api/orders/all` gains optional `paymentStatus` filter. Only additions; existing fields, routes, status codes unchanged.
- **FR-010**: Receipt and refund emails via `NotificationService` (US5); never log or send tokens.
- **FR-011**: MVC: `OrderModel`/`OrderSummaryItem` gain `PaymentStatus` (default `Unpaid`), a `PaymentModel`, client methods `PayOrderAsync`, controller action `Orders/{id}/Pay`, Pay now form and badges (US4); MVC references no API type (Principle V).
- **FR-012**: Rate limiter `payments` per authenticated user, configurable, 429 on excess.
- **FR-013**: Currency from `Payments:Currency` (default `USD`, ISO 4217 three letters, validated at startup); stored on each payment; not user-supplied.
- **FR-014**: Logging: Information for charge/refund outcome with order id, payment id, amount, currency, provider reference; never the token or idempotency key.
- **FR-015**: The migration `AddPayments` is generated with `dotnet ef migrations add AddPayments --project CategoryProduct.API` in the same change; nothing hand-edited; existing rows get `PaymentStatus = 'Unpaid'`.
- **FR-016**: Existing orders, cart, inventory and 024 email behavior and tests are unchanged (additions only to DTO JSON).
- **FR-017**: Payment finalization (payment `Succeeded` + order `Paid`) is one `SaveChangesAsync` (transactional on SQL Server like checkout; no explicit transaction on InMemory, using `Database.IsRelational()` as the controller already does).
- **FR-018**: `ConcurrencyStamp` on `Payment` (GUID concurrency token), rotated on every update, like other entities.
- **FR-019**: A pay racing a cancel is resolved by compensation (refund) as in US2.7.
- **FR-020**: `Payments:Provider`/`Payments:Currency` are documented in `.env.example` and `docker-compose.yml` as optional empty/commented values, with no secrets.
- **FR-021**: SQL Server tests: `PaymentIndexSqlServerTests` for FR-007 indexes; any test migrating to an earlier migration (e.g. `SeededTestUserNeutralizationSqlServerTests` uses `IMigrator.MigrateAsync(PreviousMigration)`) must not read full `Order` entities (use projections or raw SQL) so later schema does not break it.
- **FR-022**: Tests deterministic: no fixed sleeps; races use `CartRaceInterceptor`-style interceptors; emails awaited with `EmailQueue.WaitForIdleAsync`.

### Key Entities

- **Payment** (new): `Id`, `OrderId`, `Amount`, `Currency`, `Status` (`Pending`, `Succeeded`, `Failed`, `Refunded`), `ProviderReference`, `IdempotencyKey` (unique), `FailureReason`, `CreatedAtUtc`, `UpdatedAtUtc`, `ConcurrencyStamp`.
- **Order** (changed): new `PaymentStatus` (`Unpaid`, `Paid`, `Refunded`), new navigation `Payments`.
- **IPaymentGateway / FakePaymentGateway / PaymentOptions / PaymentService** (new, non-persistent): see `contracts/`.

## Success Criteria *(mandatory)*

- **SC-001**: A user can pay a Pending order with a fake token and see it Paid, in the API and in the MVC page.
- **SC-002**: Under retries and concurrent pays an order is charged at most once (tests and the SQL Server index prove it).
- **SC-003**: Cancelling a paid order always results in either (cancelled + refunded) or (nothing changed), never cancelled-but-still-paid at the application level.
- **SC-004**: No card number, token, key or secret appears in code, config, logs, database or emails (grep review task).
- **SC-005**: All pre-existing tests pass unchanged; `dotnet test CategoryProduct.slnx` is green three times in a row.
- **SC-006**: `dotnet build CategoryProduct.slnx` and `CategoryProduct.API.SqlServerTests` build with no new warnings in touched files; SQL Server tests pass on LocalDB.

## Out of Scope

- Any real provider (Stripe, PayPal, Paymob, ...), real keys, webhooks, 3-D Secure, SCA.
- PCI DSS: the system never sees card data; the fake token is the entire interface. A real provider later would use client-side tokenization (provider-hosted fields) so the PAN still never reaches this server; PCI SAQ scope is not addressed here.
- Partial refunds, partial payments, multiple payments per order, tips/discounts/tax/shipping, multi-currency pricing or conversion.
- Standalone refund endpoint, refund of a Confirmed order, disputes/chargebacks, payment retries by a sweeper, saved cards, invoices/PDF receipts.
- Making Confirm require payment, or changing `OrderStatus` (A1).
- Payment reporting/exports.

## Assumptions

- **A1 (status machine)**: add `Order.PaymentStatus` instead of changing `OrderStatus` or auto-confirming. Reason: `OrderStatus` is Admin-driven (`Pending` -> `Confirmed`/`Cancelled`) and covered by many existing API/MVC tests and emails; auto-confirm on payment would change `SetStatus` semantics ("Only a Pending order can be confirmed" would then 409 for the admin on paid orders), skip the 024 "confirmed" mail rules, and break the stock/cancel guarantee (Confirmed orders cannot be cancelled, so a paid-then-auto-confirmed order could never be refunded). Payment is orthogonal: Paid/Unpaid/Refunded. Admin still confirms (shipping decision) and can see Paid at a glance.
- **A2 (currency)**: default `USD` (an ISO 4217 code every real provider supports; the app stores prices without currency today so this is a label, not a conversion). Configurable by `Payments:Currency`.
- **A3 (decline conventions)**: tokens only, no card numbers at all: `tok_decline` (reason `card_declined`), `tok_insufficient_funds` (`insufficient_funds`), `tok_error` (provider exception), `tok_refund_fail` (charge approves, refund fails), every other `tok_` + `[A-Za-z0-9_]{1,60}` approves (e.g. `tok_visa`, `tok_mastercard`). The requested "card ending 0002" is documented as a convention for a future card-form provider but is NOT implemented (it would require accepting a PAN).
- **A4 (decline status code)**: 402 Payment Required for declines; 502 for provider errors; 409 for state conflicts; 400 for validation; this extends the existing `ProblemDetails` usage.
- **A5 (who pays)**: only the order owner; Admins can see and cancel/refund but not pay on behalf of users (404 for a non-owner, matching the existing `GetById`/`Cancel` pattern which hides existence).
- **A6 (refund before cancel)**: the refund is called first (idempotent per payment reference) and the cancel is then committed; if the refund fails the cancel is aborted. The opposite order could leave a cancelled order whose money was never returned.
- **A7 (idempotency scope)**: the key is globally unique in the table; ownership is derived from the order. A key reused for a different order answers 409 without revealing anything.
- **A8 (no stored token)**: the token is passed to the gateway and discarded; only provider reference and status are stored.
- **A9 (single payment per order)**: at most one `Pending`/`Succeeded` payment per order; failed attempts are kept as history.
- **A10 (email)**: receipt/refund mails are sent only after the database commit and reuse the 024 queue; no new options.
- **A11 (in-memory tests)**: InMemory does not enforce unique indexes; the controller/service checks plus `ConcurrencyStamp` give the same behavior, and the real indexes are proven on SQL Server.
- **A12 (025 in progress)**: another feature (025 password management) is being implemented at the same time and touches `Program.cs`, `ApiFactory.cs`, `AccountController`, `NotificationService`, `ApplicationDbContext`, `appsettings.json`, `docker-compose.yml`, `.env.example`. This spec was written against the committed HEAD versions; the implementer re-reads those files at implementation time.

## Known Gaps

- **Final facts (local, 2026-11-22)**: baseline before the feature: 365 MVC + 406 API tests. After: 383 MVC + 517 API tests, plus 34 SQL Server tests (10 new in `PaymentIndexSqlServerTests`). `dotnet test CategoryProduct.slnx` was green four times in a row (the first run and three repeats). The SQL Server tests ran on LocalDB (`(localdb)\MSSQLLocalDB`, throwaway GUID databases) and all 34 passed. `docker compose config` was not run (Docker not available in this session); the two new compose entries have non-empty defaults (`Fake`, `USD`).
- **Migration**: `20261122201830_AddPayments` (generated with `dotnet ef migrations add AddPayments`, not edited; `ef database update` never run). It adds `Orders.PaymentStatus` (default `'Unpaid'`), the `Payments` table and its three indexes (the active-payment one filtered with `[Status] IN ('Pending', 'Succeeded')`).
- **Deviations from the plan**: (1) The claim step is two saves, not one: first the order `ConcurrencyStamp` rotation (the lock), then the `Pending` payment insert. On a store without atomic saves (InMemory) a lost one-save claim leaked a half-written payment; the unique indexes still guard the second step on SQL Server. (2) `ApiFactory` always registers a `CountingPaymentGateway` decorator over the fake (exposed as `Factory.Gateway`) instead of an empty hook; the `ConfigureGateway` hook still exists for tests that replace it. (3) Races use deterministic hooks only: a `BeforeCharge` delegate on the counting gateway (park the first pay inside the charge, cancel or confirm the order mid-charge) and the existing `CartRaceInterceptor` extended with `RacingPayOrderId`; no real-thread races. (4) The `payments` rate limiter is partitioned by user id (falling back to the IP), not by IP like `profile`. (5) The MVC API client does not see a 502: the existing `ApiErrorHandler` turns every 5xx into `ApiUnavailableException`, so the MVC `Pay` action catches it and flashes "payment service is unavailable" (no change to existing handlers). (6) `paymentStatus` filter error key comes back as `paymentStatus` (mapped to the action parameter name, like `status` today). (7) `ApiJson.Order` is unchanged; a new `ApiJson.OrderWithPayment` and an optional `paymentStatus` on `OrderSummary` were added so scripted JSON without the field keeps being tested. (8) `SeededTestUserNeutralizationSqlServerTests` now inserts its old-schema order with raw SQL (the `Order` entity maps `PaymentStatus`, which the old schema lacks; a property with a database default is read back with OUTPUT on insert). (9) Tests capture log output through a typed-`ILogger<>` replacement, not Serilog sinks, which were unreliable with parallel hosts.

- A stale `Pending` payment (process crash mid-charge) is not swept automatically.
- No standalone refund or partial refund; refunding a Confirmed order is impossible because Confirmed orders cannot be cancelled.
- Only the Fake provider exists; a real provider needs its own task (client-side tokenization, webhooks, secrets in environment only).
- A refund failure blocks cancellation (safe) but needs the provider to recover; there is no manual override.
- CI verification: open.
