# Quickstart: Test Coverage

## Run everything that needs no Docker or database

```
dotnet build CategoryProduct.slnx
dotnet test CategoryProduct.slnx
```

This runs `CategoryProduct.API.Tests` and the new `CategoryProduct.MVC.Tests` (both are in the solution).

## Run only the MVC tests

```
dotnet test CategoryProduct.MVC.Tests
dotnet test CategoryProduct.MVC.Tests --filter "FullyQualifiedName~CartTests"
```

## Run only the new API tests

```
dotnet test CategoryProduct.API.Tests --filter "FullyQualifiedName~RefreshTokenCleanupWorkerTests|FullyQualifiedName~RefreshEdgeCaseTests"
```

## SQL Server tests (unchanged from 020)

See `specs/020-hardening-followups/quickstart.md`; the project stays outside the solution.

## Compose smoke test locally (needs a Docker host)

Bash (generates the same kind of throwaway values the CI job uses; do not commit `.env`):

```
SA="Ci-$(openssl rand -hex 12)-Aa1"; JWT="$(openssl rand -hex 32)"
printf 'SA_PASSWORD=%s\nJWT_KEY=%s\nAPI_HOST_PORT=5028\nMVC_HOST_PORT=5256\nLOW_STOCK_THRESHOLD=5\n' "$SA" "$JWT" > .env
docker compose up -d --build
curl -s http://localhost:5028/health          # expect "status":"Healthy" and a database check
curl -s -o /dev/null -w '%{http_code}\n' http://localhost:5256/   # expect 200
docker compose logs --tail=100                # when something fails
docker compose down -v
rm .env
```

PowerShell: generate with `-join ((1..24) | ForEach-Object { '{0:x}' -f (Get-Random -Maximum 16) })`, write `.env` with `Set-Content -Encoding ascii`, same compose commands.

## CI

Push the branch. Expected jobs: `build-test` (now includes MVC tests), `docker-build`, `sqlserver-tests`, `compose-smoke`. If `compose-smoke` fails, the job log ends with `docker compose ps -a` and the last 200 log lines of every service.

## Adding an MVC test

1. Derive from `MvcTestBase` and add `IClassFixture<MvcFactory>`.
2. Script what the page needs: `Fake.On(HttpMethod.Get, "api/category", FakeResponse.Json(200, ApiJson.Paged(new[] { ApiJson.Category(1, "Books") })))`.
3. `await SignInAdminAsync();` if the page needs a session.
4. `var r = await Client.GetAsync("/Categories");` then assert on `await r.Content.ReadAsStringAsync()` (use `HtmlHelpers.Decode`) and on `Fake.Requests`.
5. For a POST: `await PostFormAsync("/Categories/Create", new() { ["Name"] = "Books" })` (the helper fetches the antiforgery token from the page you name, or pass the form page URL explicitly).
6. An unscripted API call fails the test with `FakeApiUnmatchedException`; script the route or fix the test.
