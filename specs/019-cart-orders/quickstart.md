# Quickstart: Cart and Orders

Prerequisites: 015, 016, 017 and 018 implemented; API running; an Admin account and a normal user account (Swagger at `/swagger` in Development, use "Authorize" with the login token).

## Automated checks

```
dotnet build CategoryProduct.slnx
dotnet test CategoryProduct.slnx
dotnet ef migrations has-pending-model-changes --project CategoryProduct.API
```

New tests live in `CategoryProduct.API.Tests/CartTests.cs`, `OrderCheckoutTests.cs`, `OrderReadTests.cs`, `OrderLifecycleTests.cs` (plus `CartOrderTestHelpers.cs`) and the added `StageMovement` unit tests in `InventoryTests.cs`. All existing tests must still pass.

## Manual API walkthrough

1. As Admin create `POST api/product` `{ "name": "Bolt", "price": 2.5, "quantity": 5, "categoryId": 1 }` (note the id, call it P). Login as a normal user (`POST api/account/login`).
2. `POST api/cart/items` `{ "productId": P, "quantity": 2 }` -> 200, one line, `total` 5.00. Post it again with quantity 3 -> one line, quantity 5. Post quantity 99 -> 400 on `Quantity`. `PUT api/cart/items/P` `{ "quantity": 4 }` -> 200.
3. `PUT api/cart/items/P` `{ "quantity": 6 }` (stock is 5; the cart accepts it), then `POST api/orders` -> 409 with the product listed in `detail` and in the `items` extension; `GET api/product/P` still shows quantity 5; `GET api/cart` is unchanged.
4. `PUT api/cart/items/P` quantity 2, `POST api/orders` -> 201 `Pending`, `total` 5.00. `GET api/product/P` -> quantity 3. As Admin `GET api/product/P/stock-movements` -> newest is `Sale` -2 by the buyer. `GET api/cart` -> empty. `POST api/orders` again -> 400.
5. As the buyer `GET api/orders` lists the order; `GET api/orders/{id}` -> 200. As another user `GET api/orders/{id}` -> 404 and `POST api/orders/{id}/cancel` -> 404. As Admin `GET api/orders/{id}` -> 200, `GET api/orders/all?status=Pending` lists it, `GET api/orders/all?status=1` -> 400. As the buyer `GET api/orders/all` -> 403.
6. Change the product price as Admin (PUT with the same quantity) and `GET api/orders/{id}`: `unitPrice` and `total` unchanged.
7. As the buyer `POST api/orders/{id}/cancel` -> 200 `Cancelled`; product quantity 5 again; movements show `OrderCancelled` +2. Cancel again -> 409. Admin `PUT api/orders/{id}/status` `{ "status": "Confirmed" }` -> 409 (already Cancelled).
8. Check out another order, then as Admin `PUT .../status` `Confirmed` -> 200; a second `Confirmed` -> 409; `Cancelled` -> 409; the buyer `POST .../cancel` -> 409; the buyer `PUT .../status` -> 403; status `"abc"` -> 400.
9. No token on any cart or order endpoint -> 401.

## Manual MVC walkthrough

1. Anonymous: `/Products` shows no "Add to cart"; nav shows no Cart or My Orders; `/Cart` and `/Orders` redirect to login.
2. Sign in as the normal user: nav shows "Cart" and "My Orders" and not "Manage Orders". Add a product from the list and from Details (success message).
3. `/Cart`: change quantity (Update), Remove, add again, set a quantity above stock and see the warning; Checkout shows the insufficient-stock message and keeps the cart. Fix the quantity and Checkout: you land on `/Orders/{id}` with status Pending.
4. `/Orders` lists the order; Cancel on its page -> status Cancelled; the product's stock is back (check `/Products`).
5. Sign in as Admin: nav shows "Manage Orders"; `/Orders/Manage` lists all orders, the filter works, Confirm and Cancel appear only on Pending rows; confirm one and see it change; open any order from the list.
6. As the normal user open `/Orders/{adminOrderId}` -> 404 and `/Orders/Manage` -> access denied.

## SQL Server steps (not covered by InMemory tests)

1. With a SQL Server connection, apply migrations (the API applies them at startup when enabled; never hand-edit them). Confirm the migration creates `CartItems`, `Orders`, `OrderItems` without a "multiple cascade paths" error.
2. Delete a product that has order history through `DELETE api/product/{id}` -> succeeds; `GET api/orders/{id}` shows the item with `productId` null and the snapshot name; cancelling that order skips the line (check the API log for the "skipped" message) and still succeeds.
3. Delete a product that sits in a user's cart -> the line is gone from `GET api/cart`.
4. Fire two simultaneous `POST api/cart/items` for the same product (for example with two curl commands in parallel) -> one line, quantity is the sum of both, no 500.
5. Fire two simultaneous `POST api/orders` for the same cart and two simultaneous `POST api/orders/{id}/cancel` -> exactly one order and one restore.
6. Atomicity: with a debugger or a temporary failing trigger on `StockMovements`, confirm a failed checkout leaves stock, the cart and `Orders` unchanged (remove the trigger afterwards).
