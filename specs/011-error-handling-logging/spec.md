# Feature Specification: Error Handling and Logging

**Feature Branch**: `011-error-handling-logging` (documented retroactively; implemented directly on `master`)

**Created**: 2026-11-03

**Status**: Implemented (commit `f47b61e`)

**Input**: Make failures predictable and diagnosable: the API answers unexpected errors with a
standard ProblemDetails response that leaks nothing, both applications write structured logs (console
and rolling files) with Serilog, and the MVC app shows a friendly page instead of a raw exception when
the API is down or returns a server error.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Safe, standard API error responses (Priority: P1)

An API consumer that triggers an unexpected server error receives a consistent
`application/problem+json` response that identifies the failure (via a trace id) without exposing
internal details.

**Why this priority**: Prevents information leakage and gives clients one error shape to handle.

**Independent Test**: Cause an unhandled exception and check status `500`, content type
`application/problem+json`, title "An unexpected error occurred.", a `traceId` property, and that the
exception message is absent. Covered by `UnhandledException_Returns500ProblemJson` in
`CategoryProduct.API.Tests/ErrorHandlingTests.cs`.

**Acceptance Scenarios**:

1. **Given** an endpoint throws an exception, **When** it is called, **Then** the API responds `500`
   with ProblemDetails (`title` "An unexpected error occurred.", `status` 500, `traceId`).
2. **Given** that failure, **When** the response body is inspected, **Then** it does not contain the
   exception message.
3. **Given** that failure, **When** the logs are inspected, **Then** the exception is logged at error
   level with the HTTP method and path.

---

### User Story 2 - Structured logging in API and MVC (Priority: P1)

Operators get request and error logs on the console and in daily rolling files, configurable through
`appsettings.json`.

**Why this priority**: Without logs, failures such as US1's cannot be diagnosed.

**Independent Test**: Run each app, send a request, and check the console and the `logs/` folder for
`categoryproduct-api-<date>.log` / `categoryproduct-mvc-<date>.log` (manual; no automated test).

**Acceptance Scenarios**:

1. **Given** the API or MVC app is running, **When** a request is handled, **Then** a request log line
   is written to the console and to the day's log file.
2. **Given** the `Serilog` configuration section, **When** levels or sinks are changed there, **Then**
   logging follows it (read through `ReadFrom.Configuration`).
3. **Given** log files accumulate, **When** more than 14 daily files exist, **Then** older ones are
   removed (`retainedFileCountLimit` 14).

---

### User Story 3 - Friendly page when the API is unavailable (Priority: P1)

A web user whose request cannot be served because the API is unreachable, times out or answers with a
5xx sees a clear "Service unavailable" page with a Retry button, not a stack trace.

**Why this priority**: The MVC app depends entirely on the API; outages must degrade gracefully.

**Independent Test**: Stop the API, open an MVC list page; expect HTTP `503` and the Service
unavailable page (manual; no automated test).

**Acceptance Scenarios**:

1. **Given** the API is stopped, **When** an MVC page needs it, **Then** the "Service unavailable"
   view is returned with status `503` and the message "The service is unavailable. Please try again
   later."
2. **Given** the API returns a status of 500 or above, **When** the MVC client receives it, **Then**
   it is logged and the same page is shown.
3. **Given** the page is shown, **When** the user clicks Retry, **Then** the browser reloads the page.
4. **Given** the API returns a 4xx (401, 404, 409, 400...), **When** the MVC client receives it,
   **Then** the existing per-status handling applies unchanged.

---

### Edge Cases

- A request cancelled by the caller (`CancellationToken` cancelled) is not treated as "API
  unavailable"; only connection failures (`HttpRequestException`) and timeouts
  (`TaskCanceledException` while the caller's token is not cancelled) are.
- Log files live under `logs/` relative to the app's working directory, and the folder is git-ignored.
- Non-development MVC hosts still use `/Home/Error` for other unhandled exceptions (existing
  behaviour, not changed).
- The error handler only knows about exceptions; it does not alter `4xx` handling or validation
  problems.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The API MUST convert unhandled exceptions to `500` `application/problem+json` with
  `title` "An unexpected error occurred.", `status` 500 and a `traceId` extension (current activity id
  or `HttpContext.TraceIdentifier`).
- **FR-002**: The response MUST NOT include the exception message or stack trace.
- **FR-003**: The handler MUST log the exception with method and path before responding.
- **FR-004**: The API MUST register ProblemDetails services, the exception handler and status-code
  pages, and log HTTP requests with Serilog request logging.
- **FR-005**: Both API and MVC MUST configure Serilog from the `Serilog` configuration section, with
  console and file sinks (`logs/categoryproduct-api-.log` and `logs/categoryproduct-mvc-.log`, daily
  rolling, 14 files retained), default level `Information`, `Microsoft.AspNetCore` overridden to
  `Warning`.
- **FR-006**: The MVC HTTP client MUST convert connection failures, timeouts and 5xx responses into an
  `ApiUnavailableException` after logging them.
- **FR-007**: An MVC exception filter MUST render the `ServiceUnavailable` view with status `503` and
  the exception's message for `ApiUnavailableException`.
- **FR-008**: The `logs/` folder MUST be git-ignored.
- **FR-009**: Passwords and secrets MUST NOT be written to logs by this feature (the handler logs only
  method and path).

### Key Entities

- **ProblemDetails (500)**: `title`, `status`, `traceId` extension.
- **ApiUnavailableException**: MVC-internal exception with the friendly message.
- **Log file**: rolling daily text files under `logs/`.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: `UnhandledException_Returns500ProblemJson` passes (status, content type, title,
  `traceId`, no leaked message).
- **SC-002**: Both applications start with Serilog configured and write to console and `logs/`.
- **SC-003**: With the API stopped, MVC pages show the Service unavailable page with `503` instead of
  an exception page (manual check).

## Assumptions

- File logging is acceptable for local/dev operation; production sinks are a deployment decision.
- The friendly message text is a fixed constant (`ApiUnavailableException.FriendlyMessage`).
- Log retention of 14 days is sufficient.

## Known Gaps

- The MVC unavailable flow and Serilog file output have no automated tests and were not exercised in
  a browser or by running the apps while documenting.
- Only the API's 500 path is tested; validation and 404 responses are not covered by this feature's
  tests.
- Registering `UseStatusCodePages` with ProblemDetails is framework behaviour that this feature relies
  on for bodyless 4xx/5xx API responses; no test asserts it.
- No correlation id is propagated from MVC to the API beyond the framework defaults.
