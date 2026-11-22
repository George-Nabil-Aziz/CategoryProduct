# Data Model: Health Checks

**No schema change.** No migration was added; no entity or `DbContext` change.

## Health Report (runtime shape, not persisted)

Produced by `HealthCheckResponseWriter.WriteAsync` (`CategoryProduct.API/Infrastructure/`).

| Field | Type | Notes |
|-------|------|-------|
| `status` | string | `report.Status.ToString()`: `Healthy`, `Degraded` or `Unhealthy` |
| `checks[].name` | string | Registered check name; currently only `database` |
| `checks[].status` | string | Per-check status |
| `checks[].durationMs` | number | Rounded to 1 decimal |
| `totalDurationMs` | number | Rounded to 1 decimal |
