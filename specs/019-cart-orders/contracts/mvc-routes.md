# Contract: MVC routes (CategoryProduct.MVC)

`CartController` and `OrdersController` are `[Authorize]` (anonymous users are redirected to `/Account/Login`); `Manage` and `SetStatus` are `[Authorize(Roles = "Admin")]` (non-Admins go to access denied, like `UsersController`). Every POST uses `[ValidateAntiForgeryToken]`. The API is reached only through `CategoryProductApiClient` with the bearer token. On API 401 the user is signed out and sent to login (`SessionExpiredAsync` pattern).

## Cart

| Route | Action | Notes |
|---|---|---|
| `GET /Cart` | `Index` (conventional) | lines (name, unit price, editable quantity + Update, line total, "Only N in stock" warning when `availableQuantity < quantity`, Remove), cart total, "Clear cart", "Checkout" (hidden when empty; message "Your cart is empty.") |
| `POST /Cart/Add` | `Add(int productId, int quantity = 1, string? returnUrl)` | calls `POST api/cart/items`; success -> `TempData["Success"]` "Added to cart." and redirect to `returnUrl` if `Url.IsLocalUrl`, else to `Products/Index`; 400/404/409 -> `TempData["Error"]` and the same redirect |
| `POST /Cart/Update` | `Update(int productId, int quantity)` | `PUT api/cart/items/{productId}`; redirect to `Index`; errors via `TempData["Error"]` |
| `POST /Cart/Remove` | `Remove(int productId)` | `DELETE api/cart/items/{productId}`; redirect to `Index` |
| `POST /Cart/Clear` | `Clear()` | `DELETE api/cart`; redirect to `Index` |
| `POST /Cart/Checkout` | `Checkout()` | `POST api/orders`; 201 -> redirect to `/Orders/{id}` with `TempData["Success"]` "Order placed."; 400 -> "Your cart is empty."; 409 -> `TempData["Error"]` with the API `detail` (which products are short) and redirect to `Index`; 401 -> login |

## Orders

| Route | Action | Notes |
|---|---|---|
| `GET /Orders?page=1` | `Index` (conventional) | caller's orders (10 per page via `GET api/orders`), columns id, date (local time), status badge, item count, total, Details link; Previous/Next like `Views/Users/Index.cshtml` |
| `GET /Orders/Manage?page=1&status=` | `Manage` (attribute route `Orders/Manage`, Admin only) | `GET api/orders/all`; filter dropdown All/Pending/Confirmed/Cancelled; columns id, user name, date, status, total, Details; Confirm and Cancel buttons only on Pending rows; paging keeps the status filter |
| `GET /Orders/{id:int}` | `Details` (attribute route) | `GET api/orders/{id}`; 404 -> `NotFound()` (unknown or not yours); items table (name, unit price, quantity, line total; "(product removed)" marker when `productId` is null), total, status badge; Cancel button when status is Pending (owner or Admin); Admin also sees Confirm |
| `POST /Orders/{id:int}/Cancel` | `Cancel(int id, string? returnTo)` (attribute route) | `POST api/orders/{id}/cancel`; success `TempData["Success"]` "Order cancelled."; 409/404 -> `TempData["Error"]`; redirect to `Details` (or to `Manage` when `returnTo == "manage"`) |
| `POST /Orders/{id:int}/Status` | `SetStatus(int id, string status, string? returnTo, int page, string? filter)` (attribute route, Admin only) | `PUT api/orders/{id}/status`; success "Order updated."; 409 (already changed) or 400 -> `TempData["Error"]`; redirect back to `Manage` preserving `page` and `filter`, or to `Details` |

Route ordering: `Orders/Manage` is a literal and `Orders/{id:int}` has an int constraint, so they never collide.

## Existing pages changed

| Page | Change |
|---|---|
| `GET /Products` (`Views/Products/Index.cshtml`) | for signed-in users each row gets a small form posting `productId`, `quantity` (number 1..100, default 1) and `returnUrl` to `/Cart/Add`, with anti-forgery token; hidden for anonymous visitors |
| `GET /Products/Details/{id}` | same form under the details |
| `_Layout.cshtml` | for authenticated users: nav items "Cart" (`Cart/Index`) and "My Orders" (`Orders/Index`); inside the existing `@if (User.IsInRole("Admin"))` block: "Manage Orders" (`asp-controller="Orders" asp-action="Manage"`) |

## API calls (`CategoryProductApiClient`)

`GetCartAsync()`, `AddCartItemAsync(productId, quantity)`, `UpdateCartItemAsync(productId, quantity)`, `RemoveCartItemAsync(productId)`, `ClearCartAsync()`, `CheckoutAsync()` (returns `ApiResult<OrderModel>`), `GetMyOrdersAsync(page, pageSize)`, `GetOrderAsync(id)` (404 -> not found result), `GetAllOrdersAsync(status, page, pageSize)`, `CancelOrderAsync(id)`, `SetOrderStatusAsync(id, status)`. All attach the bearer token; mutations use a generic `SendAsync<T>` that maps 400/401/403/404/409/429 like the existing `SendAsync` and returns the 2xx body in `ApiResult<T>.Value`.
