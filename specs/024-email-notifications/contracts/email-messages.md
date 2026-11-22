# Contract: notification messages

All bodies are UTF-8 plain text, built with `StringBuilder` and `string.Create`/interpolation using `CultureInfo.InvariantCulture`. Money is `F2`. Dates are UTC `yyyy-MM-dd HH:mm 'UTC'`. Line separator `\n` (the pickup writer converts to CRLF). No HTML, no links, no secrets. One message per recipient; `To` is a single address.

## NotificationService

```csharp
public Task OrderPlacedAsync(Order order);                          // order.User and order.Items loaded
public Task OrderStatusChangedAsync(Order order);                   // Status is Confirmed or Cancelled
public Task LowStockAsync(IEnumerable<StockMovement> movements);    // movement.Product loaded
```

Every method: try { ... } catch (Exception ex) { log Warning (no addresses, no bodies) }. Returns a completed task even on failure. Does nothing when `Email:Enabled` is false (the queue refuses; the Admin query is skipped as well by checking the option first).

## Order placed

Recipient: `order.User?.Email`; skip when null or empty.

Subject: `Order #{Id} placed`

```
Hello {UserName},

Your order #{Id} was placed on {CreatedAtUtc}.
Status: Pending

Items:
- {ProductName} x{Quantity} @ {UnitPrice:F2} = {LineTotal:F2}
- ...

Total: {Total:F2}
```

## Order status changed

Recipient: the order's owner (`order.User?.Email`), also when the owner performed the action. Skip when null or empty. Only for `Confirmed` and `Cancelled`.

Subject: `Order #{Id} confirmed` or `Order #{Id} cancelled`

```
Hello {UserName},

Your order #{Id} is now {Confirmed|Cancelled} (updated {UpdatedAtUtc}).

Items:
- ... (same lines)

Total: {Total:F2}
```

For Cancelled the body adds the line `Reserved stock was released.` after the status line.

## Low stock (digest)

Condition per movement (FR-016): `QuantityChange < 0 && QuantityAfter < T && QuantityAfter - QuantityChange >= T`, `T = InventoryOptions.Effective`. If no movement qualifies: return before any query.

Recipients: `Users` with `Role == "Admin"` and `Email != null`, read once. One message per Admin. No Admins with an email: return.

Subject: single product `Low stock: {ProductName}`; several products `Low stock: {n} products`. Product name has CR/LF removed and the subject is truncated to 200 characters.

```
Stock fell below the threshold ({T}):

- #{ProductId} {ProductName}: {QuantityAfter} left (was {QuantityAfter - QuantityChange})
- ...
```

## Safety rules

- Subjects never contain a user name or email; only the order id or (low stock) a product name sanitized as above.
- Bodies may contain user-controlled text (user name, product names). They are plain text; no content type other than `text/plain`. CR/LF inside a product name only affects body lines.
- Recipient addresses come from the database (already normalized and validated at write time) and are re-validated by `EmailMessage.Validate()`.
- Logs: never the body; recipients masked (`a***@example.com`).
