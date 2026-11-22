# Implementation Plan: Test Coverage

**Branch**: `021-test-coverage` | **Date**: 2026-11-22 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/021-test-coverage/spec.md`

## Summary

Three independent deliverables closing test-related Known Gaps of 013-020: (1) a new `CategoryProduct.MVC.Tests` project that runs the real MVC app in memory against a scriptable fake API (added to `CategoryProduct.slnx`), (2) a `compose-smoke` CI job that runs the real compose stack with throwaway secrets, (3) a few cheap API tests, including `RefreshTokenCleanupWorker` via an optional `TimeProvider`. No schema change, no behavior change, no new runtime dependency.

## Technical Context

**Language/Version**: C# / .NET 10.

**Primary Dependencies**: test-only. `CategoryProduct.MVC.Tests` uses the package versions of `CategoryProduct.API.Tests`: `coverlet.collector` 6.0.4, `Microsoft.AspNetCore.Mvc.Testing` 10.0.10, `Microsoft.NET.Test.Sdk` 17.14.1, `xunit` 2.9.3, `xunit.runner.visualstudio` 3.1.4 (no `Microsoft.EntityFrameworkCore.InMemory`). `CategoryProduct.API.Tests` may gain `Microsoft.Extensions.TimeProvider.Testing` (10.x, resolved with `dotnet add package`; verify the version is at least two weeks old and restores).

**Storage**: none (MVC has no database; the API is faked; API worker test uses EF InMemory like the other API tests).

**Testing**: xunit; `WebApplicationFactory<Program>` for MVC; handler unit tests with fake inner handlers and `DefaultHttpContext`.

**Target Platform**: local `dotnet test`; GitHub Actions `ubuntu-latest` with Docker Compose v2 for the smoke job.

**Project Type**: existing web API + MVC; one new test project; one new CI job.

**Constraints**: `dotnet test CategoryProduct.slnx` must stay runnable with no Docker, SQL Server or network; existing CI jobs unchanged; `CategoryProduct.API.SqlServerTests` stays outside the solution; no secret committed.

**Scale/Scope**: about 20 new files in `CategoryProduct.MVC.Tests` (about 6 infrastructure, about 14 test classes, at least 120 tests), 1 MVC line, 1 workflow job (about 60 lines), 1 optional API constructor parameter, 2 new API test files, 2 small test-support edits, 7 Known Gaps edits.

## Constitution Check

| Principle | Check | Status |
|---|---|---|
| I. Consistent REST API Design | No route, verb or response shape changes. `FakeApi` mimics the existing API contract (status codes and JSON shapes listed in `contracts/fake-api.md`, taken from `CategoryProductApiClient`). | PASS |
| II. Schema Changes Only Through EF Core Migrations | No model change, no migration. | PASS |
| III. Simplicity & YAGNI | One fake handler instead of a mocking library; regex HTML helpers instead of AngleSharp; optional `TimeProvider` parameter is the smallest change that makes the worker testable (no interface, no options class). Per-class factory, no custom test framework. | PASS |
| IV. Secrets & Credential Hygiene | Compose `.env` is generated at runtime from `openssl rand`, masked, never committed (`.env` is already in `.gitignore` and `.dockerignore`). Test JWT-like strings are opaque literals ("access-1"). The existing `SQLSERVER_CI_SA_PASSWORD` literal is untouched. | PASS |
| V. API/MVC Separation | `CategoryProduct.MVC.Tests` references only `CategoryProduct.MVC`; API tests keep referencing only the API. The MVC is tested through HTTP against a fake, never against API types. | PASS |

## Project Structure

### Documentation

```text
specs/021-test-coverage/
  spec.md  plan.md  research.md  data-model.md  quickstart.md  tasks.md
  contracts/{fake-api.md, mvc-test-matrix.md, ci-compose-smoke.md}
  checklists/requirements.md
```

### Source changes

```text
CategoryProduct.slnx                                   + Project CategoryProduct.MVC.Tests
CategoryProduct.MVC/Program.cs                         + public partial class Program { }   (last line)
CategoryProduct.MVC.Tests/                             NEW
  CategoryProduct.MVC.Tests.csproj
  Support/FakeApi.cs  MvcFactory.cs  MvcTestBase.cs  HtmlHelpers.cs  ApiJson.cs
  Support/RecordingAuthenticationService.cs  Support/StubHttpClientFactory.cs  Support/StubHandler.cs
  InfrastructureTests.cs
  AccountTests.cs  AnonymousAccessTests.cs
  CategoriesTests.cs  ProductsTests.cs  ProductImageTests.cs  UsersTests.cs
  InventoryTests.cs  CartTests.cs  OrdersTests.cs  ManageOrdersTests.cs
  SessionExpiryTests.cs  ApiUnavailableTests.cs
  TokenRefreshHandlerTests.cs  ApiErrorHandlerTests.cs  AuthSessionServiceTests.cs
.github/workflows/ci.yml                               + job compose-smoke (existing jobs untouched)
CategoryProduct.API/Services/RefreshTokenCleanupWorker.cs   + optional TimeProvider ctor parameter
CategoryProduct.API.Tests/RefreshTokenCleanupWorkerTests.cs NEW
CategoryProduct.API.Tests/RefreshEdgeCaseTests.cs           NEW
CategoryProduct.API.Tests/ApiFactory.cs                     + protected virtual int RefreshPermitLimit => 1000
CategoryProduct.API.Tests/CategoryProduct.API.Tests.csproj  + Microsoft.Extensions.TimeProvider.Testing
specs/013,014,016,017,018,019,020/spec.md              Known Gaps pointers
```

**Structure Decision**: tests live in a new sibling project so the shipped MVC project gains no test dependencies; the `Support/` folder holds only infrastructure, so parallel agents never edit the same file (ownership table in `tasks.md`).

## Design

1. **Host**: `MvcFactory : WebApplicationFactory<Program>` (MVC `Program`). `ConfigureWebHost`: `UseSetting` for the keys in FR-003 (the API factory already proves `UseSetting` is visible to eager `builder.Configuration` reads, e.g. rate limits), then `ConfigureServices(s => s.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => FakeApi)))`. `ConfigureHttpClientDefaults` applies to the typed client and to the named `api-refresh` client, and the `AddHttpMessageHandler` chain (`ApiErrorHandler` outer, `TokenRefreshHandler` inner, then the primary handler) is kept. Serilog's file sink is redirected with `UseSetting("Serilog:WriteTo:1:Args:path", <temp>/mvc-.log)` so tests do not write to `logs/`.
2. **Client**: `CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true, BaseAddress = http://localhost })`; a fresh client per test is a fresh cookie jar.
3. **Sign-in**: `MvcTestBase.SignInAsync(client, role)` scripts `POST api/account/login` (if not already scripted), GETs `/Account/Login`, extracts the token, POSTs the form, asserts 302 and the auth cookie. Variants: `User`, `Admin`, optional `expiresInSeconds` (30 to force a pre-emptive refresh) and custom token strings.
4. **Assertions on requests**: `fake.Requests` (ordered `RecordedRequest` list); helpers `fake.Single(method, path)`, `fake.Calls(method, path)`.
5. **Handler unit tests**: `TokenRefreshHandler` is built by hand: `IHttpContextAccessor` stub with a `DefaultHttpContext` whose `User` has the `access_token`, `refresh_token`, `access_token_expires_at` claims and whose `RequestServices` provides `RecordingAuthenticationService` (so `AuthSessionService.SignInAsync` can run without a cookie middleware), a stub `IHttpClientFactory` that returns an `HttpClient` over a `StubHandler` for `api-refresh`, and `InnerHandler = StubHandler`. `ApiErrorHandler` the same with a throwing/statused inner handler and a `NullLogger`.
6. **Compose job**: see `contracts/ci-compose-smoke.md`.
7. **Worker**: see `research.md` R6.

## Complexity Tracking

No constitution violations. One justified production edit (`public partial class Program { }`), and one optional (`TimeProvider` parameter) recorded in `research.md`.
