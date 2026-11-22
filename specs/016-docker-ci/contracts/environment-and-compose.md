# Contract: Environment variables and compose services

## Compose services

| Service | Image / build | Container port | Host port | Depends on | Key environment |
|---|---|---|---|---|---|
| `sql` | `mcr.microsoft.com/mssql/server:2022-latest` | 1433 | not published | none | `ACCEPT_EULA=Y`, `MSSQL_SA_PASSWORD=${SA_PASSWORD:?}` |
| `api` | build `CategoryProduct.API/Dockerfile`, context `.` | 8080 | `${API_HOST_PORT:-5028}` | `sql` (`service_healthy`) | `Jwt__Key=${JWT_KEY:?}`, `ConnectionStrings__DefaultConnection=Server=sql,1433;Database=CategoryProductDB;User Id=sa;Password=${SA_PASSWORD:?};TrustServerCertificate=True;`, `Database__MigrateOnStartup=true`, `Hosting__UseHttpsRedirection=false` |
| `mvc` | build `CategoryProduct.MVC/Dockerfile`, context `.` | 8080 | `${MVC_HOST_PORT:-5256}` | `api` (`service_started`) | `Api__BaseUrl=http://api:8080/`, `Hosting__UseHttpsRedirection=false`, `Cookie__SecurePolicy=SameAsRequest` |

- Named volume `sqldata` mounted at `/var/opt/mssql` on `sql`.
- `sql` healthcheck: `/opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$$MSSQL_SA_PASSWORD" -Q "SELECT 1"` (interval 10s, timeout 5s, retries 10, start_period 20s).
- `Jwt:Issuer` and `Jwt:Audience` come from `appsettings.json`; no override needed.

## Image contract

- Runtime base `mcr.microsoft.com/dotnet/aspnet:10.0`; `USER $APP_UID`; `ASPNETCORE_HTTP_PORTS=8080`; `EXPOSE 8080`; entrypoint `dotnet CategoryProduct.API.dll` / `dotnet CategoryProduct.MVC.dll`.

## Behavioral contract

- `Database__MigrateOnStartup=true`: relational provider leads to up to 10 attempts 3 s apart, then startup fails; non-relational provider leads to skip.
- Unset or false: never migrate.
- `Hosting__UseHttpsRedirection=false`: no redirect or HSTS; unset: unchanged.
- `Cookie__SecurePolicy=SameAsRequest`: cookie secure policy follows the request scheme; unset: `Always`.

## CI contract (`.github/workflows/ci.yml`)

Triggers: `push`, `pull_request`. Jobs: `build-test` (setup-dotnet `10.0.x`; restore; build Release; test Release) and `docker-build` (`docker build -f CategoryProduct.API/Dockerfile -t categoryproduct-api:ci .`; same for MVC). No push, no secrets.
