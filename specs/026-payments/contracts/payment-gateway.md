# Contract: IPaymentGateway and FakePaymentGateway

```csharp
public interface IPaymentGateway
{
    Task<ChargeResult> ChargeAsync(ChargeRequest request, CancellationToken cancellationToken = default);
    Task<RefundResult> RefundAsync(RefundRequest request, CancellationToken cancellationToken = default);
}

public sealed record ChargeRequest(int OrderId, decimal Amount, string Currency, string IdempotencyKey, string PaymentToken);
public sealed record ChargeResult(bool Approved, string? ProviderReference, string? FailureCode);
public sealed record RefundRequest(string ProviderReference, decimal Amount, string Currency, string IdempotencyKey);
public sealed record RefundResult(bool Succeeded, string? RefundReference, string? FailureCode);
```

## Rules for every implementation

- `ChargeAsync` is idempotent per `IdempotencyKey`: the same key returns the same reference and outcome and never charges twice.
- `RefundAsync` is idempotent per `IdempotencyKey` (the service passes `"refund-" + paymentId`).
- A decline is a normal result (`Approved = false`, `FailureCode` such as `card_declined`), not an exception. An exception means a provider/transport error; the service maps it to `provider_error` / 502.
- The token is read only to decide the outcome and must not be logged, stored or returned.
- Amount is positive, 2 decimals; currency is a 3-letter code.

## FakePaymentGateway (default)

Stateless, thread-safe, no I/O, no clock, no randomness, no delay.

| Token | Charge result | Refund result for that charge |
|---|---|---|
| `tok_decline` | declined, `card_declined` | n/a |
| `tok_insufficient_funds` | declined, `insufficient_funds` | n/a |
| `tok_error` | throws `InvalidOperationException("Simulated provider failure.")` | n/a |
| `tok_refund_fail` | approved, reference prefix `fake_chf_` | fails, `refund_failed` |
| any other `tok_[A-Za-z0-9_]{1,60}` (e.g. `tok_visa`, `tok_mastercard`) | approved, reference prefix `fake_ch_` | succeeds |

- Charge reference: prefix + first 24 lowercase hex chars of `SHA256(UTF8(idempotencyKey))`.
- Refund reference: `fake_re_` + first 24 hex chars of `SHA256(UTF8(refundKey))`. A reference starting with `fake_chf_` makes the refund fail with `refund_failed`; a reference not starting with `fake_ch_` or `fake_chf_` fails with `unknown_reference`.
- A token that does not match `tok_[A-Za-z0-9_]{1,60}` reaching the gateway (defense in depth, the service validates first) is declined with `invalid_token` and never echoed.

## Selection

`Payments:Provider` (case-insensitive): `Fake` or empty -> `FakePaymentGateway` (singleton, startup Warning `Fake payment gateway active: no real money moves`); anything else -> startup `InvalidOperationException("Unknown Payments:Provider '<value>'. Supported: Fake.")`.

## Test doubles

`ApiFactory` exposes an optional hook so a test wraps the gateway in a counting decorator (`CountingPaymentGateway`: counts `ChargeAsync`/`RefundAsync` calls, can block or throw) while still using the Fake logic.
