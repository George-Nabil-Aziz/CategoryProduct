# Contract: CI job, test-project interface, MVC and compose configuration

## 1. `sqlserver-tests` job (added to `.github/workflows/ci.yml`)

Workflow-level addition (the existing `on:` and jobs stay as they are):

```yaml
env:
  # Test-only password for the ephemeral SQL Server service container. Not a secret.
  # Keep in sync with jobs.sqlserver-tests.services.sqlserver.env.MSSQL_SA_PASSWORD
  # (the env context is not available inside services.<id>.env).
  SQLSERVER_CI_SA_PASSWORD: Ci-Only_Sa-Pass-1234
```

Job:

```yaml
  sqlserver-tests:
    runs-on: ubuntu-latest
    services:
      sqlserver:
        image: mcr.microsoft.com/mssql/server:2022-latest
        env:
          ACCEPT_EULA: "Y"
          MSSQL_SA_PASSWORD: Ci-Only_Sa-Pass-1234
        ports:
          - 1433:1433
        options: >-
          --health-cmd "/opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P $MSSQL_SA_PASSWORD -Q 'SELECT 1'"
          --health-interval 10s
          --health-timeout 5s
          --health-retries 10
          --health-start-period 20s
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x
      - run: dotnet test CategoryProduct.API.SqlServerTests -c Release
        env:
          SQLSERVER_TEST_CONNECTION: Server=localhost,1433;User Id=sa;Password=${{ env.SQLSERVER_CI_SA_PASSWORD }};TrustServerCertificate=True;
```

Notes: the password has no `;`, `=`, `$` or spaces and meets SQL Server complexity. The job does not depend on `build-test`. `dotnet test` restores and builds the project and the referenced API project itself. Health-check quoting and `--health-start-period` support are unverified until the first GitHub run.

## 2. Test-project interface

- Env var `SQLSERVER_TEST_CONNECTION`: a connection string without or with a database; the factory replaces the database with `CategoryProductTest_<guid>` (`SqlConnectionStringBuilder.InitialCatalog`). Login needs permission to create and drop databases (`sa` in CI).
- Missing/blank: `InvalidOperationException("Environment variable SQLSERVER_TEST_CONNECTION is not set. Point it at a SQL Server where the login can create and drop databases, e.g. Server=localhost,1433;User Id=sa;Password=...;TrustServerCertificate=True;")`.
- `SqlServerApiFactory` public surface used by tests: `CreateClient()`, `CreateUserAsync(string name, string role)`, `LoginAsync(string name)` (returns access and refresh tokens), `WithDbAsync(Func<ApplicationDbContext, Task>)`, `ConnectionString`, `DatabaseName`.
- Run locally: `dotnet test CategoryProduct.API.SqlServerTests` after setting the variable. Not part of `dotnet test CategoryProduct.slnx`.

## 3. MVC configuration

| Key | Effect |
|---|---|
| `DataProtection:KeysPath` set | `AddDataProtection().SetApplicationName("CategoryProduct.MVC").PersistKeysToFileSystem(new DirectoryInfo(path))` |
| not set / blank | no Data Protection call; framework defaults |

## 4. Docker

`CategoryProduct.MVC/Dockerfile` (final stage, before `USER $APP_UID`):

```dockerfile
RUN mkdir -p /data/dp-keys && chown $APP_UID /data/dp-keys
```

`docker-compose.yml`, service `mvc`: add `DataProtection__KeysPath: /data/dp-keys` to `environment`, `volumes: [mvckeys:/data/dp-keys]`; top-level `volumes:` gains `mvckeys:`. `.env.example` needs no change.
