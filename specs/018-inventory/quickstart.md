# Quickstart: Inventory

Prerequisites: 015, 016 and 017 implemented; API running with an Admin account (Swagger at `/swagger` in Development, use "Authorize" with the login token).

## Automated checks

```
dotnet build CategoryProduct.slnx
dotnet test CategoryProduct.slnx
```

New tests live in `CategoryProduct.API.Tests/InventoryTests.cs` (endpoints, status codes, initial movement, PUT rule, low-stock, threshold override, interceptor-driven concurrency retry, `InventoryService` unit tests). All existing tests must still pass.

## Manual API walkthrough (threshold 5)

1. Login as Admin (`POST api/account/login`), copy `token`.
2. `POST api/product` `{ "name": "Bolt", "price": 1, "quantity": 7, "categoryId": 1 }` -> 200, `isLowStock` false.
3. `GET api/product/{id}/stock-movements` -> one item, reason `Initial`, `quantityAfter` 7.
4. `POST api/product/{id}/stock-movements` `{ "quantityChange": -3, "reason": "Damage" }` -> 201, `quantityAfter` 4. The API log shows a `Low stock` warning.
5. `GET api/product/{id}` -> `quantity` 4, `isLowStock` true. `GET api/product/low-stock` lists it (note the literal route works).
6. `POST` `{ "quantityChange": -10, "reason": "Damage" }` -> 409; `{ "quantityChange": 0, "reason": "Restock" }` -> 400; `{ "quantityChange": 1, "reason": "Sale" }` -> 400; unknown id -> 404; no token -> 401; token of a non-Admin -> 403.
7. `PUT api/product/{id}` with `quantity` 99 -> 400 on `Quantity`; with `quantity` 4 and a new name -> 200.
8. Restock +5 -> `quantityAfter` 9, `isLowStock` false, product leaves `low-stock`.

## Manual MVC walkthrough

1. Sign in as Admin: nav shows "Inventory"; as a normal user or anonymous it does not, and `/Inventory` redirects.
2. `/Inventory`: banner counts low-stock products; badges on flagged rows.
3. Open a product: add Restock +5, see the success message and a new row; add -100 and see the conflict message.
4. `/Products/Edit/{id}`: Quantity is read-only; saving a name change succeeds.

## SQL Server only checks (not provable on InMemory)

1. `dotnet ef migrations list --project CategoryProduct.API` shows `AddInventory`; apply it with the normal startup migration (016) or your usual process (never hand-edit).
2. Delete a product that has movements, then `SELECT COUNT(*) FROM StockMovements WHERE ProductId = <id>` is 0 (cascade).
3. Fire two restocks at once (two terminals or a short script): both appear in history and the final quantity includes both.
