# Implementation Plan: MVC Bug Fixes

**Branch**: `022-mvc-bugfixes` | **Date**: 2026-11-22 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/022-mvc-bugfixes/spec.md`

## Summary

Four small, independent fixes in `CategoryProduct.MVC`, each with tests in `CategoryProduct.MVC.Tests`: (1) a `HomeController` (`Index`, `Error`) and a user-friendly `Error.cshtml` so the non-Development exception handler yields a 500 page; (2) `GetUsersAsync` reports Unauthorized through `ApiResult<T>` and `UsersController` uses the `SessionExpiredAsync` pattern; (3) a Conflict branch in `ProductsController.AddErrors`; (4) `LogoutAsync` sends the bearer and marks the request so `TokenRefreshHandler` skips refresh and retry. No API change, no schema change, no migration, no new dependency.

## Technical Context

**Language/Version**: C# / .NET 10.

**Primary Dependencies**: none added. Production code uses only what the MVC project already references (ASP.NET Core MVC, cookie authentication, `System.Net.Http`). Tests use the packages already in `CategoryProduct.MVC.Tests` (xunit 2.9.3, `Microsoft.AspNetCore.Mvc.Testing` 10.0.10).

**Storage**: none (the MVC app has no database; the API is faked in tests).

**Testing**: xunit; `WebApplicationFactory<Program>` via `MvcFactory`/`MvcTestBase`; `FakeApi` scripting; handler unit tests with `StubHandler`.

**Target Platform**: local `dotnet test`; existing GitHub Actions jobs unchanged (`build-test` runs the new tests because the project is in `CategoryProduct.slnx`).

**Project Type**: existing web API + MVC; no new project.

**Constraints**: `dotnet test CategoryProduct.slnx` stays runnable without Docker, SQL Server or network; `Program.cs` unchanged; behavior of requests that do not use the new option stays identical; no secret committed.

**Scale/Scope**: 1 new controller, 1 view edited, 2 template views removed, 4 existing MVC files edited (`CategoryProductApiClient.cs`, `UsersController.cs`, `ProductsController.cs`, `TokenRefreshHandler.cs`), `MvcFactory.cs` edited, 1 new test support class, 1 new test class, 5 existing test classes extended, 1 existing test updated, 1 spec edited (021 Known Gaps).

## Constitution Check

Checked against `.specify/memory/constitution.md` v1.0.0.

| Principle | Check | Status |
|---|---|---|
| I. Consistent REST API Design | No API route, verb, status code or response shape changes; the MVC only starts to read the 401 and 409 statuses the API already returns and sends an extra `Authorization` header on a call the API ignores. | PASS |
| II. Schema Changes Only Through EF Core Migrations | No model change, no migration (`data-model.md`). | PASS |
| III. Simplicity & YAGNI | Smallest changes that satisfy each defect: a controller with two actions, one extra `if` in `AddErrors`, reuse of the existing private `GetAsync<T>` and `ApiResult` pattern, one `HttpRequestOptionsKey<bool>` instead of a new handler or client. Errors are produced in tests through the existing `FakeApi.Throw`, not a new test-only endpoint. | PASS |
| IV. Secrets & Credential Hygiene | No secret added; test tokens are opaque literals (`tok-1`, `ref-1`). The error page shows no exception text. Logout still transmits the refresh token only to the API logout endpoint, as before; the bearer is the access token already held in the cookie. The seeded `testuser` is out of scope and untouched. | PASS |
| V. API/MVC Separation | MVC still talks to the API only over HTTP; `CategoryProduct.MVC.Tests` references only `CategoryProduct.MVC`. No API type is shared. | PASS |

Workflow gate ("controller behavior changes must be verified"): satisfied by automated tests in `CategoryProduct.MVC.Tests`; no manual Swagger step is needed because no API behavior changes.

## Project Structure

### Documentation

```text
specs/022-mvc-bugfixes/
  spec.md  plan.md  research.md  data-model.md  quickstart.md  tasks.md
  contracts/{error-page.md, users-session-expiry.md, products-conflict.md, logout-bearer.md}
  checklists/requirements.md
```

### Source changes

```text
CategoryProduct.MVC/Controllers/HomeController.cs          NEW   Index (redirect), Error (500 + ErrorViewModel)
CategoryProduct.MVC/Views/Shared/Error.cshtml              EDIT  friendly text + link to Categories, no dev text
CategoryProduct.MVC/Views/Home/Index.cshtml                DELETE (unreferenced template leftover)
CategoryProduct.MVC/Views/Home/Privacy.cshtml              DELETE (unreferenced template leftover)
CategoryProduct.MVC/Services/CategoryProductApiClient.cs   EDIT  GetUsersAsync -> ApiResult<PagedResult<UserItem>>; LogoutAsync bearer + skip option
CategoryProduct.MVC/Services/TokenRefreshHandler.cs        EDIT  public SkipRefreshOption key; early pass-through
CategoryProduct.MVC/Controllers/UsersController.cs         EDIT  Unauthorized/Forbidden handling, SessionExpiredAsync
CategoryProduct.MVC/Controllers/ProductsController.cs      EDIT  Conflict branch in AddErrors
CategoryProduct.MVC.Tests/Support/MvcFactory.cs            EDIT  unseal, protected virtual EnvironmentName
CategoryProduct.MVC.Tests/Support/ProductionMvcFactory.cs  NEW
CategoryProduct.MVC.Tests/ErrorPageTests.cs                NEW
CategoryProduct.MVC.Tests/UsersTests.cs                    EDIT  401/403 tests
CategoryProduct.MVC.Tests/SessionExpiryTests.cs            EDIT  Users rows (if the Theory fits, see research R5)
CategoryProduct.MVC.Tests/ProductsTests.cs                 EDIT  409 tests assert the detail
CategoryProduct.MVC.Tests/ProductImageTests.cs             EDIT  upload 409 asserts detail; new RemoveImage 409
CategoryProduct.MVC.Tests/AccountTests.cs                  EDIT  logout asserts bearer; new logout tests
CategoryProduct.MVC.Tests/TokenRefreshHandlerTests.cs      EDIT  skip-option tests
CategoryProduct.MVC.Tests/ApiUnavailableTests.cs           EDIT  logout 401/503 tests (if not in AccountTests)
specs/021-test-coverage/spec.md                            EDIT  Known Gaps items 1-4, two annotations
specs/021-test-coverage/contracts/mvc-test-matrix.md       EDIT  logout rows note the bearer
```

**Structure Decision**: no new project and no new service; each fix touches one controller/service pair plus tests, so the four stories are independent and can be implemented in any order. The only shared production file is `CategoryProductApiClient.cs` (stories 2 and 4), so those two edits are sequential.

## Design

1. **Error page**. `HomeController` has `Index() => RedirectToAction("Index", "Categories")` and `Error()` with `[AllowAnonymous]` and `[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]`, no HTTP verb attribute. It sets `Response.StatusCode = 500` and returns `View("Error", new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier })`; the Razor lookup finds `Views/Shared/Error.cshtml` because `Views/Home` has no `Error` view. `Error.cshtml` keeps `ViewData["Title"]`, shows a heading and one sentence, the Request ID when `Model.ShowRequestId`, and `<a asp-controller="Categories" asp-action="Index">`. `Program.cs` is unchanged. Details in `contracts/error-page.md`.
2. **Production in tests**. `MvcFactory` becomes non-sealed, `ConfigureWebHost` calls `builder.UseEnvironment(EnvironmentName)` with `protected virtual string EnvironmentName => "Development"`. `ProductionMvcFactory : MvcFactory` overrides it. `ErrorPageTests : MvcTestBase, IClassFixture<ProductionMvcFactory>` and passes the factory to the base constructor. The failure is produced with `Fake.Throw(HttpMethod.Get, "api/category", new InvalidOperationException("secret-marker-..."))`.
3. **Users 401**. `GetUsersAsync` becomes `GetAsync<PagedResult<UserItem>>($"api/users?page={page}&pageSize={pageSize}&search={Uri.EscapeDataString(search ?? "")}")` (URL unchanged). `UsersController.Index`: Unauthorized then `SessionExpiredAsync()`; Forbidden then `RedirectToAction("AccessDenied", "Account")`; `!Success` then `throw new HttpRequestException(...)`; otherwise the same ViewBag/View as today (from `result.Value!`). `ChangeRole`: `if (result.Unauthorized) return await SessionExpiredAsync(Url.Action(nameof(Index), new { page, search }))` before the existing message chain. `SessionExpiredAsync(string? returnUrl = null)` signs out and redirects to `Account/Login` with `returnUrl ?? Request.Path + Request.QueryString`. Details in `contracts/users-session-expiry.md`.
4. **Products 409**. One line in `AddErrors`, identical to Categories, placed between the Forbidden and `Errors` loop code. All five call sites already call `AddErrors` for non-success results. Details in `contracts/products-conflict.md`.
5. **Logout**. `TokenRefreshHandler.SkipRefreshOption = new HttpRequestOptionsKey<bool>("cp.skip-refresh")` (public static readonly) and, as the first statement of `SendAsync`, `if (request.Options.TryGetValue(SkipRefreshOption, out var skip) && skip) return await base.SendAsync(request, ct);`. `LogoutAsync` builds an `HttpRequestMessage` (POST `api/account/logout`, `JsonContent.Create(new { refreshToken })`), calls `ApplyBearerToken(request)`, sets `request.Options.Set(TokenRefreshHandler.SkipRefreshOption, true)`, sends it inside the existing try/catch. Details and the refresh/rotation analysis in `contracts/logout-bearer.md` and `research.md` R3-R4.
6. **Tests first**: for each fix write the failing test, then the change (tasks.md orders them that way).

## Complexity Tracking

No constitution violations. Two decisions worth a reviewer's eye, both recorded in `research.md`: the `HttpRequestOptionsKey` marker on the logout request (R3) and the removal of the two dead `Views/Home` template views (R2).
