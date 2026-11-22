# Contract: E2E scenarios

16 tests, each `[E2EFact]`, each independent (own unique user/category/product), each one `RunAsync`. Locators are role/label/text based; ids and ARIA labels only where the views already have them. Texts below come from the views at HEAD and the 026/027 specs; T002 re-verifies them against the implemented views. `Steps.*` helpers are shared.

## Helpers (`Steps`)

| Helper | Does |
|---|---|
| `RegisterAsync(page, user, password)` | `/Account/Register`, fill `User name`, `Password`, `Confirm password`, click `Register`, expect `Hello, <user>` |
| `LoginAsync(page, user, password)` | `/Account/Login`, fill, click `Login`, expect `Hello, <user>` |
| `LogoutAsync(page)` | click navbar `Logout`, expect `Login` link visible |
| `CreateCategoryAsync(page, name)` | `/Categories/Create`, fill `Name`, `Create`, expect row with name |
| `CreateProductAsync(page, name, price, qty, category)` | `/Products/Create`, fill, select category by label, `Create`, expect name in list; returns product id read from the `Details` link href |
| `UploadImageAsync(page, productId, filePayload)` | `/Products/Details/{id}`, set file input, click `Upload image` |
| `AddToCartAsync(page, productId, qty)` | Details page quantity + `Add to cart`, expect `Added to cart.` |
| `CheckoutAsync(page)` | `/Cart`, click `Checkout`, expect `Order placed.`, returns order id from the URL |
| `PayAsync(page, token)` | select `paymentToken` option, click the pay button (name from the 026 view, `Pay now`) |
| `NewShopFixtureAsync(admin page, ...)` | an Admin context creates category + product (qty 5, price 12.50) and returns ids |

Admin sign-in uses `E2E_ADMIN_USERNAME/PASSWORD` in its own context. A test that needs both an Admin and a user uses two contexts (two cookie jars) opened from `fixture.Browser`.

## Tests

| # | Class.Method | Steps and assertions (browser-only value) |
|---|---|---|
| 1 | `AuthTests.Register_Login_Logout_Works` | register a unique user -> `Hello, name`; assert auth cookie exists (`HttpOnly`, `SameSite=Lax`); Logout -> `Login` and `Register` links, no `Hello`, auth cookie gone; login again -> `Hello, name` |
| 2 | `AuthTests.Register_ClientSideValidation_BlocksInvalidFormWithoutRequest` | counter on POST to `/Account/Register`; fill name `ab`, password `abc`, confirm `xyz`, click Register -> field messages visible (`Passwords do not match.`, min-length messages), URL still `/Account/Register`, counter == 0; then fix the fields -> the registration succeeds (counter == 1) |
| 3 | `AuthTests.Login_WrongPassword_ShowsErrorAndStays` | register user, logout, login with another password -> a `.text-danger` summary is visible, URL `/Account/Login`, no `Hello` |
| 4 | `AuthTests.NonAdmin_AdminPages_AccessDenied_And_Anonymous_RedirectToLogin` | anonymous: `/Users`, `/Inventory`, `/Dashboard`, `/Orders/Manage` -> URL contains `/Account/Login` and `ReturnUrl`; registered user: same URLs -> `/Account/AccessDenied`, text `You don't have permission to do this.` |
| 5 | `AuthTests.ForgotPassword_ShowsGenericConfirmation_ForKnownAndUnknownUser` | registered user name and an unknown name each -> `#forgot-confirmation` has the same text; no mail asserted (A6) |
| 6 | `CatalogTests.Category_Create_ClientValidation_And_Success` | Admin: empty Name + Create -> required message visible, no `POST /Categories/Create`; valid name -> in the list |
| 7 | `CatalogTests.Product_Create_ClientValidation_And_Success` | Admin: empty form + Create -> messages for Name and `Please select a category.`, no POST; valid -> product listed with price `12.50` and quantity |
| 8 | `CatalogTests.Product_ImageUpload_RendersImage` | Admin uploads `TestImage.Png` -> `img[alt=<name>]` visible, `naturalWidth > 0` (waited with `WaitForFunctionAsync`, not a sleep), button text `Replace image` |
| 9 | `CatalogTests.Product_ImageUpload_RejectsNonImage` | Admin uploads text bytes named `x.png` (`image/png`) -> an error text visible, no `img[alt]` |
| 10 | `OrderFlowTests.AddToCart_Checkout_OrderAppearsInMyOrders` | Admin creates product qty 5; user registers, adds 2, Cart shows line total 25.00, Checkout -> `Order placed.`, `Pending` badge, total 25.00; `/Orders` lists `#<id>`; `/Cart` shows `Your cart is empty.` |
| 11 | `OrderFlowTests.Pay_WithApproveToken_MarksOrderPaid` | order from #10 flow; select `tok_visa`, pay -> `Payment received.`, `Paid` badge visible, pay form gone |
| 12 | `OrderFlowTests.Pay_WithDeclineToken_ShowsError_AndOrderStaysUnpaid` | select `tok_decline`, pay -> alert contains `Payment declined`; pay form still visible; no `Paid` badge |
| 13 | `OrderFlowTests.Cancel_PaidOrder_Refunds_AndRestoresStock` | pay `tok_visa`, click `Cancel order` -> `Cancelled` and `Refunded` badges; Admin context opens `/Inventory/History/{productId}` -> `Quantity: 5` |
| 14 | `OrderFlowTests.Admin_Confirms_Order_OnManageOrders` | user places an order; Admin `/Orders/Manage`, locate the row by `#<id>` (row filtered by text), click `Confirm` -> row shows `Confirmed`, the row has no `Confirm` button |
| 15 | `DashboardTests.Dashboard_Renders_Kpis_And_Svg_Charts` | Admin: `kpi-orders`, `kpi-revenue`, `kpi-aov`, `kpi-new-users`, `kpi-low-stock` visible; both `svg[aria-label]` contain >= 1 `rect`; no console `error` messages; `from` after `to` + Show -> field message visible, cards absent |
| 16 | `ErrorPageTests.ErrorPage_ShowsGenericMessage_WithoutStackTrace` | GET `/Home/Error` -> `Something went wrong`; page text does not contain `   at `, `Exception`, `.cs:line` |

## Data and independence rules

- Tests 10-14 each create a product with quantity 5 and a user; tests never share them.
- Stock assertions only for the product of the same test.
- Dashboard assertions are structural (numbers vary with other tests' data).
- A test never depends on another test's data or on execution order.

## Selector policy

`GetByRole(Button, Name=...)`, `GetByLabel(...)`, `GetByText(...)`, `GetByRole(Row, Name=...)`; ids/ARIA labels that already exist; `locator("select[name=paymentToken]")` and `input[type=file]` where no accessible name exists. No XPath, no nth-child, no CSS class chains except `.alert-danger` / `.alert-success` / `.badge` which are the views' own semantic classes.
