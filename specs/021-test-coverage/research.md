# Research: Test Coverage

## R1. Fake the HTTP boundary

**Decision**: replace the primary `HttpMessageHandler` of every `HttpClient` with `FakeApi`.
**Rationale**: `CategoryProductApiClient` is a typed client whose behavior (status mapping to `ApiResult`, `EnsureSuccessStatusCode`, JSON options, bearer attachment) is the code under test, as are `ApiErrorHandler` and `TokenRefreshHandler`, which are `DelegatingHandler`s in the same pipeline. A fake underneath keeps all of them real.
**Alternatives**: mock `CategoryProductApiClient` (skips the handlers and status mapping, rejected); run the real API in a second `WebApplicationFactory` and route MVC's client to it (heavy, couples MVC tests to API changes and InMemory seeding, rejected; this would be an end-to-end suite, a different goal).

## R2. Registering the fake for both clients

`Program.cs` registers `AddHttpClient(TokenRefreshHandler.RefreshClientName, ...)` and `AddHttpClient<CategoryProductApiClient>(...).AddHttpMessageHandler<ApiErrorHandler>().AddHttpMessageHandler<TokenRefreshHandler>()`. `services.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => fake))` (Microsoft.Extensions.Http, .NET 8+) applies to all named and typed clients, including the refresh client, without touching their names. The same `FakeApi` instance is returned for both so one request log shows the whole conversation (page call, refresh call, retry). `FakeApi` must not be disposed by the handler lifetime: pass a non-disposing wrapper or override `Dispose(bool)` to no-op (the factory's handler lifetime is 2 minutes and `HttpMessageHandler` instances are reused/disposed by the factory).

If `ConfigureHttpClientDefaults` order relative to the `AddHttpClient` calls ever matters, fallback is `services.AddHttpClient<CategoryProductApiClient>().ConfigurePrimaryHttpMessageHandler(...)` plus the same for the named client (after `Program.cs` registrations via `ConfigureTestServices`).

## R3. `Program` accessibility and name clash

The MVC `Program.cs` uses top-level statements, so the generated `Program` is `internal`. Adding `public partial class Program { }` as the last statement (same as the API) makes it usable. Both apps then define a global `Program`, but the MVC test project references only `CategoryProduct.MVC`, so the name is unambiguous there; `CategoryProduct.API.Tests` references only the API. Verified from csproj files: neither test project references the other app. If the two test projects were ever merged, the types would clash, which is one more reason to keep them separate.

## R4. Static web assets and Development environment

`app.MapStaticAssets()` reads the `CategoryProduct.MVC.staticwebassets.endpoints.json` manifest from the output folder; for a project referenced from a test project the SDK copies it, and `WebApplicationFactory` also locates the content root of the MVC project (the API tests already rely on this with `.slnx`). Risk: in the test output the manifest might be missing and `MapStaticAssets` could throw at startup. Mitigation, in order: use the default `Development` environment (static web assets are discovered from the project in Development); if startup still fails, set `UseStaticWebAssets` explicitly via `builder.UseStaticWebAssets()`. The first task that boots the factory (T009) verifies this; record the outcome here.

Razor views are compiled into the MVC assembly (Razor SDK), so no view files need copying. `_Layout.cshtml` references `~/lib/...` and `~/css/...` only as URLs; tests never fetch them.

## R5. Configuration injection

`Program.cs` reads `Api:BaseUrl`, `Cookie:SecurePolicy`, `Hosting:UseHttpsRedirection` and `DataProtection:KeysPath` from `builder.Configuration`. `UseSetting` is used (as `ApiFactory` does for `RateLimiting:*`, which the API also reads eagerly in `Program.cs`, and those tests pass). Environment variables (`Api__BaseUrl`) are the fallback if a value is read before the override is applied. `Cookie:SecurePolicy=SameAsRequest` is required because `TestServer` is plain HTTP and the default is `Always`, which would make the browser-like cookie handler drop the auth cookie. The antiforgery cookie uses the framework default (`SameAsRequest`) and needs no setting. `DataProtection:KeysPath` set to a temp directory also exercises the 020 branch of `Program.cs`; the directory is deleted on factory dispose.

## R6. `RefreshTokenCleanupWorker` testability

`ExecuteAsync` builds `new PeriodicTimer(TimeSpan.FromMinutes(minutes))` (minutes from `RefreshTokenCleanup:IntervalMinutes`, default 60, `<= 0` falls back to 60) and calls `RefreshTokenCleanupService.PurgeAsync(DateTime.UtcNow, ct)` inside a try/catch per tick. Without a seam the test would need to wait one real minute at the smallest configurable interval (integer minutes).

**Decision**: add an optional constructor parameter `TimeProvider? timeProvider = null` (stored as `timeProvider ?? TimeProvider.System`), use `new PeriodicTimer(interval, _time)` and `_time.GetUtcNow().UtcDateTime` for the purge timestamp. .NET 8+ `PeriodicTimer` supports a `TimeProvider`. With the default the behavior is identical. DI: an optional parameter with a default value is satisfied without registration, so `AddHostedService<RefreshTokenCleanupWorker>()` keeps working whether or not the host registers `TimeProvider`.
**Test**: `FakeTimeProvider` (package `Microsoft.Extensions.TimeProvider.Testing`): build a `ServiceCollection` with an InMemory `ApplicationDbContext` (shared database root so scopes see one store), `RefreshTokenCleanupService` scoped, config values; `StartAsync`; assert nothing purged; `Advance(60 min)`; poll (<= 5 s, 10 ms steps) until old tokens are gone and recent ones remain; advance again to prove it repeats; `StopAsync` completes within the timeout. Extra cases: interval `0` falls back to 60 (advance 59 min: nothing; +1 min: purged); a first tick that throws (service factory throws once) is logged and the second tick still purges; cancel before first tick returns without purge.
**Alternatives**: `IntervalSeconds` config key (adds public config surface, rejected); `InternalsVisibleTo` + internal `TickAsync` (changes structure more, rejected); no test, only start/stop (fallback, documented).
**Package risk**: if `Microsoft.Extensions.TimeProvider.Testing` cannot be used, write a ~30-line `ManualTimeProvider : TimeProvider` whose `CreateTimer` returns a manual `ITimer` fired by the test.

## R7. Token refresh behavior to pin (from `TokenRefreshHandler`)

- Participates only when `HttpContext` exists, `request.Headers.Authorization != null` and `User.Identity.IsAuthenticated`; otherwise passthrough (login, register, logout, public GETs).
- Buffers request content up front, so the retry can resend it; `CloneAsync` copies headers and body.
- `ExpiresSoon`: claim `access_token_expires_at` parsed with `RoundtripKind`; refresh when `<= UtcNow + 60 s`; on success the request's bearer is replaced with the new token.
- On 401, refresh once per `HttpContext` (`HttpContext.Items["refresh_tried"]`): a pre-emptive refresh in the same request counts, so a 401 afterwards does not refresh again and the original 401 is returned.
- Refresh failure (non-2xx, null body, `HttpRequestException`, timeout, JSON error) returns false and the original response (401) is returned, which the controllers turn into `SessionExpiredAsync`.
- A successful refresh calls `AuthSessionService.SignInAsync(http, name, login)`, which sets the response cookie and `http.User` for the rest of the request.
- Not covered by code and so not tested: concurrency between two requests of the same session (each has its own `HttpContext`; the API grace window from 020 handles it).

## R8. Which controllers handle API 401

| Controller | 401 handling | Test |
|---|---|---|
| Categories, Products (writes) | `result.Unauthorized` -> `SessionExpiredAsync` | yes |
| Inventory | catches `HttpRequestException` 401 on reads, `result.Unauthorized` on write | yes |
| Cart, Orders (incl. Manage, SetStatus, Cancel) | `result.Unauthorized` -> `SessionExpiredAsync` | yes |
| Users | not handled (`EnsureSuccessStatusCode` throws; `ChangeRole` ignores) | no, Known Gap |
| Account.Login | 401 means bad credentials | covered in Account |

`SessionExpiredAsync` signs out and redirects to `/Account/Login?returnUrl=<path+query>`; the test asserts the redirect and that the next request carrying the old cookie jar is anonymous (the cookie was cleared).

## R9. HTML helpers without a parser

Needed operations: `ExtractAntiforgeryToken(html)` (regex over `name="__RequestVerificationToken"` and `value="..."` in either attribute order), `Decode(html)`, `ContainsText(html, text)` after decoding, `HasElement(html, regexFragment)`, `FormAction(html, actionPath)` to confirm a form posts to the expected URL, and `Alert(html, "danger"|"success"|"warning")` to read the text of `div.alert-*`. Tag-helper output is stable (attribute order from `TagBuilder`), so regexes are not fragile. Revisit AngleSharp only if a test needs structural queries (nested tables) that regex makes unreadable.

## R10. Compose smoke design

- `docker compose up -d --build` builds both images (about 2-4 min on a runner) and waits for nothing beyond `depends_on` health (sql healthy) and `api` started, so readiness must be polled from outside.
- API readiness: `GET http://localhost:5028/health` (migrations run on startup with `Database__MigrateOnStartup=true`, so the first 200 implies the migrated database answered the EF health check). Expect JSON `status` `Healthy` and a `checks[].name == "database"` entry (shape from `HealthCheckResponseWriter`).
- MVC readiness: `GET http://localhost:5256/` follows the default route to `Categories/Index`, which calls the API, so a 200 with the layout marker `MVC C&amp;P` or `CategoryProduct.MVC` proves MVC -> API connectivity over the compose network (`Api__BaseUrl=http://api:8080/`).
- Polling: loop with `curl --max-time 5`, 5 s sleep, 60 tries (5 min) per endpoint; the loop is plain shell in the job, no extra action.
- Logs on failure: `docker compose ps -a` and `docker compose logs --no-color --tail=200`. Teardown always: `docker compose down -v --remove-orphans`, then `rm -f .env`.
- Secrets: generated with `openssl rand -hex`, masked with `::add-mask::` before being written; `.env` is git-ignored and docker-ignored.
- `timeout-minutes: 20` on the job, so a hang cannot burn runner time.

## R11. Parallelism of test classes

xunit runs classes in parallel, tests inside a class serially. Each class has its own `IClassFixture<MvcFactory>` (own host, own `FakeApi`), so no shared mutable state. `MvcTestBase` resets the fake and creates a new client in its constructor (xunit creates one instance per test). Host start is about 0.3-1 s; about 14 classes, so about 15 host starts, acceptable.

## R12. Other cheap API gaps chosen (FR-013)

From the 013/014/015 Known Gaps and `AccountController`/`UsersController` code: expired refresh token (`ExpiresAtUtc <= now` returns 401), user row deleted (`FindAsync` returns null returns 401), `{}` body (record `RefreshRequestDto(string RefreshToken)` is a non-nullable reference type under `Nullable` enable, so `[ApiController]` model validation is expected to return 400; assert whatever the real status is and name it in the test), refresh rate limit (needs `RefreshPermitLimit` as a virtual property in `ApiFactory`, same pattern as `LoginPermitLimit`; a subclass with limit 2 expects the third call to be 429), `GET api/users` without token (401). Not chosen: access-token revocation (not implemented), true concurrency (SQL Server tests, 020).
