# Feature Specification: Health Checks

**Feature Branch**: `012-health-checks`

**Created**: 2026-11-22 (retroactive; documents delivered work)

**Status**: Implemented (commit `68c11e4`, 2026-11-03)

**Input**: "Add a `/health` endpoint to CategoryProduct.API that verifies the database is reachable."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Probe service and database health (Priority: P1)

An operator, load balancer, or monitoring tool calls `GET /health` without credentials and learns
whether the API is up and whether it can reach its database.

**Why this priority**: It is the whole feature; no other story exists without it.

**Independent Test**: Call `GET /health` anonymously against a running API with a working database
and confirm `200` with `status: "Healthy"` and a `database` entry in `checks`.

**Acceptance Scenarios**:

1. **Given** the API is running and the database is reachable, **When** `GET /health` is called with
   no credential, **Then** the response is `200 OK`, `application/json`, with top-level `status`
   `"Healthy"` and a `checks` array containing an entry named `database`.
2. **Given** the database cannot be reached, **When** `GET /health` is called, **Then** the report
   status is not `Healthy` (the framework default maps `Unhealthy` to `503`; this path is not
   covered by an automated test).

---

### User Story 2 - Health probes do not flood the logs (Priority: P2)

Health probes are typically polled every few seconds. Successful probes MUST NOT produce
Information-level request-log lines, while real failures remain visible.

**Why this priority**: Operational hygiene; the endpoint works without it.

**Independent Test**: Run the API and poll `/health`; confirm no per-request Information log lines
for `/health`, then confirm other routes still log at Information.

**Acceptance Scenarios**:

1. **Given** a request to a path under `/health` completes without exception and with status < 500,
   **When** Serilog request logging evaluates it, **Then** it is logged at `Verbose` level.
2. **Given** any request throws or returns status >= 500 (including `/health`), **Then** it is
   logged at `Error` level.
3. **Given** any other request, **Then** it is logged at `Information` level (unchanged).

### Edge Cases

- `/health` is anonymous (`AllowAnonymous`), so it works regardless of JWT state and is not subject
  to the `[Authorize]` requirements on write endpoints.
- `/health` is mapped after `app.UseRateLimiter()` but has no rate-limit policy attached, so it is
  not rate limited.
- A failing database check surfaces through the standard health-check status, not through the
  global exception handler.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The API MUST expose `GET /health`, reachable without authentication.
- **FR-002**: The endpoint MUST run an EF Core `DbContext` check against `ApplicationDbContext`,
  registered under the name `database`.
- **FR-003**: The response MUST be JSON with `status`, a `checks` array (`name`, `status`,
  `durationMs`) and `totalDurationMs` (durations rounded to 1 decimal), using camelCase (web JSON
  defaults).
- **FR-004**: Request logging for paths starting with `/health` MUST be `Verbose` unless the request
  failed (exception or status >= 500), in which case it MUST be `Error`; all other requests MUST
  remain `Information`.
- **FR-005**: An automated test MUST cover the healthy path (status `200`, `status = "Healthy"`,
  `database` check present).

### Key Entities

- **Health Report (not persisted)**: overall status, per-check entries, total duration.

## Success Criteria *(mandatory)*

- **SC-001**: `GET /health` returns `200` with `status: "Healthy"` against a reachable database
  (verified by `HealthTests.Health_ReturnsHealthyWithDatabaseCheck`).
- **SC-002**: The endpoint requires no credential.
- **SC-003**: Successful health probes add no Information-level request-log entries.

## Assumptions

- Only a database check is required; no liveness/readiness split, no checks for other dependencies.
- The default health-check status-code mapping (Healthy `200`, Degraded `200`, Unhealthy `503`) is
  acceptable; no custom `ResultStatusCodes` are configured.
- The automated test runs against the EF Core in-memory provider, so it proves wiring and response
  shape, not real SQL Server connectivity.

## Known Gaps

- The unhealthy (database down) path is not automatically tested.
- Log-level behavior (User Story 2) has no automated test.
- Health output is unauthenticated and exposes check names and timings; acceptable for the current
  scope but worth revisiting if the API is exposed publicly.
