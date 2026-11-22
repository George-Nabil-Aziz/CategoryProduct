# Contract: Payment endpoints and DTO changes

All routes under `api/orders` (`[Authorize]`).

## POST api/orders/{id}/pay

Rate limiter `payments`. Body `{ "paymentToken": "tok_visa", "idempotencyKey": "optional" }` (no amount/currency fields; unknown JSON members are ignored).

| Case | Status | Body |
|---|---|---|
| Approved (or replay of an approved key) | 200 | `PaymentResponseDto` (`status: "Succeeded"`) |
| Declined (or replay of a declined key) | 402 | `ProblemDetails` `Title: "Payment declined"`, `Detail: "Payment declined: <reason>."`, extensions `paymentId`, `reason` |
| Provider error | 502 | `ProblemDetails` `Title: "Payment provider error"`, generic detail |
| Invalid token / key | 400 | `ValidationProblem` with `PaymentToken` / `IdempotencyKey` |
| Not owner, unknown id (including Admin non-owner) | 404 | empty |
| Anonymous | 401 | |
| Not Pending | 409 | `Only a Pending order can be paid.` |
| Already Paid | 409 | `The order is already paid.` |
| Payment in progress / another active payment | 409 | `A payment for this order is already in progress or completed.` |
| Key used for another order | 409 | `Idempotency key already used.` |
| Total <= 0 | 409 | `The order has nothing to pay.` |
| Pay lost a race with a cancel (charge refunded) | 409 | `The order was cancelled; the payment was refunded.` |
| Rate limit | 429 | `ProblemDetails` (existing handler) |

`PaymentResponseDto`: `{ id, orderId, amount, currency, status, providerReference, failureReason, createdAtUtc, updatedAtUtc }`. `status` is one of `Pending`, `Succeeded`, `Failed`, `Refunded`. The idempotency key and token are never returned.

## GET api/orders/{id}/payments?page=1&pageSize=10

Owner or Admin; others 404; 401 anonymous. `PagedResponseDto<PaymentResponseDto>` newest first (`CreatedAtUtc` desc, then `Id` desc); `pageSize` clamped 1..50.

## Order DTOs (additive)

`OrderResponseDto` and `OrderSummaryDto` gain `paymentStatus` (`Unpaid` | `Paid` | `Refunded`). `GET api/orders/all` gains optional `paymentStatus`; unknown value -> 400 `ValidationProblem` (`PaymentStatus must be Unpaid, Paid or Refunded.`); combined with `status` it is an AND.

## Cancel changes (behavior, same routes)

`POST api/orders/{id}/cancel` and `PUT api/orders/{id}/status {"status":"Cancelled"}`:

| Case | Result |
|---|---|
| Unpaid order | unchanged |
| Paid Pending order, refund ok | 200, `status: "Cancelled"`, `paymentStatus: "Refunded"` |
| Paid Pending order, refund fails | 502 `Payment provider error`; nothing changed |
| Pending payment in flight | 409 `A payment is in progress for this order.` |

## MVC routes

`POST /Orders/{id}/Pay` (`[Authorize]`, `[ValidateAntiForgeryToken]`), form fields `paymentToken`, `idempotencyKey`; always redirects to `Orders/Details/{id}` with `TempData["Success"]` ("Payment received.") or `TempData["Error"]`.
