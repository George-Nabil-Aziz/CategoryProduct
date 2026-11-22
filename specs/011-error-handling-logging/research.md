# Research: Error Handling and Logging

## Decision 1: `IExceptionHandler` + ProblemDetails for the API

**Decision**: Implement `GlobalExceptionHandler : IExceptionHandler`, register with
`AddExceptionHandler<...>()` and `AddProblemDetails()`, activate with `app.UseExceptionHandler()` and
`app.UseStatusCodePages()`. The handler logs the exception (method, path), builds a `ProblemDetails`
(`Title` "An unexpected error occurred.", `Status` 500, `traceId` extension from `Activity.Current?.Id`
or `TraceIdentifier`) and writes it as `application/problem+json`.

**Rationale**: Built-in .NET mechanism; one response shape; clients and logs can be correlated via
`traceId`; no exception text reaches the client.

**Alternatives considered**: Custom try/catch middleware (more code, same result); per-action
try/catch (duplication).

## Decision 2: Serilog configured from `appsettings.json`

**Decision**: `builder.Host.UseSerilog((ctx, services, cfg) => cfg.ReadFrom.Configuration(...)
.ReadFrom.Services(services).Enrich.FromLogContext())` in both apps, plus
`app.UseSerilogRequestLogging()`. Config: default `Information`, `Microsoft.AspNetCore` `Warning`,
sinks Console and File (`logs/categoryproduct-api-.log` / `logs/categoryproduct-mvc-.log`,
`rollingInterval` Day, `retainedFileCountLimit` 14). Package: `Serilog.AspNetCore` 10.0.0.

**Rationale**: Structured logs, sinks changed without code edits; the single meta-package includes the
console and file sinks used.

**Alternatives considered**: Built-in `ILogger` providers only (no file output); other sinks such as
Seq (new infrastructure, not requested).

## Decision 3: Git-ignore log files

**Decision**: Add `logs/` to `.gitignore`.

**Rationale**: Logs may contain request details and must not be committed (Principle IV spirit).

## Decision 4: MVC outage handling via `DelegatingHandler` + exception filter

**Decision**: `ApiErrorHandler` (registered transient, added with `AddHttpMessageHandler`) catches
`HttpRequestException` and `TaskCanceledException` (only when the caller's token is not cancelled),
logs "API unreachable", and throws `ApiUnavailableException`; any response with status >= 500 is
logged, disposed and also becomes `ApiUnavailableException`. `ApiUnavailableFilter : IExceptionFilter`
(added globally via `AddControllersWithViews(o => o.Filters.Add<ApiUnavailableFilter>())`) renders the
`ServiceUnavailable` view with status 503 and the exception message in `ViewData["Message"]`.

**Rationale**: One place covers every API call without touching each controller; 4xx stay with the
existing `ApiResult` handling.

**Alternatives considered**: try/catch in each controller action or in the client (repetitive);
global exception middleware (would also swallow unrelated exceptions).

## Decision 5: Test the 500 path through an `IStartupFilter`

**Decision**: `ThrowingFactory` adds a `/test/throw` endpoint that throws
`InvalidOperationException("boom secret")`; the test asserts the response never contains
"boom secret".

**Rationale**: Exercises the real pipeline without adding a production endpoint.
