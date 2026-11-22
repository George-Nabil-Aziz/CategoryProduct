# Research: Health Checks

## Decision 1: Use built-in ASP.NET Core health checks

**Decision**: `builder.Services.AddHealthChecks().AddDbContextCheck<ApplicationDbContext>("database")`
and `app.MapHealthChecks("/health", ...)`.

**Rationale**: Framework-native, no custom controller, standard status semantics. Matches
Principle III (no new abstraction).

**Alternatives considered**: A hand-written `HealthController` running a connectivity check was
rejected because it would duplicate what the framework provides and would not use the standard
status-code mapping.

## Decision 2: EF Core check via a NuGet package

**Decision**: Add `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` `10.0.10` to
`CategoryProduct.API.csproj`.

**Rationale**: `AddDbContextCheck<T>` lives in this package. It is the only new dependency.

## Decision 3: Custom JSON response writer

**Decision**: `Infrastructure/HealthCheckResponseWriter.WriteAsync` emits
`{status, checks[{name,status,durationMs}], totalDurationMs}` with `JsonSerializerDefaults.Web`.

**Rationale**: The default writer returns plain text (for example `Healthy`), which is hard for
tools to parse per check. Exception messages and check `data` are deliberately not serialized, so
internal error details are not leaked.

## Decision 4: Anonymous access

**Decision**: `.AllowAnonymous()` on the health endpoint.

**Rationale**: Probes from load balancers and orchestrators have no JWT.

## Decision 5: Quiet request logging for `/health`

**Decision**: Configure `UseSerilogRequestLogging` `GetLevel`: exception or status >= 500 ->
`Error`; path under `/health` -> `Verbose`; otherwise `Information`.

**Rationale**: Probes are frequent; the failure case must stay visible, so errors win over the
path rule (order of conditions in the lambda).

**Alternatives considered**: Excluding the path from request logging entirely was rejected because
failed probes would then be invisible.
