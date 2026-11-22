# Contract: FakeApi

`CategoryProduct.MVC.Tests/Support/FakeApi.cs`. A `HttpMessageHandler` registered as the primary handler of every `HttpClient` in the MVC host (and used directly in handler unit tests).

## Routing

- A route is `(HttpMethod, path)`. `path` is compared case-insensitively against `request.RequestUri.AbsolutePath` with leading and trailing `/` removed, e.g. `api/category`, `api/product/7/image`, `api/orders/all`.
- Query strings are not part of the match; they are recorded. A route may add an optional predicate on the query (e.g. `q => q.Contains("page=2")`) so the same path can serve different pages; the first matching route that was registered last wins (later registrations override earlier ones).
- No match: the handler throws `FakeApiUnmatchedException` ("No FakeApi route for GET api/foo?x=1. Scripted: ...") and still records the request. It is NOT an `HttpRequestException`, so `ApiErrorHandler` does not turn it into the 503 page; the exception surfaces through the test client and fails the test with a readable message.

## Scripting API (names are normative, implementer may add overloads)

```csharp
fake.On(HttpMethod.Get, "api/category", FakeResponse.Json(200, ApiJson.Paged(...)));
fake.On(HttpMethod.Post, "api/cart/items", req => FakeResponse.Json(409, ApiJson.ConflictProblem("Not enough stock")));
fake.OnSequence(HttpMethod.Get, "api/cart", r1, r2, r3);   // n-th call returns n-th response, last repeats
fake.OnQuery(HttpMethod.Get, "api/orders", q => q.Contains("page=2"), FakeResponse.Json(200, page2));
fake.Throw(HttpMethod.Get, "api/category", new HttpRequestException("down"));   // connection failure
fake.Hang(HttpMethod.Get, "api/category");                                       // throws TaskCanceledException (timeout) when the caller token is not cancelled
fake.Reset();                                                                    // clears routes and requests
fake.Requests                                                                    // IReadOnlyList<RecordedRequest>, ordered
fake.Calls(HttpMethod.Post, "api/account/refresh")                               // filtered list
fake.Single(HttpMethod.Put, "api/product/3")                                     // asserts exactly one, returns it
```

`FakeResponse` factories: `Json(status, object body)` (serialized with `JsonSerializerDefaults.Web`), `Status(status)` (empty body, e.g. 204/401/403/404/429), `Bytes(status, byte[] bytes, string contentType)`, `Raw(status, string text, string contentType)`.

## Recording

Every call appends a `RecordedRequest { Seq, Method, Path, Query, Authorization, ContentType, BodyText, BodyBytes }` before the response is produced. The body is read inside the handler (`ReadAsByteArrayAsync`), so multipart uploads are visible (`BodyText` contains `name="file"; filename="x.png"` and the content type part). `Authorization` is the raw header value (`Bearer <token>`) or `null`. Recording is thread-safe (`lock`).

## Rules the tests rely on

- The fake never validates the bearer by itself; tests that need "valid token accepted, old token rejected" script a computed route that inspects `req.Authorization` (e.g. 401 for `Bearer old`, 200 for `Bearer new`).
- Default login/refresh payloads come from `ApiJson.Login(...)`; `MvcTestBase` scripts `POST api/account/login` and `POST api/account/refresh` only when asked, never implicitly, so "unscripted refresh" fails loudly.
- `FakeApi` is created once per `MvcFactory` and shared by the typed and refresh clients; the factory prevents its disposal by the `HttpClientFactory` (override `Dispose(bool)` to no-op, real cleanup in `MvcFactory.Dispose`).
- Content: `HttpResponseMessage.RequestMessage` is set to the request so `EnsureSuccessStatusCode` messages stay useful.

## API contract the fake imitates

Taken from `CategoryProductApiClient`; every route below must be scriptable.

| Client call | Method and path (relative to `Api:BaseUrl`) | Success body |
|---|---|---|
| Login | `POST api/account/login` `{userName,password}` | LoginResult; 401 bad creds; 429 |
| Refresh | `POST api/account/refresh` `{refreshToken}` | LoginResult; 401 |
| Logout | `POST api/account/logout` `{refreshToken}` | 204 |
| Register | `POST api/account/register` `{userName,password}` | 2xx; 400 errors; 409; 429 |
| Users | `GET api/users?page&pageSize&search`; `PUT api/users/{id}/role` `{role}` | Paged users; 2xx / 409 / 400 / 404 |
| Categories | `GET api/category?page&pageSize&search`; `GET api/category/{id}`; `GET api/category/{id}/products`; `POST api/category`; `PUT api/category/{id}`; `DELETE api/category/{id}` | Paged; Category; Product[] |
| Products | `GET api/product?page&pageSize&search&categoryId&minPrice&maxPrice&sortBy&desc`; `GET api/product/{id}`; `POST api/product`; `PUT api/product/{id}`; `DELETE api/product/{id}` | Paged; Product |
| Images | `GET api/product/{id}/image`; `POST api/product/{id}/image` (multipart `file`); `DELETE api/product/{id}/image` | bytes + content type; 404 |
| Inventory | `GET api/product/low-stock?page=1&pageSize=1`; `GET api/product/{id}/stock-movements?page&pageSize`; `POST api/product/{id}/stock-movements` `{quantityChange,reason,note}` | Paged |
| Cart | `GET api/cart`; `POST api/cart/items` `{productId,quantity}`; `PUT api/cart/items/{productId}` `{quantity}`; `DELETE api/cart/items/{productId}`; `DELETE api/cart` | Cart |
| Orders | `POST api/orders`; `GET api/orders?page&pageSize`; `GET api/orders/{id}`; `GET api/orders/all?page&pageSize&status`; `POST api/orders/{id}/cancel`; `PUT api/orders/{id}/status` `{status}` | Order; Paged OrderSummary |

Note `GET api/category` is also called by `GetAllCategoriesAsync` (loops pages of 50 until `totalPages`), so Products Index/Create/Edit tests must script `api/category` too.
