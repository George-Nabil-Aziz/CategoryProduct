# Implementation Plan: Error Handling and Logging

**Branch**: `011-error-handling-logging` | **Date**: 2026-11-03 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/011-error-handling-logging/spec.md`

**Status**: Implemented (`f47b61e`)

## Summary

API: `AddProblemDetails` + an `IExceptionHandler` (`GlobalExceptionHandler`) + `UseExceptionHandler`,
`UseStatusCodePages`, Serilog host logging and request logging. MVC: Serilog, plus a
`DelegatingHandler` (`ApiErrorHandler`) on the typed API client that turns connection failures and 5xx
into `ApiUnavailableException`, and an MVC exception filter (`ApiUnavailableFilter`) that renders a
`ServiceUnavailable` view with 503.

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: ASP.NET Core, new `Serilog.AspNetCore` 10.0.0 in both
`CategoryProduct.API.csproj` and `CategoryProduct.MVC.csproj`

**Storage**: N/A (log files under `logs/`); no database change

**Testing**: xUnit in `CategoryProduct.API.Tests` - `ErrorHandlingTests` with a `ThrowingFactory`
(derives from `ApiFactory`, adds a `/test/throw` endpoint through an `IStartupFilter`); no MVC tests

**Target Platform**: ASP.NET Core web hosts

**Project Type**: Web service + MVC client

**Performance Goals**: N/A

**Constraints**: Do not leak exception details; log files rolled daily, 14 retained; `logs/` ignored by
git

**Scale/Scope**: 1 new API class, 1 new MVC file (2 classes), 1 new view, 2 `Program.cs` edits,
2 `appsettings.json` edits, 2 package references, 1 test file

## Constitution Check

| Principle | Check | Status |
|---|---|---|
| I. Consistent REST API Design | Errors use standard ProblemDetails (`application/problem+json`); no route or verb changes. | PASS |
| II. Schema Changes Only Through EF Core Migrations | No schema change. | PASS |
| III. Simplicity & YAGNI | Framework-provided `IExceptionHandler`/ProblemDetails/Serilog; no custom middleware, no repository layers. A DelegatingHandler + filter are the smallest hooks for one concrete need (MVC outage page). | PASS |
| IV. Secrets & Credential Hygiene | Handler returns no exception text (tested) and logs only method and path; no credentials or secrets are added to config or logs by this feature. | PASS |
| V. API/MVC Separation of Concerns | MVC handles API failures purely at the HTTP boundary (`HttpClient` handler); no shared types or references. | PASS |

## Project Structure

### Documentation (this feature)

```text
specs/011-error-handling-logging/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── spec.md
├── contracts/
│   ├── problem-details.md
│   └── mvc-service-unavailable.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
.gitignore                                          # + logs/
CategoryProduct.API/
├── CategoryProduct.API.csproj                      # + Serilog.AspNetCore 10.0.0
├── Program.cs                                      # UseSerilog, AddProblemDetails, AddExceptionHandler, UseExceptionHandler, UseStatusCodePages, UseSerilogRequestLogging
├── appsettings.json                                # Serilog section (console + file)
└── Infrastructure/GlobalExceptionHandler.cs        # new
CategoryProduct.API.Tests/
└── ErrorHandlingTests.cs                           # new: ThrowingFactory, UnhandledException_Returns500ProblemJson
CategoryProduct.MVC/
├── CategoryProduct.MVC.csproj                      # + Serilog.AspNetCore 10.0.0
├── Program.cs                                      # UseSerilog, ApiErrorHandler on typed client, ApiUnavailableFilter, UseSerilogRequestLogging
├── appsettings.json                                # Serilog section (console + file)
├── Services/ApiErrorHandler.cs                     # new: ApiUnavailableException, ApiErrorHandler, ApiUnavailableFilter
└── Views/Shared/ServiceUnavailable.cshtml          # new
```

**Structure Decision**: One new folder (`CategoryProduct.API/Infrastructure/`) and a few new files;
everything else edits existing files.

## Complexity Tracking

*No entries - no constitution violations.*
