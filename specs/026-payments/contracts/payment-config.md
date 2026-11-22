# Contract: Payment configuration

```json
"Payments": {
  "Provider": "Fake",
  "Currency": "USD"
}
```

| Key | Default | Rules |
|---|---|---|
| `Payments:Provider` | `Fake` | case-insensitive; empty -> `Fake`; unknown -> startup error |
| `Payments:Currency` | `USD` | upper-cased; must match `^[A-Z]{3}$`; empty -> `USD`; invalid -> startup error |
| `RateLimiting:PaymentsPermitLimit` | `10` per minute per user | tests use 1000 |

Environment form (compose / `.env.example`, optional, both may stay empty): `Payments__Provider`, `Payments__Currency`.

No key, secret, webhook secret or credential setting exists in this feature. A future real provider adds its own keys through environment variables only (constitution Principle IV).

## Token and key validation (PaymentTokenRules)

- `paymentToken`: required; trimmed; `^tok_[A-Za-z0-9_]{1,60}$`; a value that is 12..19 digits after removing spaces and `-` gets the message `Card numbers are not accepted. Send a payment token.`; everything else invalid gets `Payment token is invalid.` The value is never echoed.
- `idempotencyKey`: optional; trimmed; empty/null -> generated GUID (`N` format); otherwise `^[A-Za-z0-9_-]{1,64}$`, else `Idempotency key is invalid.`
