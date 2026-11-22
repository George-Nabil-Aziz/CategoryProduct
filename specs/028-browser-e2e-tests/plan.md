# Implementation Plan: Browser end-to-end tests (Playwright)

**Branch**: `028-browser-e2e-tests` | **Date**: 2026-11-22 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/028-browser-e2e-tests/spec.md`

## Summary

(1) A new xunit project `CategoryProduct.E2ETests` using `Microsoft.Playwright` (Chromium), outside `CategoryProduct.slnx`, with an `E2EFactAttribute` that skips when `E2E_BASE_URL` is unset, an assembly fixture that preflights the stack, a base class with isolated contexts and failure-only screenshot/trace capture, and helper steps. (2) 16 focused tests in five classes covering auth and client validation, catalog with image upload, the order/payment/refund flow, the dashboard and the error page. (3) An additive `docker-compose.e2e.yml` that raises the API rate limits for the test stack. (4) A new CI job `e2e` that builds the stack with random secrets, seeds the bootstrap Admin, installs Chromium, runs the tests and uploads artifacts on failure. No production code changes.

## Technical Context

**Language/Version**: C# / .NET 10 (test project only).

**Primary Dependencies**: `Microsoft.Playwright` (exact version pinned at T003), `xunit` 2.9.3, `xunit.runner.visualstudio` 3.1.4, `Microsoft.NET.Test.Sdk` 17.14.1 (same as the sibling test projects, verified in their `.csproj`). No `Microsoft.Playwright.Xunit`, no `Microsoft.AspNetCore.Mvc.Testing`, no project references.

**Storage**: none (the suite uses the stack's SQL Server through the UI only).

**Testing**: real Chromium against the compose stack (`sql`, `api`, `mvc`). The existing offline suites are unchanged.

**Target Platform**: developer machines (Windows/Linux/macOS with Docker) and GitHub Actions `ubuntu-latest`.

**Project Type**: new test project in the existing solution folder layout (`CategoryProduct.E2ETests/`), not part of the `.slnx`.

**Constraints**: constitution I-V; no production change; `dotnet test CategoryProduct.slnx` offline and unchanged; no secrets committed or uploaded; no fixed sleeps, no retries; existing CI jobs untouched.

**Scale/Scope**: about 12 new files in `CategoryProduct.E2ETests` (csproj, `AssemblyInfo.cs`, `E2EFactAttribute.cs`, `E2EEnvironment.cs`, `E2EFixture.cs`, `BrowserTestBase.cs`, `Steps.cs`, `E2EData.cs`, `TestImage.cs`, 5 test classes), `docker-compose.e2e.yml`, one CI job, `.gitignore` entry for `test-results/`.

## Constitution Check

Checked against `.specify/memory/constitution.md` v1.0.0.

| Principle | Check | Status |
|---|---|---|
| I. Consistent REST API Design | No API route, verb or response is added or changed. | PASS (not applicable) |
| II. Schema Changes Only Through EF Core Migrations | No model or schema change; the suite only uses the UI. | PASS (not applicable) |
| III. Simplicity & YAGNI | Plain xunit + Playwright library; one base class, one steps helper; no page-object framework, no custom DSL, no parallel runner, no retry plugin, no test-data cleanup service. A compose override file is added only because the rate limits cannot otherwise be raised without touching the base compose. | PASS |
| IV. Secrets & Credential Hygiene (NON-NEGOTIABLE) | No credentials in code: admin and base URL come from environment; CI generates random values per run and masks them; `.env` is created at runtime, never committed, never uploaded (artifact paths are limited to `test-results/`), and deleted in teardown; generated users get random passwords that are never logged; traces may contain form input, so they are uploaded only on failure, only from throwaway users and a throwaway stack, and the artifact retention is short (7 days). | PASS |
| V. API/MVC Separation | The tests reference neither project; they drive the deployed MVC through a browser, and MVC talks to the API over HTTP as in production. | PASS |

Workflow gates: no model change; "no secret values in tracked configuration": T030 grep; "controller behavior verified": the point of this feature.

## Project Structure

### Documentation

```text
specs/028-browser-e2e-tests/
  spec.md  plan.md  research.md  data-model.md  quickstart.md  tasks.md
  contracts/{e2e-environment.md, e2e-scenarios.md, e2e-ci.md}
  checklists/requirements.md
```

### Source changes

```text
CategoryProduct.E2ETests/                       (NEW, NOT in CategoryProduct.slnx)
  CategoryProduct.E2ETests.csproj
  AssemblyInfo.cs               DisableTestParallelization
  E2EEnvironment.cs             reads env vars, validation, ResultsDir
  E2EFactAttribute.cs           Skip when E2E_BASE_URL empty
  E2EFixture.cs                 assembly/collection fixture: preflight GET /, playwright + chromium
  BrowserTestBase.cs            context per test, tracing, RunAsync wrapper, failure capture
  Steps.cs                      Register/Login/Logout/CreateCategory/CreateProduct/UploadImage/AddToCart/Checkout/Pay/Cancel
  E2EData.cs                    unique names, random password
  TestImage.cs                  in-memory 1x1 PNG
  AuthTests.cs  CatalogTests.cs  OrderFlowTests.cs  DashboardTests.cs  ErrorPageTests.cs
docker-compose.e2e.yml                          (NEW) rate limit override for the api service
.github/workflows/ci.yml                        (EDIT) new job `e2e` only
.gitignore                                      (EDIT) test-results/ 
```

Not changed: `CategoryProduct.slnx`, `docker-compose.yml`, every `CategoryProduct.API*`/`MVC*` project, existing CI jobs.

## Design

- **Opt-in and skip**: `E2EFactAttribute : FactAttribute` reads `E2E_BASE_URL` in its constructor and sets `Skip` when empty. No fixture runs for skipped tests, so `dotnet test CategoryProduct.E2ETests` is instant and needs no browser. See `contracts/e2e-environment.md`.
- **Fixture**: a `ICollectionFixture` shared by all classes (`[Collection("e2e")]`) creates `IPlaywright` and the Chromium browser once, runs the preflight, and exposes `BaseUrl`, admin credentials and the browser. Disposed at the end.
- **Isolation**: `BrowserTestBase` makes a `BrowserContext` per test (`Locale=en-US`, `TimezoneId=UTC`, `ViewportSize 1280x900`, `BaseURL`), default action/assertion timeout 15 s, navigation timeout 30 s, tracing on. `RunAsync(Func<IPage, Task>)` runs the body; on exception it writes screenshot, trace and a diagnostics text file, then rethrows; on success it stops tracing without saving.
- **Locators**: role/label/text first (`GetByRole`, `GetByLabel`), the ids and ARIA labels that exist (`#forgot-confirmation`, `#kpi-orders`, `svg[aria-label=...]`). Assertions use `Assertions.Expect(...)` (auto-retry), never manual polling.
- **Client validation proof**: a `page.Request` handler counts POSTs to the form's path; the test asserts the count is zero after the click and the message locator is visible (which proves jQuery Validate ran, since without JS the form would post).
- **Real cookies**: `context.CookiesAsync()` inspected after login/logout.
- **Image upload**: `Locator("input[type=file]").SetInputFilesAsync(new FilePayload{ Name="e2e.png", MimeType="image/png", Buffer=TestImage.Png })`, then `img.EvaluateAsync<int>("e => e.naturalWidth")` inside `Expect`-style polling via `Assertions`/`WaitForFunctionAsync` (Playwright's own wait, not a sleep).
- **Rate limits**: `docker-compose.e2e.yml` adds `RateLimiting__*` env to `api`; `Steps` throws a clear message when a page shows the 429 text.
- **CI**: new job `e2e` per `contracts/e2e-ci.md`.

## Complexity Tracking

| Item | Why needed | Simpler alternative rejected because |
|---|---|---|
| `docker-compose.e2e.yml` override | login/register limits are 5/min per IP and the API sees one IP (the MVC container); the suite needs dozens of logins | editing the base compose would loosen limits for everyone; sleeping between logins would make the suite slow and flaky; a unique `X-Forwarded-For` is not trusted by the API |
| New CI job instead of extending `compose-smoke` | different failure meaning, timeout, artifacts and needs (dotnet SDK + browser) | extending would make a smoke failure and an E2E failure indistinguishable and push the smoke job past its 20 minute budget (research R5) |
| Custom fact attribute + collection fixture | offline skip and one browser per run | `Microsoft.Playwright.Xunit` ties to its own base class and has no env-based skip; per-test browser launch triples the run time |

## Phase order

Setup and re-read of the landed 026/027 views -> project, environment, skip attribute, fixture, base class (US1) -> helpers and PNG -> Auth tests (US2) -> Catalog tests (US3) -> Order flow tests (US4) -> Dashboard and error page (US5) -> compose override and CI job (US6) -> review/hygiene -> final verification and Known Gaps (US7). Tests are written against a running local stack where Docker exists; CI is the verification of record because the dev machine has no Docker.
