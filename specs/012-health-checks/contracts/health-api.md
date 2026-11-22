# Contract: Health API

## GET /health

**Auth**: none (`AllowAnonymous`). **Request body**: none. No rate-limit policy attached.

**Success**: `200 OK`, `Content-Type: application/json`

```json
{
  "status": "Healthy",
  "checks": [ { "name": "database", "status": "Healthy", "durationMs": 1.2 } ],
  "totalDurationMs": 1.3
}
```

(Values illustrative; field names and shape come from `HealthCheckResponseWriter`.)

**Failure**: when the report status is `Unhealthy` the framework default is `503 Service
Unavailable` with the same JSON shape (default status-code mapping; not covered by a test).

**Note**: This is an infrastructure endpoint mapped directly in `Program.cs`, not an
`api/[controller]` resource, so the CRUD/pagination conventions of Principle I do not apply.
