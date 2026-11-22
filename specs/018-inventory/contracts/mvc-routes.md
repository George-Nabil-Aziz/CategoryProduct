# Contract: MVC routes (CategoryProduct.MVC)

`InventoryController` is `[Authorize(Roles = "Admin")]`; anonymous users are redirected to login and non-Admins to access denied, as for `UsersController`.

| Route | Action | Notes |
|---|---|---|
| `GET /Inventory?page=1` | `Index` | conventional route; products paged (10 per page) with quantity and "Low stock" badge (`IsLowStock`); banner "N product(s) low on stock" from `GET api/product/low-stock?pageSize=1` `totalCount`; link per row to history |
| `GET /Inventory/Product/{id:int}?page=1` | `History` (attribute route) | 404 if the product is unknown; product header (name, quantity, low-stock badge); movements table (time, change, quantity after, reason, note, user), 10 per page, Previous/Next; add-movement form |
| `POST /Inventory/Product/{id:int}` | `AddMovement` (attribute route, `[ValidateAntiForgeryToken]`) | fields `quantityChange`, `reason` (dropdown: Restock, Adjustment, Damage, Return), `note`; success -> `TempData["Success"]` + redirect to `History`; 400/409 -> `TempData["Error"]` with the API message(s) + redirect; 401 -> sign out and redirect to login; 404 -> `NotFound()` |
| `GET /Products/Edit/{id}` | existing | Quantity input is `readonly` with the hint "Change quantity from Inventory"; the value is still posted so PUT carries the unchanged quantity |

Navigation: `_Layout.cshtml` adds "Inventory" (`asp-controller="Inventory" asp-action="Index"`) inside the existing `@if (User.IsInRole("Admin"))` block.

API calls (`CategoryProductApiClient`): `GetStockMovementsAsync(id, page, pageSize)` (authenticated GET, returns null on 404), `GetLowStockCountAsync()` (authenticated GET), `AddStockMovementAsync(id, StockMovementRequest)` (existing `SendAsync` path, so 400 errors, 409 `Conflict` message, 401, 403 map to `ApiResult`).
