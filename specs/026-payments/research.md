# Research: Payments

## R1. What exists today (verified at HEAD)

- `Order`: `Id, UserId, User, Status (OrderStatus), Total, CreatedAtUtc, UpdatedAtUtc, ConcurrencyStamp (Guid, IsConcurrencyToken), Items`. `OrderStatus`: `Pending, Confirmed, Cancelled`, stored as string (max 20) via `HasConversion<string>()`.
- `OrdersController` (`api/orders`, `[Authorize]`): `Checkout` (Pending order, stock decremented, snapshot of `UnitPrice`/`LineTotal`, `Total = Sum(LineTotal)`), `GetMine`, `GetAll` (Admin, `status` filter), `GetById` (404 for non-owner non-Admin), `Cancel` and `SetStatus(Cancelled)` both via `CancelCoreAsync` (only Pending can be cancelled; restores stock with `InventoryService.StageMovementAsync(+qty, OrderCancelled)`; order read untracked then attached; `ConcurrencyStamp` rotated; 3-attempt retry on `DbUpdateConcurrencyException`; `Database.IsRelational()` decides whether a transaction is used), `SetStatus(Confirmed)` (Pending -> Confirmed only, Admin).
- 409s are `ProblemDetails` via `ConflictProblem`; validation uses `ValidationProblem(ModelState)`.
- `NotificationService` (024): scoped, composes plain-text mails, `EmailQueue.TryEnqueue`, every public method swallows exceptions, requires `Email:Enabled` and `order.User?.Email`. Order DTOs: `OrderResponseDto(Id, UserId, UserName, Status, Total, CreatedAtUtc, UpdatedAtUtc, Items)`, `OrderSummaryDto(... ItemCount ...)`; constructors only in `OrderMappingExtensions.ToResponse` and the `ListAsync` projection.
- Tests: `ApiFactory` (InMemory, seeds admin `tester`, `plainuser`, products, `Emails` recorder, `EmailQueue`), `CartRaceInterceptor` (fires once in `SavingChangesAsync` when an Order is Added/Modified or a StockMovement Added; fields `RaceStatus`, `RacingCancelOrderId`, ...) and `CartRacingFactory`; `CartOrderTestHelpers` (`RegisterAndLoginAsync`, `CheckoutOkAsync`, `CancelAsync`, `GetOrderAsync`, ...). MVC tests: `FakeApi` scripted JSON (`ApiJson.Order(id, status, userId, userName, lines)` has no payment field), `MvcTestBase`, `HtmlHelpers`. SQL Server tests: `SqlServerEnv`, `UserEmailIndexSqlServerTests` pattern (`WithDatabaseAsync`, unique violation numbers 2601/2627), `SeededTestUserNeutralizationSqlServerTests` migrates to `PreviousMigration` through `IMigrator`.
- MVC: `OrdersController` (`Details`, `Cancel`, `Manage`, `SetStatus`, `ErrorMessage` helper, `SessionExpiredAsync`), `CategoryProductApiClient` (`SendAsync<T>`, `ApiResult` with `Conflict`, `Errors`, `NotFound`, `Forbidden`, `TooManyRequests`), `OrderModel`/`OrderSummaryItem` positional records, `_OrderStatusBadge` partial.

## R2. Status machine: PaymentStatus vs auto-confirm

Options: (a) auto-confirm on payment; (b) `Order.PaymentStatus`. Chosen (b).
Reasons: `Confirmed` orders cannot be cancelled, so auto-confirm would make a paid order impossible to cancel/refund; Admin confirm is a fulfilment decision and `SetStatus` rejects confirming a non-Pending order (a paid-and-auto-confirmed order would make the Admin's button fail); the 024 "confirmed" mail would fire from the wrong place; many existing tests assert Pending after checkout and Confirmed after Admin action. (b) is purely additive (a new column with default `Unpaid`, a new JSON field).

## R3. Provider selection: unknown value

Options: fall back to Fake with a warning, or fail startup. Chosen: fail startup with `InvalidOperationException("Unknown Payments:Provider 'X'. Supported: Fake.")`. A silent fallback would let a production deployment that believes it configured a real provider "approve" orders with no money moving. Missing/empty value -> `Fake` (the documented default) and a Warning log. Casing is ignored. `ApiFactory` sets nothing (default Fake).

## R4. Token and card-number handling

- The API accepts `paymentToken` only; accepted shape `^tok_[A-Za-z0-9_]{1,60}$`. Anything else is 400.
- A value that after removing spaces and dashes is 12..19 digits (PAN-like), including Luhn-valid or not, gets the specific message `Card numbers are not accepted. Send a payment token.`; the value is never echoed, logged or stored (validation messages are static).
- The token is not persisted (A8); only the gateway sees it. The unit tests assert the `Payments` table has no token column and that logs from a pay do not contain the token (a capturing logger in `PaymentTests`).
- "Card ending 0002" is not implemented (would require a PAN). Documented as a future convention for a card-form provider.
- PCI DSS: out of scope; with a real provider, hosted fields/client tokenization keep the PAN off this server.

## R5. Pay algorithm and race analysis

Steps inside `PaymentService.PayAsync` (each retry iteration starts with `ChangeTracker.Clear()`; max 3 iterations):

1. Load the order tracked with `User`. Missing or `UserId != callerId` -> `NotFound`.
2. Replay: find `Payment` by `IdempotencyKey`. If found and `OrderId` differs -> `KeyUsed` (409). If found for this order: `Succeeded`/`Refunded` -> replay success; `Failed` -> replay decline (402); `Pending` -> go to step 5 with the existing payment (re-drive).
3. State checks: `Status != Pending` -> 409 `Only a Pending order can be paid.`; `PaymentStatus == Paid` -> 409 already paid; `Total <= 0` -> 409.
4. Another active payment (`Pending`/`Succeeded` with a different key) -> 409 in progress or completed. Else claim: add `Payment{Status=Pending, Amount=round(Total,2), Currency, IdempotencyKey, ...}` and set `order.ConcurrencyStamp = Guid.NewGuid()` in ONE `SaveChangesAsync`. A concurrent claim fails with `DbUpdateConcurrencyException` (stamp mismatch; deterministic on InMemory) or a unique-index `DbUpdateException` (SQL Server, filtered unique index on active payments or on the key); both -> next iteration, which now sees the other payment in step 2/4 and answers 409 or replay.
5. Charge: `gateway.ChargeAsync(new ChargeRequest(orderId, amount, currency, key, token))` OUTSIDE any transaction. Exception -> payment `Failed` (`provider_error`) saved, result `ProviderError` (502). Declined -> payment `Failed` with the reason, 402.
6. Finalize on approval: payment `Succeeded` + `ProviderReference`, order `PaymentStatus = Paid`, `UpdatedAtUtc`, stamp rotated, one `SaveChangesAsync` (a relational transaction is not needed because it is a single save; the controller convention of `IsRelational()` is kept only where several saves happen). On `DbUpdateConcurrencyException` (someone changed the order after the claim: cancel, confirm): reload; if the order is `Cancelled` -> `gateway.RefundAsync` for the reference, payment `Refunded`, result `Conflict` (US2.7); if still Pending and Unpaid -> retry finalize; otherwise (e.g. Confirmed by an Admin concurrently) -> finalize on the reloaded order is still valid (Paid on a Confirmed order is allowed), so retry finalize.
7. Email: after a successful finalize (not on replay), `PaymentReceiptAsync`.

Race table: pay||pay (different keys): one claim wins, the other 409. pay||pay (same key): second sees the Pending/Succeeded payment by key in step 2 -> re-drive of the idempotent gateway then finalize is a no-op (`Succeeded` already) -> replay. pay||cancel before claim: claim conflicts on stamp -> retry re-reads Cancelled -> 409. pay||cancel after claim: cancel sees a `Pending` payment -> 409 (R6); cancel that read before the claim conflicts on stamp (cancel retries, then sees Pending -> 409).

## R6. Refund-before-cancel

`CancelCoreAsync` currently loops: read order -> checks -> stage stock -> attach/update -> save. New steps (before the loop's transaction, executed once per call, memoized across retries): find the order's payments: `Pending` -> 409 in progress; `Succeeded` -> `gateway.RefundAsync(reference, amount, refundKey)` where `refundKey = "refund-" + payment.Id` (idempotent); throws or returns Failed -> abort with 502, no DB change; success -> inside the existing save also set payment `Refunded` (attach, rotate stamp) and `order.PaymentStatus = Refunded`. If the loop ends in conflict after a successful refund, the payment stays `Succeeded` in the database while refunded at the provider; the next cancel retry refunds again with the same refund key and the Fake returns the same refund reference, so no double refund. Alternative (cancel then refund) rejected: a provider failure after the commit would leave a cancelled order with the money unreturned and no automatic retry.

## R7. Idempotency key semantics

Optional in the body (a GUID is generated if missing). Format `^[A-Za-z0-9_-]{1,64}$`. Unique index on the whole table. The same key on the same order replays the stored outcome (even `Failed`), which matches how provider idempotency keys work; a new attempt after a decline needs a new key (the MVC renders a fresh GUID per page load, so a refresh after a decline gets a new key while a double submit of the same rendered form reuses it).

## R8. Fake gateway determinism

Stateless and thread-safe. `ChargeAsync`: token rules (R4/contracts) decide the outcome; the reference is `"fake_ch_" + hex(SHA256(idempotencyKey))[..24]`. `RefundAsync`: fails when the stored reference was produced for `tok_refund_fail`, but the gateway is stateless, so the failure marker must be derivable from the reference: for `tok_refund_fail` the charge reference uses the prefix `fake_chf_` instead of `fake_ch_`, and `RefundAsync` fails when the reference starts with `fake_chf_`. Refund reference `"fake_re_" + hex(SHA256(refundKey))[..24]`. No clock, no random, no delay.

## R9. Rate limiting

Add a per-user fixed-window limiter `payments` (default 10/min, `RateLimiting:PaymentsPermitLimit`; tests set 1000 in `ApiFactory`), keyed by user id like the existing `profile` policy (the implementer copies that policy's partition key code). Applied to `Pay` only. MVC already maps 429 to a message through `ApiResult.TooManyRequests`.

## R10. Order DTO growth and existing tests

Positional records: append `string PaymentStatus` as the last parameter. Existing API tests use JSON access by property name, not constructors (verified by grep at T002); MVC tests use `ApiJson` anonymous objects without the field, so the MVC records take `string PaymentStatus = "Unpaid"` (optional parameter on a positional record is allowed and System.Text.Json deserializes a missing field to the default for constructor parameters with defaults).

## R11. Migration default and SQL Server tests

`Orders.PaymentStatus` is `nvarchar(20) NOT NULL`; the migration must backfill existing rows with `'Unpaid'`: configure `HasDefaultValue(OrderPaymentStatus.Unpaid)` in the model so the generated migration carries `defaultValue: "Unpaid"`. Review the generated migration (T016/T040). `SeededTestUserNeutralizationSqlServerTests` migrates to an old migration; it and any future such test must not read `Order`/`Payment` entities (full entity reads would select the missing column). The new `PaymentIndexSqlServerTests` migrates fully.

## R12. Configuration binding

`Payments:Currency` empty string binds to the default (same lesson as 024 R4): the options class normalizes empty/whitespace to `USD` and validates `^[A-Z]{3}$` after upper-casing; invalid -> startup `InvalidOperationException`. `Provider` empty -> `Fake`.

## R13. Compose and Docker

`docker-compose.yml` and `.env.example` get optional `Payments__Provider` / `Payments__Currency` entries (empty or commented, matching the 024 style). The Dockerfile is unchanged. The compose smoke test is unaffected (no payment call).
