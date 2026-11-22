# Feature Specification: Docker and CI

**Feature Branch**: `016-docker-ci`

**Created**: 2026-11-22

**Status**: Draft

**Input**: User description: "Containerize CategoryProduct.API and CategoryProduct.MVC with Docker, run them with SQL Server via docker compose (migrations applied automatically inside the container only), and add a GitHub Actions CI workflow that builds, tests, and builds both Docker images."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Run the whole stack with one command (Priority: P1)

A developer with only Docker installed copies `.env.example` to `.env`, fills in two secret values, and runs `docker compose up --build`. SQL Server, the API, and the MVC site start in the right order; the database schema is created automatically; the MVC site is reachable in a browser and can talk to the API.

**Why this priority**: This is the core deliverable; without it nothing else in the feature has value.

**Independent Test**: On a machine with Docker, follow `quickstart.md`: `/health` on the API returns `Healthy` including the `database` check, and the MVC login page loads and signs in against the API.

**Acceptance Scenarios**:

1. **Given** a fresh clone with a populated `.env`, **When** `docker compose up --build` is run, **Then** the `sql` container becomes healthy before the `api` container starts, and the `api` container applies all EF Core migrations and serves `GET /health` as `Healthy`.
2. **Given** the stack is running, **When** the MVC site is opened on the published host port, **Then** it renders and its calls to the API use `http://api:8080/` over the compose network.
3. **Given** the stack is restarted (`docker compose down` then `up`, without `-v`), **When** it comes back, **Then** previously created data is still present (named volume) and re-applying migrations is a no-op.
4. **Given** no `.env` file or a missing `JWT_KEY`/`SA_PASSWORD`, **When** compose is started, **Then** it fails fast with a clear message instead of starting with an empty or default secret.

---

### User Story 2 - Migrations auto-apply only inside the container (Priority: P1)

The API applies pending EF Core migrations at startup only when explicitly told to via configuration. The flag is off by default, so a developer running `dotnet run`, the test suite, or any non-container deployment never has the schema changed implicitly.

**Why this priority**: Without it, the containerized database is empty and the stack does not work; with the wrong default it would silently mutate developers' databases.

**Independent Test**: An integration test boots the API against the EF InMemory provider with the flag unset and with the flag `true` and confirms the app starts and serves requests in both cases (migration is skipped because the provider is not relational).

**Acceptance Scenarios**:

1. **Given** `Database:MigrateOnStartup` is absent or `false`, **When** the API starts, **Then** no migration is attempted.
2. **Given** `Database:MigrateOnStartup` is `true` and the provider is relational (SQL Server), **When** the API starts, **Then** pending migrations are applied before the API begins serving requests.
3. **Given** the flag is `true` but the database is not yet reachable, **When** the API starts, **Then** it retries a bounded number of times with a short delay and, if still failing, startup fails with a logged error (the container exits visibly rather than serving with no schema).
4. **Given** the flag is `true` and the provider is not relational (EF InMemory used by tests), **When** the API starts, **Then** the migration step is skipped without error.

---

### User Story 3 - Container-friendly HTTP hosting (Priority: P2)

Both images listen on plain HTTP port 8080 as a non-root user, and neither app tries to redirect to HTTPS or emit HSTS inside the container where TLS is not terminated. The MVC authentication cookie still works over plain HTTP in this setup.

**Why this priority**: Without these, the containers start but the user experience (or log noise and redirects) is degraded or broken.

**Independent Test**: `curl http://localhost:<api-port>/health` returns 200 with no redirect; MVC sign-in over `http://localhost:<mvc-port>` keeps the user signed in across requests.

**Acceptance Scenarios**:

1. **Given** `Hosting:UseHttpsRedirection` is `false` (set by compose), **When** a request arrives over HTTP, **Then** it is served directly with no redirect and no "failed to determine the https port" warning, and MVC does not emit HSTS.
2. **Given** the setting is absent (local `dotnet run`, tests), **When** the apps start, **Then** behavior is exactly as before this feature (redirection on).
3. **Given** `Cookie:SecurePolicy` is `SameAsRequest` (set by compose), **When** a user signs in over HTTP, **Then** the browser stores and returns the cookie; when the setting is absent the cookie remains `Always` secure as today.
4. **Given** the images are inspected, **Then** the process runs as a non-root user and listens on 8080.

---

### User Story 4 - Continuous integration on GitHub Actions (Priority: P2)

On every push and pull request, a workflow restores, builds in Release, and runs the tests with .NET 10, and a second job builds both Docker images (without pushing) to catch Dockerfile breakage.

**Why this priority**: Protects the repo but does not block local use of the stack.

**Independent Test**: Review the workflow file; once pushed, both jobs appear on the commit and pass. (Not runnable locally; see Known Gaps.)

**Acceptance Scenarios**:

1. **Given** a push or pull request, **When** the workflow triggers, **Then** the `build-test` job runs restore, `build -c Release --no-restore`, and `test -c Release --no-build`.
2. **Given** the same trigger, **When** the `docker-build` job runs, **Then** it runs `docker build` for the API and MVC Dockerfiles with the repo root as context and does not push.
3. **Given** a failing test or a broken Dockerfile, **Then** the corresponding job fails and the run is marked failed.

---

### Edge Cases

- SA password that does not meet SQL Server complexity rules: the `sql` container exits at startup; `.env.example` documents the rule and the quickstart tells the developer to check `docker compose logs sql`.
- SA password containing `;`, `=`, `$`, quotes, or leading/trailing spaces would corrupt the connection string or compose interpolation; documented restriction.
- API starts before SQL accepts logins even though the container is "healthy": covered by the migration retry loop.
- Two API replicas migrating concurrently: out of scope (single `api` service).
- Stale named volume created with a different SA password than the current `.env`: SQL Server keeps the original password and the API login fails. Documented fix: `docker compose down -v` (destroys data).
- `Jwt__Key` shorter than 32 characters: the API throws at startup (existing check in `CategoryProduct.API/Program.cs`) and the container exits; `.env.example` states the minimum.
- Non-root user cannot write to `/app`: the Serilog file sink (`logs/...` in both `appsettings.json` files) would fail; the Dockerfiles pre-create a writable `/app/logs`.
- Containers run as `Production`, so Swagger UI (Development only in `Program.cs`) is not exposed; use `/health`, MVC, or an HTTP client.
- The migration `SeedTestUserForAuth` inserts a `testuser` row (hash committed in the migration) into any database that runs migrations, including the compose one. Pre-existing behavior; noted as a risk, not changed here.
- MVC data-protection keys are not persisted: restarting the `mvc` container invalidates existing sign-in cookies. Accepted.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The repo MUST contain `CategoryProduct.API/Dockerfile` and `CategoryProduct.MVC/Dockerfile`, each multi-stage (`mcr.microsoft.com/dotnet/sdk:10.0` build, `mcr.microsoft.com/dotnet/aspnet:10.0` runtime), built with the repo root as context, running as a non-root user and listening on HTTP port 8080.
- **FR-002**: The repo MUST contain a root `.dockerignore` excluding at least `bin/`, `obj/`, `.git/`, `.vs/`, `.env`, `logs/`, and `specs/`.
- **FR-003**: The repo MUST contain a root `docker-compose.yml` defining `sql` (`mcr.microsoft.com/mssql/server:2022-latest`, healthcheck, named volume), `api`, and `mvc` services.
- **FR-004**: The `api` service MUST wait for `sql` to be healthy (`depends_on` with `condition: service_healthy`); the `mvc` service MUST be configured with `Api__BaseUrl=http://api:8080/` and depend on `api` having started.
- **FR-005**: The API MUST read `Database:MigrateOnStartup` (default `false` in `appsettings.json`); when `true` and the DbContext provider is relational it MUST call `Database.MigrateAsync()` before serving requests, with a bounded retry loop; when the provider is not relational or the flag is false it MUST do nothing.
- **FR-006**: Only `docker-compose.yml` (via environment) MAY set `Database__MigrateOnStartup=true`; no committed `appsettings*.json` may enable it.
- **FR-007**: Secrets (`Jwt__Key`, SQL Server SA password, and the connection string built from it) MUST be supplied via compose environment interpolated from a gitignored `.env` file; `.env` MUST be added to `.gitignore`; only `.env.example` with placeholder values is committed. Compose MUST fail fast when a required variable is unset (`${VAR:?message}`).
- **FR-008**: Both apps MUST support a config flag `Hosting:UseHttpsRedirection` (absent means `true`); when `false`, `UseHttpsRedirection()` (and MVC `UseHsts()`) MUST be skipped. Compose MUST set it to `false` for both services.
- **FR-009**: The MVC auth cookie secure policy MUST remain `Always` by default and MUST be overridable to `SameAsRequest` via config `Cookie:SecurePolicy`; compose sets `SameAsRequest` for the container.
- **FR-010**: The repo MUST contain `.github/workflows/ci.yml` triggered on `push` and `pull_request` with a `build-test` job (`actions/checkout`, `actions/setup-dotnet` with `10.0.x`, `dotnet restore`, `dotnet build -c Release --no-restore`, `dotnet test -c Release --no-build`) and a `docker-build` job that runs `docker build` for both Dockerfiles without pushing.
- **FR-011**: An automated API integration test MUST prove the startup migration step is skipped (no exception, app serves requests) when the flag is unset/false and when it is true under the InMemory provider.
- **FR-012**: No real secret value, password, or key may appear in any committed file; placeholders in `.env.example` MUST be obviously fake.
- **FR-013**: Existing behavior outside containers (launch profiles, user-secrets JWT key, SQL Express connection string, existing tests) MUST be unchanged.

### Key Entities

No persisted entities are added or changed. See `data-model.md` for configuration keys and environment variables.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: From a fresh clone, copying `.env.example` to `.env`, editing two values, and running `docker compose up --build` yields a healthy API (`/health` = `Healthy`) and a loading MVC site with no manual database step.
- **SC-002**: `dotnet test` passes with all pre-existing tests plus the new migration-skip tests, and none of them touch a real SQL Server.
- **SC-003**: A search of tracked files finds no real credential; `.env` is ignored by git.
- **SC-004**: Both Docker images build successfully in CI and run as a non-root user.
- **SC-005**: A developer running `dotnet run` locally sees no change in behavior (no automatic migrations, HTTPS redirection active).

## Assumptions

- **Build context and Dockerfile location**: each Dockerfile lives in its project folder but is built with the repo root as context (`docker build -f CategoryProduct.API/Dockerfile .`), so a single root `.dockerignore` applies. The MVC image copies only the MVC project (no reference to the API project, consistent with constitution Principle V); the API image does not copy the test project.
- **Port**: apps listen on 8080 via `ASPNETCORE_HTTP_PORTS=8080` (already the default in .NET 8+ aspnet images, set explicitly for clarity). Host ports default to API 5028 and MVC 5256, matching the HTTP ports in each project's `launchSettings.json` (verified). SQL Server is not published to the host.
- **Non-root**: the runtime stage uses the image's built-in `app` user via `USER $APP_UID`.
- **Migration flag**: key `Database:MigrateOnStartup`, env `Database__MigrateOnStartup`. Default `false` in `CategoryProduct.API/appsettings.json`. Retry: 10 attempts, 3-second delay (constants; YAGNI). Relational check via `Database.IsRelational()`. Logic lives in a small static helper in `CategoryProduct.API/Data/`, called from `Program.cs` after `Build()` and before `app.Run()`.
- **HTTPS redirection decision**: both `Program.cs` files call `app.UseHttpsRedirection()` unconditionally (verified). In .NET this does not throw when no HTTPS port exists; it logs a warning and passes the request through. A flag is still chosen to keep logs clean and make intent explicit and testable. Key `Hosting:UseHttpsRedirection`, absent means `true`. MVC `UseHsts()` (non-Development only) is gated by the same flag.
- **MVC cookie decision**: MVC sets `CookieSecurePolicy.Always` (verified), which browsers may reject over plain HTTP; a config override `Cookie:SecurePolicy` (default `Always`) is added and compose sets `SameAsRequest`. TLS termination by a reverse proxy is out of scope.
- **Environment**: containers run with `ASPNETCORE_ENVIRONMENT=Production` (image default); Swagger is off.
- **Connection string**: provided via `ConnectionStrings__DefaultConnection` in compose: `Server=sql,1433;Database=CategoryProductDB;User Id=sa;Password=<SA_PASSWORD>;TrustServerCertificate=True;`. The committed value in `appsettings.json` (a machine-specific SQL Express string with Windows auth, no password) is not changed.
- **Health checks**: `sql` uses `sqlcmd` from `/opt/mssql-tools18/bin` with `-C`. The `api` service has no container healthcheck because the aspnet runtime image ships neither `curl` nor `wget`; `mvc` therefore depends on `api` with `condition: service_started`.
- **Logs**: Dockerfiles create `/app/logs` owned by the non-root user so the existing Serilog file sink works; the console sink is what `docker compose logs` shows.
- **CI**: single workflow file, `ubuntu-latest`, no image push, no secrets needed (tests set their own JWT key and use InMemory, verified in `ApiFactory.cs`).
- **Out of scope**: pushing images to a registry, TLS in containers, Kubernetes, persisting data-protection keys, multi-replica migration coordination, Swagger in containers, container healthchecks for api/mvc.
- **Overlap**: feature `015-auth-hardening` may also edit `CategoryProduct.API/Program.cs`; this feature's edits there are additive and localized (redirection guard, one helper call) so merges should be trivial.

## Known Gaps

- **Docker and `gh` are not installed on the authoring machine**: the Dockerfiles, `docker-compose.yml`, `.dockerignore`, and the GitHub Actions workflow were NOT built, run, or executed locally. They can only be validated by careful review and by the first real run (`docker compose up --build` on a Docker host; the workflow on GitHub). Treat them as unverified until then.
- The only locally verifiable parts are the C# changes (`dotnet build`, `dotnet test`) and YAML/JSON syntax.
- No YAML parser was available locally (no Python, no installed node YAML module); `docker-compose.yml` and `ci.yml` were validated by manual review only.
- The constitution's Technology Stack section says MVC has its own EF Core + SQL Server data access, but `CategoryProduct.MVC.csproj` references only Serilog and MVC calls the API over HTTP; compose therefore gives MVC no database connection. The constitution text is outdated; amending it is out of scope.
- The `api` and `mvc` containers have no healthcheck (see Assumptions).
- The MVC container does not persist ASP.NET Core Data Protection keys, so auth cookies become
  invalid when the `mvc` container is recreated (users sign in again). Found in Review.
- Migration `SeedTestUserForAuth` seeds its test user into the compose database too; change or
  remove that user's password before exposing the stack. Found in Review.
