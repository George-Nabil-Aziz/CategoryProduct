# Data Model: Docker and CI

**No data model changes.** No entities, columns, indexes, or EF Core migrations are added or modified. The existing migrations in `CategoryProduct.API/Migrations/` (`20260731184210_InitialCreate` through `20261103183150_AddRefreshTokens`) are applied as-is by the startup step.

## Configuration keys

| Key | Env var form | Project | Committed default | Purpose |
|---|---|---|---|---|
| `Database:MigrateOnStartup` | `Database__MigrateOnStartup` | API | `false` in `appsettings.json` | Apply pending migrations at startup (relational provider only) |
| `Hosting:UseHttpsRedirection` | `Hosting__UseHttpsRedirection` | API, MVC | absent (= true) | Skip HTTPS redirection / HSTS when false |
| `Cookie:SecurePolicy` | `Cookie__SecurePolicy` | MVC | absent (= `Always`) | `Always` or `SameAsRequest` for the auth cookie |
| `Jwt:Key` | `Jwt__Key` | API | none (required, at least 32 chars) | JWT signing key |
| `ConnectionStrings:DefaultConnection` | `ConnectionStrings__DefaultConnection` | API | SQL Express string (local only) | Database connection |
| `Api:BaseUrl` | `Api__BaseUrl` | MVC | `https://localhost:7183/` | API address |

## `.env` variables (compose interpolation only)

| Variable | Required | Placeholder in `.env.example` |
|---|---|---|
| `SA_PASSWORD` | yes | `ChangeMe_Str0ngPassw0rd` |
| `JWT_KEY` | yes | `replace-with-a-random-string-of-at-least-32-characters` |
| `API_HOST_PORT` | no (default 5028) | `5028` |
| `MVC_HOST_PORT` | no (default 5256) | `5256` |
