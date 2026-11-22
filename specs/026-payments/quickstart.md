# Quickstart: Payments

## Run everything (no Docker, SQL Server, network or provider)

```powershell
dotnet build CategoryProduct.slnx
dotnet test CategoryProduct.slnx
```

## Targeted runs

```powershell
dotnet test CategoryProduct.API.Tests --filter "FullyQualifiedName~Payment"
dotnet test CategoryProduct.MVC.Tests --filter "FullyQualifiedName~PaymentTests"
```

## SQL Server tests (LocalDB)

```powershell
dotnet build CategoryProduct.API.SqlServerTests
dotnet test CategoryProduct.API.SqlServerTests --filter "FullyQualifiedName~PaymentIndexSqlServerTests"
```

Use the same LocalDB connection setup as the other SQL Server tests (see `SqlServerEnv`).

## Try it by hand (Fake provider, the default)

1. Start API and MVC, register, add a product to the cart, check out (order is Pending, Unpaid).
2. Open the order, choose `tok_visa`, click Pay now: "Payment received.", badge Paid, status still Pending.
3. Try another order with `tok_decline`: "Payment declined: card_declined."; pay again with `tok_visa`.
4. Cancel the paid order: badge Refunded, status Cancelled, stock restored. With `Email:Enabled=true` and a user email, see the payment received and payment refunded files in the pickup folder (`App_Data/email-outbox`).
5. API: `POST api/orders/{id}/pay` with `{ "paymentToken": "tok_visa", "idempotencyKey": "demo-1" }`, repeat it: same payment id.

## Configuration

`Payments:Provider` = `Fake` (only value); `Payments:Currency` = `USD`. A wrong provider name stops the API at startup on purpose.

## Manual checks

- Send `paymentToken: "4242424242424242"`: 400, no payment row, value not in logs.
- Two browser tabs paying the same order: one wins, the other gets a conflict message.
