# Data Model: Test Coverage

## Schema impact

**None.** No entity, property, index or relationship changes; no migration. `CategoryProduct.API/Data/ApplicationDbContext.cs` and `CategoryProduct.API/Migrations/**` are untouched. The MVC app has no database.

## Production code touched

| File | Change | Behavior impact |
|---|---|---|
| `CategoryProduct.MVC/Program.cs` | append `public partial class Program { }` | none (makes the generated `Program` public for `WebApplicationFactory`) |
| `CategoryProduct.API/Services/RefreshTokenCleanupWorker.cs` | optional ctor parameter `TimeProvider? timeProvider = null`; `PeriodicTimer(interval, _time)`; purge timestamp from `_time` | none with the default `TimeProvider.System` |

## Test infrastructure types (namespace `CategoryProduct.MVC.Tests`)

| Type | File | Responsibility |
|---|---|---|
| `FakeApi : HttpMessageHandler` | `Support/FakeApi.cs` | route table, response scripting, request log, loud failure on unscripted calls (see `contracts/fake-api.md`) |
| `FakeResponse` | `Support/FakeApi.cs` | status, optional JSON body object, raw bytes + content type, headers, or an exception to throw |
| `RecordedRequest` | `Support/FakeApi.cs` | `Seq`, `Method`, `Path` (no leading slash, lower-cased), `Query`, `Authorization`, `ContentType`, `BodyText`, `BodyBytes` |
| `MvcFactory : WebApplicationFactory<Program>` | `Support/MvcFactory.cs` | MVC host with settings from FR-003; owns one `FakeApi`; cleans temp dirs on dispose |
| `MvcTestBase : IClassFixture<MvcFactory>` | `Support/MvcTestBase.cs` | per-test: `Fake` (reset), `Client` (no redirects, cookies), `GetAsync`, `PostFormAsync`, `SignInUserAsync`, `SignInAdminAsync`, `AssertRedirect` |
| `HtmlHelpers` | `Support/HtmlHelpers.cs` | `ExtractAntiforgeryToken`, `Decode`, `Alert`, `HasText`, `FormAction`, `FieldError` |
| `ApiJson` | `Support/ApiJson.cs` | builders returning anonymous/dictionary objects serialized with web (camelCase) defaults |
| `RecordingAuthenticationService : IAuthenticationService` | `Support/RecordingAuthenticationService.cs` | unit-test double that records `SignInAsync` principals and `SignOutAsync` calls |
| `StubHttpClientFactory : IHttpClientFactory` | `Support/StubHttpClientFactory.cs` | returns `HttpClient` over a given handler with `BaseAddress` |
| `StubHandler : HttpMessageHandler` | `Support/StubHandler.cs` | delegate-based inner handler for handler unit tests, counts calls |

## JSON shapes the MVC deserializes (what `ApiJson` must emit; camelCase)

| Builder | JSON |
|---|---|
| `Login(role, expiresInSeconds = 900, token = "access-N", refresh = "refresh-N")` | `{ token, expiresAtUtc, role, refreshToken, refreshTokenExpiresAtUtc }` (UTC ISO 8601; refresh expiry now + 7 days) |
| `Paged(items, page = 1, pageSize = 5, totalCount?, totalPages?)` | `{ items, page, pageSize, totalCount, totalPages }` (defaults derived from `items.Count`) |
| `Category(id, name)` | `{ id, name }` |
| `Product(id, name, price, quantity, categoryId, imageUrl?, isLowStock)` | `{ id, name, price, quantity, categoryId, imageUrl, isLowStock }` (`imageUrl` non-null only means "has image"; views use the MVC proxy) |
| `StockMovement(...)` | `{ id, productId, quantityChange, quantityAfter, reason, note, userId, userName, createdAtUtc }` |
| `Cart(lines)` / `CartLine(...)` | `{ items: [{ productId, productName, unitPrice, quantity, lineTotal, availableQuantity }], itemCount, total }` |
| `Order(...)` / `OrderLine(...)` | `{ id, userId, userName, status, total, createdAtUtc, updatedAtUtc, items: [{ id, productId, productName, unitPrice, quantity, lineTotal }] }` |
| `OrderSummary(...)` | `{ id, userId, userName, status, total, itemCount, createdAtUtc, updatedAtUtc }` |
| `User(id, userName, role)` | `{ id, userName, role }` |
| `ValidationProblem(field, message...)` | `{ title, status: 400, errors: { field: [messages] } }` |
| `ConflictProblem(detail)` | `{ title, status: 409, detail }` |

Status mapping the MVC applies (from `CategoryProductApiClient.SendAsync/ToResultAsync`), which the fake must trigger: 2xx success; 404 `NotFound`; 403 `Forbidden`; 429 `TooManyRequests`; 401 `Unauthorized`; 409 `Conflict` = `detail` (or the fallback "The request conflicts with existing data."); 400 `Errors` from `errors`; any other non-2xx (not 5xx) `EnsureSuccessStatusCode` throws `HttpRequestException`; 5xx `ApiUnavailableException` (503 page).

## Configuration set by `MvcFactory`

| Key | Value | Reason |
|---|---|---|
| `Api:BaseUrl` | `http://fake-api/` | base address of both clients; never resolved because the primary handler is the fake |
| `Hosting:UseHttpsRedirection` | `false` | TestServer is HTTP |
| `Cookie:SecurePolicy` | `SameAsRequest` | auth cookie must be accepted over HTTP |
| `DataProtection:KeysPath` | `<temp>/cp-mvc-dp-<guid>` | exercises the 020 branch, no machine-level key store |
| `Serilog:WriteTo:1:Args:path` | `<temp>/cp-mvc-logs-<guid>/mvc-.log` | keeps the file sink out of the repo |
| environment | `Development` | see spec Assumptions |

Compose/CI additions: job `compose-smoke` (see `contracts/ci-compose-smoke.md`); generated `.env` keys `SA_PASSWORD`, `JWT_KEY`, `API_HOST_PORT`, `MVC_HOST_PORT`, `LOW_STOCK_THRESHOLD` (same names as `.env.example`).
