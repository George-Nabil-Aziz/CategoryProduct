# Contract: Payment emails

Sent through `NotificationService` -> `EmailQueue` (feature 024); same guards: `Email:Enabled`, recipient `order.User?.Email` present, all exceptions swallowed with a Warning, plain text, invariant culture, UTC dates (`yyyy-MM-dd HH:mm 'UTC'`). Neither mail contains a token, idempotency key or card data.

## Receipt (after a payment becomes Succeeded; not on replay, not on decline)

Subject: `Payment received for order #<id>`

```text
Hello <userName>,

We received your payment for order #<id> on <date>.
Amount: <amount F2> <currency>
Reference: <providerReference>

Items:
- <name> x<qty> @ <unit> = <line>

Total: <total F2>
```

## Refund (after a paid order is cancelled and the refund succeeded)

Subject: `Payment refunded for order #<id>`

```text
Hello <userName>,

Your payment of <amount F2> <currency> for order #<id> was refunded on <date>.
Reference: <providerReference>
```

The 024 "Order #<id> cancelled" mail is still sent unchanged and independently (two mails on a paid cancel).
