# Data Model: Payments

## Schema impact

One migration `AddPayments` (generated, not hand-edited).

### New table `Payments`

| Column | Type | Notes |
|---|---|---|
| `Id` | int identity PK | |
| `OrderId` | int, NOT NULL | FK to `Orders(Id)`, `OnDelete(Restrict)` |
| `Amount` | decimal(18,2), NOT NULL | server-computed from `Order.Total` |
| `Currency` | nvarchar(3), NOT NULL | from `Payments:Currency` (default `USD`) |
| `Status` | nvarchar(20), NOT NULL | `PaymentState`: `Pending`, `Succeeded`, `Failed`, `Refunded` (string conversion, like `Order.Status`) |
| `ProviderReference` | nvarchar(100), NULL | gateway reference; NULL until charged |
| `IdempotencyKey` | nvarchar(64), NOT NULL | unique |
| `FailureReason` | nvarchar(200), NULL | machine-readable code such as `card_declined`; never a token |
| `CreatedAtUtc` | datetime2, NOT NULL | |
| `UpdatedAtUtc` | datetime2, NOT NULL | |
| `ConcurrencyStamp` | uniqueidentifier, NOT NULL | `IsConcurrencyToken`, rotated on every update |

Indexes (declared in `OnModelCreating`):

- `IX_Payments_IdempotencyKey` UNIQUE.
- `IX_Payments_OrderId_Active` UNIQUE on `OrderId` with filter `[Status] IN ('Pending', 'Succeeded')` (at most one active payment per order; any number of `Failed`/`Refunded`).
- `IX_Payments_OrderId_CreatedAtUtc` (non-unique) for the payments list.

No column stores a token, card data or the raw request.

### Changed table `Orders`

| Column | Type | Notes |
|---|---|---|
| `PaymentStatus` | nvarchar(20), NOT NULL, default `'Unpaid'` | `OrderPaymentStatus`: `Unpaid`, `Paid`, `Refunded` (string conversion); `HasDefaultValue(OrderPaymentStatus.Unpaid)` so existing rows are backfilled |

`Status` (`OrderStatus`) is unchanged.

## Non-persistent types

- `IPaymentGateway`: `Task<ChargeResult> ChargeAsync(ChargeRequest, CancellationToken)`, `Task<RefundResult> RefundAsync(RefundRequest, CancellationToken)`.
- `ChargeRequest(int OrderId, decimal Amount, string Currency, string IdempotencyKey, string PaymentToken)`; `ChargeResult(bool Approved, string? ProviderReference, string? FailureCode)`.
- `RefundRequest(string ProviderReference, decimal Amount, string Currency, string IdempotencyKey)`; `RefundResult(bool Succeeded, string? RefundReference, string? FailureCode)`.
- `PaymentOptions { Provider = "Fake", Currency = "USD" }`.
- `PaymentService` outcomes: `PayOutcome` (`Succeeded`, `Declined`, `ProviderError`, `NotFound`, `NotPending`, `AlreadyPaid`, `InProgress`, `KeyUsed`, `NothingToPay`, `Conflict`) and `RefundOutcome` (`NotNeeded`, `Refunded`, `InProgress`, `ProviderError`).

## Changed DTOs (additive)

- `OrderResponseDto` and `OrderSummaryDto`: new last member `string PaymentStatus`.
- New `PayOrderRequestDto(string? PaymentToken, string? IdempotencyKey)`; new `PaymentResponseDto(int Id, int OrderId, decimal Amount, string Currency, string Status, string? ProviderReference, string? FailureReason, DateTime CreatedAtUtc, DateTime UpdatedAtUtc)`.
- MVC: `OrderModel`/`OrderSummaryItem` gain `string PaymentStatus = "Unpaid"`; new `PaymentModel`, `PayOrderRequest(string PaymentToken, string IdempotencyKey)`.

## Production code touched

`Models/Order.cs`, `Models/Payment.cs` (new), `Models/OrderPaymentStatus.cs` (new), `Data/ApplicationDbContext.cs`, `Dtos/OrderResponseDto.cs`, `Dtos/PaymentDtos.cs` (new), `Infrastructure/OrderMappingExtensions.cs`, `Controllers/OrdersController.cs`, `Services/{IPaymentGateway,FakePaymentGateway,PaymentOptions,PaymentTokenRules,PaymentService,NotificationService}.cs`, `Program.cs`, `appsettings.json`, MVC files listed in plan.md.

## Foreign keys

`Payments.OrderId -> Orders.Id` Restrict (orders are never deleted today; payments must not vanish with an order). `Orders.UserId -> Users.Id` is unchanged (Restrict).

## State machine (payment x order)

```text
Order Unpaid  --claim-->  Payment Pending  --approve-->  Payment Succeeded + Order Paid
                                          --decline-->  Payment Failed   (Order Unpaid)
Order Paid (Pending) --cancel--> refund ok --> Payment Refunded + Order Refunded + OrderStatus Cancelled
Order Paid (Pending) --cancel--> refund fails --> nothing changes (502)
Order Paid --Admin confirm--> OrderStatus Confirmed, still Paid (cannot be cancelled afterwards)
```

## Test infrastructure touched

`ApiFactory.cs` (settings `RateLimiting:PaymentsPermitLimit`, optional protected `ConfigureGateway` hook for a decorating gateway that counts calls), `CartOrderTestHelpers.cs` (additive `PayAsync`, `PayOkAsync`, interceptor field `RacingPayOrderId`), `ApiJson.cs` in MVC tests (additive `paymentStatus` parameter with default), `SqlServerEnv` unchanged.
