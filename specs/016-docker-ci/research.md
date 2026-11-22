# Research: Docker and CI

## Decision 1: Where migrations run

**Decision**: In-process at API startup behind `Database:MigrateOnStartup` (default false), guarded by `Database.IsRelational()`, with a 10 x 3 s retry loop.

**Rationale**: Simplest approach that works with `docker compose up`; no extra tooling image or `efbundle`. The flag is only set in compose, so local runs and tests are unaffected (Constitution II stays intact: the schema still changes only through committed migrations).

**Alternatives considered**: an `efbundle` init container (needs SDK at build time and an extra service; rejected under YAGNI); running `dotnet ef database update` manually (breaks the one-command goal); always migrating (mutates developers' databases implicitly; rejected).

## Decision 2: HTTPS redirection in containers

**Decision**: Config flag `Hosting:UseHttpsRedirection` (absent = true); compose sets false for both apps. MVC also skips `UseHsts()` under the same flag.

**Rationale**: Verified: both `Program.cs` files call `app.UseHttpsRedirection()` unconditionally. With no HTTPS port the middleware only warns, but an explicit flag avoids warning noise and a redirect to an unreachable port if an HTTPS port is ever configured.

**Alternatives considered**: auto-detect from `ASPNETCORE_HTTPS_PORT(S)` (implicit, harder to test); removing the call (breaks local HTTPS profile behavior).

## Decision 3: MVC cookie over HTTP

**Decision**: `Cookie:SecurePolicy` config (default `Always`); compose sets `SameAsRequest`.

**Rationale**: MVC sets `CookieSecurePolicy.Always`; over plain HTTP some browsers refuse Secure cookies, which would make login appear broken in the container.

## Decision 4: Image layout

**Decision**: One Dockerfile per project, repo-root context. Build stage: copy csproj, `dotnet restore`, copy sources, `dotnet publish -c Release --no-restore -p:UseAppHost=false -o /app/publish`. Runtime stage: `aspnet:10.0`, `ASPNETCORE_HTTP_PORTS=8080`, `EXPOSE 8080`, pre-created `/app/logs` owned by `$APP_UID`, `USER $APP_UID`.

**Rationale**: Standard layer-caching pattern; .NET 8+ images ship a non-root `app` user and default to port 8080. The API image copies only the API project (not the test project); the MVC image only the MVC project.

## Decision 5: Secrets

**Decision**: `.env` (gitignored; the current `.gitignore` has no `.env` rule, so one is added) feeds compose interpolation; `.env.example` has placeholders `SA_PASSWORD` and `JWT_KEY`. Compose maps `JWT_KEY` to `Jwt__Key`. `${VAR:?}` fails fast when unset.

**Verified**: API `Program.cs` reads `Jwt:Key` from configuration (so `Jwt__Key` works) and throws if it is missing or under 32 characters.

**Note**: avoid `;`, `=`, `$`, quotes in `SA_PASSWORD` (connection string and compose interpolation).

## Decision 6: CI shape

**Decision**: `ubuntu-latest`; job `build-test` (checkout, setup-dotnet 10.0.x, restore, build Release, test Release) and independent job `docker-build` (checkout, two `docker build` commands, no push). The solution file is `CategoryProduct.slnx`; the .NET 10 SDK supports `.slnx`.

**Alternatives considered**: `docker/build-push-action` with buildx cache (extra complexity, no push needed); a matrix for two images (not justified).

## Decision 7: Test for migration skip

**Decision**: Add `CategoryProduct.API.Tests/MigrationStartupTests.cs`: (a) call the helper with an InMemory-backed service provider and flag true, asserting no exception; (b) a `WebApplicationFactory` subclass setting `Database:MigrateOnStartup=true` that asserts `/health` returns 200; (c) the flag-unset case covered by the existing `ApiFactory`, with an explicit assertion. `ApiFactory` already replaces SQL Server with InMemory (verified), so no real DB is touched.
