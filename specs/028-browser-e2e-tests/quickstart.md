# Quickstart: Browser end-to-end tests

## Offline (no Docker, no browser): build and see the tests skip

```powershell
dotnet build CategoryProduct.E2ETests
dotnet test CategoryProduct.E2ETests        # every test skipped, exit code 0
dotnet test CategoryProduct.slnx            # unchanged: the E2E project is not part of the solution
```

## Real run against the compose stack (needs Docker)

1. Create a `.env` next to `docker-compose.yml` (never commit it):

   ```text
   SA_PASSWORD=<strong random>
   JWT_KEY=<64 hex chars>
   MVC_HOST_PORT=5256
   BOOTSTRAP_ADMIN_USERNAME=e2e-admin
   BOOTSTRAP_ADMIN_PASSWORD=<random, 8+ chars>
   ```

2. Start the stack WITH the E2E override (raises the per-IP rate limits):

   ```powershell
   docker compose -f docker-compose.yml -f docker-compose.e2e.yml up -d --build
   ```

   The bootstrap admin is created only on a database that has no Admin yet; after `docker compose down -v` it is created again.

3. Install the browser once (after the first build):

   ```powershell
   dotnet build CategoryProduct.E2ETests
   pwsh CategoryProduct.E2ETests/bin/Debug/net10.0/playwright.ps1 install chromium
   ```

4. Run:

   ```powershell
   $env:E2E_BASE_URL = "http://localhost:5256"
   $env:E2E_ADMIN_USERNAME = "e2e-admin"
   $env:E2E_ADMIN_PASSWORD = "<the password from .env>"
   dotnet test CategoryProduct.E2ETests
   ```

   Useful: `$env:E2E_HEADED = "1"`, `$env:E2E_SLOWMO_MS = "300"`, `--filter "FullyQualifiedName~OrderFlowTests"`.

5. Tear down: `docker compose -f docker-compose.yml -f docker-compose.e2e.yml down -v`.

## After a failure

`test-results/` holds `<Class>.<Test>.png`, `.zip` (trace) and `.txt`. Open a trace:

```powershell
pwsh CategoryProduct.E2ETests/bin/Debug/net10.0/playwright.ps1 show-trace test-results/<Class>.<Test>.zip
```

In CI the same folder is the artifact `e2e-failure-artifacts` of the `e2e` job (kept 7 days).

## Common problems

- `E2E: cannot reach <url>`: the stack is not up or `E2E_BASE_URL` has the wrong port.
- `Rate limited by the API`: the stack was started without `docker-compose.e2e.yml`.
- Admin login fails: the database already had an Admin (the bootstrap does nothing then); use that Admin's credentials or `down -v` and start again.
- Everything skipped: `E2E_BASE_URL` is not set in the shell that runs `dotnet test`.
