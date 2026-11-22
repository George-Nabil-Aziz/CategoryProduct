# Contract: MVC test matrix

Every row is at least one test. "Req" lists what the test must assert on `fake.Requests` in addition to the response. Class names are the files in `CategoryProduct.MVC.Tests/`. A (G2)/(G3) marker is the implementing group from `tasks.md`.

Legend: `A` = signed in as Admin, `U` = signed in as User, `anon` = anonymous. Redirect assertions compare path+query of `Location`.

## AccountTests (G2)

| Action | Scenario | Expect | Req |
|---|---|---|---|
| GET Login | anon | 200, form with token, `returnUrl` kept in form action | none |
| POST Login | ok User | 302 `/Categories`, auth cookie set; next page shows `Hello, <name>` | login body has userName/password; no Authorization |
| POST Login | ok Admin | 302; layout shows Users, Inventory, Manage Orders links | |
| POST Login | local returnUrl | 302 to returnUrl | |
| POST Login | external returnUrl (`https://evil.test/`) | 302 `/Categories` | |
| POST Login | API 401 | 200 page contains "Invalid username or password.", no auth cookie | |
| POST Login | API 429 | 200 page contains "Too many login attempts. Try again in a minute." | |
| POST Login | empty fields | 200 with validation, API not called | `fake.Requests` empty |
| POST Login | no antiforgery token | 400 | |
| GET Register | anon | 200 form | |
| POST Register | ok | register then login called in order, 302 `/Categories`, cookie set | bodies; register has no Authorization |
| POST Register | ok but login 401 | 302 `/Account/Login` | |
| POST Register | API 409 | "Username already taken." | |
| POST Register | API 429 | "Too many attempts. Try again in a minute." | |
| POST Register | API 400 `{UserName:[..],Password:[..]}` | messages shown (field-bound) | |
| POST Register | other 400 key | message shown in summary | |
| POST Register | passwords differ / short password / bad chars | 200 with model error, API not called | |
| POST Logout | signed in | 302 `/Categories`, cookie cleared (next GET anonymous), API got `POST api/account/logout` with `{refreshToken}` | Authorization is the access token (022) |
| POST Logout | API throws `HttpRequestException` | still 302 and signed out | |
| POST Logout | API 500 | still signed out (503 from handler is swallowed by `LogoutAsync`) | |
| GET AccessDenied | U | 200 | |
| Cookie | expiry follows refresh token | `Set-Cookie` has an `expires` near `refreshTokenExpiresAtUtc` | |

## AnonymousAccessTests (G2) (Theory over URLs)

| Case | Expect |
|---|---|
| anon GET `/Cart`, `/Orders`, `/Orders/1`, `/Orders/Manage`, `/Inventory`, `/Inventory/Product/1`, `/Users`, `/Categories/Create`, `/Categories/Edit/1`, `/Categories/Delete/1`, `/Products/Create`, `/Products/Edit/1`, `/Products/Delete/1` | 302 to `/Account/Login?ReturnUrl=<encoded url>`; API not called |
| anon POST (with a valid token from the login page) to `/Cart/Add`, `/Cart/Checkout`, `/Orders/1/Cancel`, `/Inventory/Product/1`, `/Users/ChangeRole`, `/Products/UploadImage/1`, `/Products/RemoveImage/1`, `/Categories/Create` | 302 to login; API not called |
| U GET `/Users`, `/Inventory`, `/Orders/Manage`, `/Categories/Create`, `/Products/Create`, `/Products/Edit/1` | 302 to `/Account/AccessDenied`; API not called |
| U POST `/Orders/1/Status`, `/Users/ChangeRole`, `/Products/UploadImage/1` | 302 to AccessDenied |
| anon GET `/`, `/Categories`, `/Products`, `/Products/Details/1`, `/Categories/Details/1`, `/Products/Image/1`, `/Account/Login`, `/Account/Register`, `/Account/AccessDenied` | not a login redirect |

## CategoriesTests (G2)

| Action | Scenario | Expect | Req |
|---|---|---|---|
| Index | anon | rows rendered, no Create link, paging links | `GET api/category?page=1&pageSize=5&search=` without Authorization |
| Index | page 2 + search | query reaches API; search box keeps value; Previous/Next links keep `search` | query string |
| Index | `page=0` | clamped to 1 | `page=1` |
| Index | A | Create link visible | |
| Details | found | name + products listed | calls `api/category/{id}` and `/products` |
| Details | 404 | 404 response | |
| Create GET/POST | A ok | 302 `/Categories`; body `{name}` | Authorization Bearer present |
| Create POST | API 400 / 409 / 403 | form re-rendered with message ("You don't have permission to make changes." etc.) | |
| Create POST | API 401 (refresh also fails) | 302 to login with returnUrl, cookie cleared | refresh called once |
| Create POST | invalid model (name too short) | 200, API not called | |
| Edit GET | A ok / 404 | form prefilled / 404 | |
| Edit POST | ok / id mismatch / API 404 / 400 | 302 / 404 / 404 / form | `PUT api/category/{id}` body |
| Delete GET/POST | ok / API 409 (has products) / API 404 | 302 / delete page with message / 404 | `DELETE` with Bearer |

## ProductsTests (G2)

| Action | Scenario | Expect | Req |
|---|---|---|---|
| Index | anon, filters | table, category names resolved, Add-to-cart hidden, Create/Edit/Delete hidden | query string has `categoryId`, `minPrice`, `maxPrice`, `sortBy`, `desc=true`; decimals use invariant culture; `GET api/category` also called |
| Index | U | Add-to-cart form with hidden `returnUrl` | |
| Index | A | Create/Edit/Delete links | |
| Index | item with imageUrl | `<img src="/Products/Image/{id}">`, response never contains `fake-api` | |
| Details | found | fields, category name, Add-to-cart for U, upload form for A only | |
| Details | 404 | 404 | |
| Create GET | A | category select populated | |
| Create POST | ok | 302 `/Products`; body `{name,price,quantity,categoryId}` | Bearer |
| Create POST | API 400 field errors | field messages next to inputs, select repopulated | |
| Create POST | model invalid (CategoryId 0) | "Please select a category." | API not called |
| Edit GET | A | Quantity input has `readonly`, Inventory hint link to `/Inventory/Product/{id}` | |
| Edit POST | ok | PUT body keeps posted quantity | Bearer |
| Edit POST | API 400 key `Quantity` | "Use stock movements to change quantity." rendered in the Quantity validation span | |
| Edit POST | 404 / id mismatch | 404 | |
| Delete GET/POST | ok / 400 / 404 | 302 / Delete page re-rendered (200; message not asserted unless the view shows a summary) / 404 | `DELETE api/product/{id}` Bearer |
| Create/Edit/Delete POST | API 409 | page re-renders with status 200 and no exception; message NOT asserted (`ProductsController.AddErrors` has no Conflict branch, see spec Known Gaps) | |

## ProductImageTests (G2)

| Action | Scenario | Expect | Req |
|---|---|---|---|
| Image | API 200 png bytes | same bytes, `Content-Type: image/png`, `X-Content-Type-Options: nosniff`, `Cache-Control: no-cache`; works anonymous | `GET api/product/{id}/image` |
| Image | API 404 | 404 | |
| Image | API 5xx | 503 service-unavailable page | |
| UploadImage | A, valid png | 302 `/Products/Details/{id}` | multipart POST, Bearer, part `file`, filename and content type preserved, byte content equal |
| UploadImage | no file | 200 Details with "Please choose an image file." | no upload call |
| UploadImage | API 400 `{file:["Only JPG, PNG..."]}` | Details re-rendered with message | |
| UploadImage | API 404 / 409 | 404 / Details re-rendered (200), 409 message not asserted (same gap) | |
| UploadImage | non-admin | AccessDenied redirect | |
| RemoveImage | A ok | 302 Details | `DELETE api/product/{id}/image` Bearer |
| RemoveImage | API 404 / 400 | 404 / error | |
| RemoveImage | non-admin | AccessDenied | |

## UsersTests (G2)

| Action | Scenario | Expect | Req |
|---|---|---|---|
| Index | A | users with role selects | `GET api/users?page=1&pageSize=10&search=` Bearer |
| Index | search + page 2 | query reaches API; paging links keep search | |
| Index | A: Previous/Next only when applicable | | |
| Index | U / anon | AccessDenied / login redirect | API not called |
| ChangeRole | ok | 302 `/Users?page=..&search=..`; next page "Role updated." | `PUT api/users/{id}/role` body `{role}` |
| ChangeRole | 409 | next page shows API detail (e.g. "Cannot remove the last admin.") | |
| ChangeRole | 400 | joined messages | |
| ChangeRole | 404 | "User not found." | |
| ChangeRole | 403 | "Could not change role." | |

## InventoryTests (G3)

| Action | Scenario | Expect | Req |
|---|---|---|---|
| Index | A, low stock total 3 | "3 product(s) low on stock", Low stock badge on flagged rows | `GET api/product?page=1&pageSize=10`, `GET api/product/low-stock?page=1&pageSize=1`, Bearer on low-stock |
| Index | A, low stock total 0 | no banner | |
| Index | paging | Next/Previous | |
| Index | U | AccessDenied | |
| Index | API 401 + refresh fails | 302 login with returnUrl | |
| History | A | product name, quantity, rows (+ sign for positive), "-" for empty note/user | `GET api/product/{id}`, `GET api/product/{id}/stock-movements?page=1&pageSize=10` Bearer |
| History | product 404 / movements 404 | 404 | |
| History | page 2 | query + Previous link | |
| History | API 401 | login redirect | |
| AddMovement | ok | 302 `/Inventory/Product/{id}?page=..`, then "Stock movement recorded." | body `{quantityChange,reason,note}`, Bearer |
| AddMovement | 409 | detail in error alert | |
| AddMovement | 400 | joined messages | |
| AddMovement | 403 / 429 | permission / too-many messages | |
| AddMovement | 404 | 404 | |
| AddMovement | 401 | login redirect | |

## CartTests (G3)

| Action | Scenario | Expect | Req |
|---|---|---|---|
| Index | U, lines | names, N2 prices/totals, "Only N in stock" when available < quantity | `GET api/cart` Bearer |
| Index | empty | "Your cart is empty." + browse link | |
| Index | API 401 | login redirect | |
| Add | ok | 302 `/Products`; "Added to cart." | body `{productId,quantity}` Bearer |
| Add | local returnUrl / external returnUrl | redirect there / `/Products` | |
| Add | 409 / 400 / 404 / 429 | error flash texts ("Not found.", "Too many requests. Please try again later.") | |
| Update | ok / 409 | "Cart updated." / detail | `PUT api/cart/items/{id}` body `{quantity}` |
| Remove | ok / 404 | "Item removed." / "Not found." | `DELETE api/cart/items/{id}` |
| Clear | ok | "Cart cleared." | `DELETE api/cart` |
| Checkout | ok | 302 `/Orders/{id}`; "Order placed." shown on details | `POST api/orders` Bearer |
| Checkout | 409 | cart page shows detail | |
| Checkout | 400 | "Your cart is empty." | |
| Checkout | 401 | login redirect | |
| any POST | anon | login redirect | |

## OrdersTests (G3)

| Action | Scenario | Expect | Req |
|---|---|---|---|
| Index | U | rows, status badge, paging, Details links | `GET api/orders?page=1&pageSize=10` Bearer |
| Index | page 2 | query | |
| Index | API 401 | login redirect | |
| Details | Pending | items, total, Cancel form present | `GET api/orders/{id}` |
| Details | Confirmed / Cancelled | no Cancel form | |
| Details | removed product | "(product removed)" | |
| Details | admin viewing | user name shown, Confirm button for Pending | |
| Details | 404 | 404 | |
| Cancel | ok | 302 Details; "Order cancelled." | `POST api/orders/{id}/cancel` |
| Cancel | 409 | detail | |
| Cancel | 404 | "Order not found." | |
| Cancel | admin with `returnTo=manage` | 302 Manage with `page`/`status`; user with `returnTo=manage` goes to Details | |

## ManageOrdersTests (G3)

| Action | Scenario | Expect | Req |
|---|---|---|---|
| Manage | A | all users' orders | `GET api/orders/all?page=1&pageSize=10&status=` Bearer |
| Manage | `status=pending` (lower-case) | sent as `Pending`, option selected | |
| Manage | `status=bogus` | sent empty | |
| Manage | paging keeps filter | links contain `status=` | |
| Manage | U / anon | AccessDenied / login | |
| SetStatus | Confirm ok | 302 Manage keeping page/filter; "Order updated." | `PUT api/orders/{id}/status` body `{status:"Confirmed"}` |
| SetStatus | 409 invalid transition | detail shown | |
| SetStatus | 400 | joined messages | |
| SetStatus | no `returnTo` | 302 Details | |
| SetStatus | U | AccessDenied | API not called |

## SessionExpiryTests (G3) (Theory)

For each of: Categories POST Create, Products POST Edit, Inventory GET Index, Inventory GET History, Inventory POST AddMovement, Cart GET Index, Cart POST Add, Orders GET Index, Orders GET Details, Orders GET Manage, Orders POST Cancel:
- API answers 401 to the page call and 401 to `POST api/account/refresh`: 302 to `/Account/Login?returnUrl=<original path+query>`, and the next request with the same client is anonymous (admin links gone / protected page redirects).
- Exactly one refresh call is recorded.
- API answers 401 then (after refresh 200) 200 on retry: the page succeeds, two calls to the page route, second carries the new token, and a new auth cookie was issued.

Users is excluded (see spec Known Gaps).

## ApiUnavailableTests (G3)

| Case | Expect |
|---|---|
| `GET /Categories` with `fake.Throw(..., HttpRequestException)` | 503, page contains "Service unavailable" and `ApiUnavailableException.FriendlyMessage` |
| `GET /Products` API 500 / 502 | 503 same page |
| API timeout (`fake.Hang`) | 503 |
| Authenticated Cart with API 500 | 503 |
| Logout with API down | not 503, signed out |
| Login with API down | 503 (login is not best-effort) |

## Handler unit tests (G3)

`TokenRefreshHandlerTests`: see FR-008. Cases: no bearer or anonymous user passes through untouched (inner called once, refresh never); bearer + token expiring in 30 s: refresh first, inner called once with new token, principal re-signed with new claims (`RecordingAuthenticationService`); token expiring in 15 min: no refresh; inner 401 then refresh ok: inner called twice, retry has new bearer, same method/URI/body bytes/content-type; refresh returns 401: response is the original 401, inner called once; refresh throws `HttpRequestException`: original 401; refresh returns 200 with `null`/invalid JSON: original 401; pre-emptive refresh already tried then 401: no second refresh, original 401 returned; two requests on the same `HttpContext` after a failed refresh do not retry refresh; missing refresh claim: no refresh call; malformed `access_token_expires_at`: treated as not expiring.
`ApiErrorHandlerTests`: `HttpRequestException` to `ApiUnavailableException` with inner exception and friendly message; `TaskCanceledException` with non-cancelled caller token to `ApiUnavailableException`; caller token cancelled to `TaskCanceledException` propagates; 500/503 to `ApiUnavailableException` (response disposed); 400/401/404/409 returned untouched; 2xx returned.
`ApiUnavailableFilterTests`: filter turns `ApiUnavailableException` into a `ViewResult` named `ServiceUnavailable` with status 503 and `ViewData["Message"]`; other exceptions untouched and `ExceptionHandled` false.
`AuthSessionServiceTests`: claims (`Name`, `Role`, `access_token`, `refresh_token`, `access_token_expires_at` in round-trip "O" format, UTC), `IsPersistent=false`, `ExpiresUtc` equals refresh expiry as UTC, `http.User` replaced.
`InfrastructureTests` (G1): factory boots; Login page has token; FakeApi route, sequence, unscripted-call failure, recording of body and Authorization; `SignInAdminAsync` yields cookie.

## Coverage check (SC-002)

Controller actions (from the code): Account: Login GET/POST, Register GET/POST, AccessDenied, Logout. Categories: Index, Details, Create GET/POST, Edit GET/POST, Delete GET, DeleteConfirmed. Products: Index, Details, Create GET/POST, Edit GET/POST, Delete GET, DeleteConfirmed, Image, UploadImage, RemoveImage. Inventory: Index, History, AddMovement. Cart: Index, Add, Update, Remove, Clear, Checkout. Orders: Index, Details, Cancel, Manage, SetStatus. Users: Index, ChangeRole. All appear above.
