# Data Model: Error Handling and Logging

**No schema change.** No database model or migration was added (Constitution Principle II not
triggered).

## Transient / non-persisted shapes

### ProblemDetails (API 500 response)

| Field | Value |
|---|---|
| `title` | "An unexpected error occurred." |
| `status` | `500` |
| `traceId` (extension) | `Activity.Current?.Id`, else `HttpContext.TraceIdentifier` |

Content type: `application/problem+json`.

### ApiUnavailableException (MVC)

`ApiUnavailableException(string message, Exception? inner = null)`; constant
`FriendlyMessage` = "The service is unavailable. Please try again later."

### Log files

| App | Path pattern | Rolling | Retained |
|---|---|---|---|
| API | `logs/categoryproduct-api-.log` | daily | 14 files |
| MVC | `logs/categoryproduct-mvc-.log` | daily | 14 files |

Configuration key: `Serilog` (`MinimumLevel.Default` `Information`,
`MinimumLevel.Override["Microsoft.AspNetCore"]` `Warning`, `WriteTo` Console + File).
