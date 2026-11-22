# Implementation Plan: Payments

**Branch**: `026-payments` | **Date**: 2026-11-22 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/026-payments/spec.md`

## Summary

(1) A `Payment` entity/table and `Order.PaymentStatus` (`Unpaid`/`Paid`/`Refunded`) with one `AddPayments` migration; the existing `OrderStatus` machine is untouched. (2) `IPaymentGateway` with a deterministic `FakePaymentGateway` selected by `Payments:Provider` (only `Fake`; unknown fails startup), token-only (no card numbers). (3) `POST api/orders/{id}/pay` implemented by a scoped `PaymentService` that claims the order with a `Pending` payment plus a `ConcurrencyStamp` rotation, charges through the gateway, then finalizes `Succeeded` + `Paid` in one save; idempotency by a unique key; double pay prevented by the claim and a filtered unique index. (4) Cancelling a Paid order (both cancel paths share `CancelCoreAsync`) refunds first, then cancels; a refund failure aborts the cancel. (5) Receipt/refund emails through 024's `NotificationService`. (6) `paymentStatus` in order DTOs, `GET api/orders/{id}/payments`, an Admin filter, MVC Pay now form with a fake token selector and badges.

## Technical Context

**Language/Version**: C# / .NET 10.

**Primary Dependencies**: none added (`System.Security.Cryptography` for the deterministic fake reference; EF Core and Options already referenced).

**Storage**: SQL Server via EF Core: new `Payments` table, new `Orders.PaymentStatus` column, two indexes (migration). InMemory in tests (indexes not enforced; service checks and `ConcurrencyStamp` give equal behavior).

**Testing**: xunit. `CategoryProduct.API.Tests` (`ApiFactory`, `CartRaceInterceptor`/`CartRacingFactory`, `RecordingEmailSender`, `EmailQueue.WaitForIdleAsync`), `CategoryProduct.MVC.Tests` (`FakeApi`, `MvcTestBase`, `HtmlHelpers`, `ApiJson`), `CategoryProduct.API.SqlServerTests` (not in `CategoryProduct.slnx`; CI and LocalDB).

**Target Platform**: local `dotnet test CategoryProduct.slnx`; GitHub Actions jobs unchanged.

**Project Type**: existing web API + MVC; no new project.

**Constraints**: constitution principles I-V; no real keys; never handle card numbers; existing tests keep their expectations; API contract only grows (new optional fields, new endpoints); the charge happens outside any database transaction (no external call inside a transaction).

**Scale/Scope**: 2 models/enums + `Payment`, 1 generated migration, ~6 new API files (`IPaymentGateway`, `FakePaymentGateway`, `PaymentOptions`, `PaymentService`, `PaymentDtos`, token rules), `OrdersController` and `NotificationService` edited, `ApplicationDbContext`, `Program.cs` registration + limiter, 2 appsettings, compose/.env.example, MVC (client, models, controller action, details/index/manage views, badge partial), ~9 new test files.

## Constitution Check

Checked against `.specify/memory/constitution.md` v1.0.0.

| Principle | Check | Status |
|---|---|---|
| I. Consistent REST API Design | `POST api/orders/{id}/pay` mirrors the existing action sub-routes `POST api/orders/{id}/cancel` and `PUT api/orders/{id}/status` under the plural `orders` resource; `GET api/orders/{id}/payments` is a plural sub-resource list and is paginated-by-nature bounded (attempts per order are few; capped at 50, newest first, with `page`/`pageSize` like other lists to honor the pagination rule). Standard codes: 200, 400 `ValidationProblem`, 401, 404, 409/402/502/429 `ProblemDetails`. Existing routes, verbs, codes unchanged; only optional response fields and one optional filter are added. | PASS |
| II. Schema Changes Only Through EF Core Migrations | `Payment`, `Order.PaymentStatus` and the indexes ship with `dotnet ef migrations add AddPayments --project CategoryProduct.API` in the same change (task T016); index filters are declared in `OnModelCreating` (`HasFilter`), not hand-edited in the migration; the MVC keeps its own models. | PASS |
| III. Simplicity & YAGNI | Controllers keep using `ApplicationDbContext`. New abstractions have concrete consumers: `IPaymentGateway` has the Fake implementation plus test gateways (a recording/throwing decorator in tests) and is the seam a real provider needs by definition of the request; `PaymentService` is justified by two callers (pay and cancel/refund) and by orchestration that does not belong in the controller; no repository, no outbox, no webhook, no sweeper, no partial refunds. See Complexity Tracking. | PASS |
| IV. Secrets & Credential Hygiene (NON-NEGOTIABLE) | No provider keys exist; config holds only `Provider` and `Currency`; tokens and idempotency keys are never logged, stored (token) or emailed; PAN-looking input is rejected before any use; grep task T041. | PASS |
| V. API/MVC Separation | MVC calls `api/orders/{id}/pay` and reads `paymentStatus` over HTTP with its own `PaymentModel`/`OrderModel` additions; no API type referenced; MVC tests reference only `CategoryProduct.MVC`. | PASS |

Workflow gates: "Every change that adds or modifies a Model class MUST include the migration": T016 (generation) and T040 (review). "No secret values in tracked configuration": T041. "Controller behavior verified": automated tests per story.

## Project Structure

### Documentation

```text
specs/026-payments/
  spec.md  plan.md  research.md  data-model.md  quickstart.md  tasks.md
  contracts/{payment-gateway.md, payment-config.md, payment-endpoints.md, payment-emails.md}
  checklists/requirements.md
```

### Source changes

```text
CategoryProduct.API/Models/Payment.cs                          NEW entity + PaymentState enum
CategoryProduct.API/Models/OrderPaymentStatus.cs               NEW enum (Unpaid, Paid, Refunded) + TryParseName
CategoryProduct.API/Models/Order.cs                            EDIT PaymentStatus, Payments navigation
CategoryProduct.API/Data/ApplicationDbContext.cs               EDIT DbSet<Payment>, Payment config, Order.PaymentStatus config (025 also edits this file)
CategoryProduct.API/Migrations/<ts>_AddPayments(.Designer).cs  NEW generated; snapshot regenerated by the tool
CategoryProduct.API/Services/IPaymentGateway.cs                NEW interface + request/result records
CategoryProduct.API/Services/FakePaymentGateway.cs             NEW deterministic fake
CategoryProduct.API/Services/PaymentOptions.cs                 NEW Provider, Currency (+ validation helper)
CategoryProduct.API/Services/PaymentTokenRules.cs              NEW token/idempotency key validation
CategoryProduct.API/Services/PaymentService.cs                 NEW pay + refund orchestration
CategoryProduct.API/Services/NotificationService.cs            EDIT PaymentReceiptAsync, PaymentRefundedAsync (025 edits this file too)
CategoryProduct.API/Dtos/PaymentDtos.cs                        NEW PayOrderRequestDto, PaymentResponseDto
CategoryProduct.API/Dtos/OrderResponseDto.cs                   EDIT add paymentStatus to both records (positional: append at the end)
CategoryProduct.API/Infrastructure/OrderMappingExtensions.cs   EDIT map paymentStatus
CategoryProduct.API/Controllers/OrdersController.cs            EDIT Pay, GetPayments, paymentStatus filter, CancelCoreAsync refund
CategoryProduct.API/Program.cs                                 EDIT options, gateway selection, PaymentService, limiter "payments" (025 edits this file too)
CategoryProduct.API/appsettings.json                           EDIT "Payments": { "Provider": "Fake", "Currency": "USD" }
docker-compose.yml, .env.example                               EDIT optional Payments__Provider / Payments__Currency
CategoryProduct.MVC/Models/OrderModels.cs                      EDIT PaymentStatus (default Unpaid), PaymentModel
CategoryProduct.MVC/Services/CategoryProductApiClient.cs       EDIT PayOrderAsync
CategoryProduct.MVC/Controllers/OrdersController.cs            EDIT Pay action
CategoryProduct.MVC/Views/Orders/{Details,Index,Manage}.cshtml EDIT payment badge, Pay now form
CategoryProduct.MVC/Views/Shared/_PaymentStatusBadge.cshtml    NEW
CategoryProduct.API.Tests/ApiFactory.cs                        EDIT Payments test settings, optional gateway override hook (025 edits this file too)
CategoryProduct.API.Tests/CartOrderTestHelpers.cs              EDIT additive: PayAsync helper, interceptor RacingPayOrderId
CategoryProduct.API.Tests/{PaymentTokenRulesTests,FakePaymentGatewayTests,PaymentOptionsTests,PaymentTests,PaymentIdempotencyTests,PaymentConcurrencyTests,PaymentRefundTests,PaymentReadTests,PaymentEmailTests}.cs   NEW
CategoryProduct.MVC.Tests/PaymentTests.cs                      NEW (+ ApiJson additions)
CategoryProduct.API.SqlServerTests/PaymentIndexSqlServerTests.cs NEW
```

## Design

- **Why PaymentStatus and not a status change**: see spec A1. `OrderStatus` and its tests, mails and Admin UI stay as they are.
- **Pay flow** (`PaymentService.PayAsync(orderId, callerId, token, key)`), detailed in `research.md` R5: load -> validate (404/409) -> replay by key -> claim (insert `Pending` payment + rotate `Order.ConcurrencyStamp` in one save) -> gateway charge (outside any transaction) -> finalize (`Succeeded` + `Paid` in one save) or record `Failed`. Each DB step is a bounded (3) retry on `DbUpdateConcurrencyException`/unique violation, like `OrdersController`.
- **Refund-first cancel**: `CancelCoreAsync` asks `PaymentService.RefundForCancelAsync(order)` once before its transaction loop; `Ok` -> the same save that cancels the order also marks the payment `Refunded` and the order `Refunded`; `ProviderError` -> `CancelResult` conflict-like 502; `InProgress` -> 409.
- **Gateway lifetime**: singleton (stateless), registered by a small factory in `Program.cs` that reads `Payments:Provider` and throws on unknown.
- **DTO growth**: both order records get `string PaymentStatus` appended as the last positional parameter; `OrderMappingExtensions` and the one summary projection in `ListAsync` are the only constructors (verified by grep at T002).
- **Emails**: `NotificationService.PaymentReceiptAsync(order, payment)` and `PaymentRefundedAsync(order, payment)`, same guards as 024 (`Email:Enabled`, user email present, swallow exceptions).
- **MVC**: the Pay now form posts `paymentToken` (select) and `idempotencyKey` (hidden GUID per render) to `Orders/{id}/Pay`; the controller forwards them; results map to `TempData` messages.

## Complexity Tracking

| Item | Why needed | Simpler alternative rejected because |
|---|---|---|
| `IPaymentGateway` | the requested provider seam; Fake now, real later | calling a static fake would force edits in the pay/cancel flow when a provider arrives |
| `PaymentService` | two callers (pay, cancel-refund) and a multi-step state machine with compensation | putting it in `OrdersController` would triple that controller and duplicate refund logic |
| Claim step with `Pending` payment | atomic double-pay prevention independent of provider behavior | relying on provider idempotency alone cannot stop two different keys |
| Filtered unique index (one active payment per order) | database-level guarantee on SQL Server | service check alone races across processes |
| Refund-before-cancel | no cancelled-but-unrefunded state | cancel-then-refund can strand money on provider failure |

## Phase order

Setup -> design primitives (options, tokens, gateway) -> model + migration -> pay flow (US1+US2) -> refund (US3) -> reads/DTOs (US4 API) -> emails (US5) -> MVC (US4 UI) -> SQL Server test -> review/secret hygiene -> final verification and Known Gaps (US6).
