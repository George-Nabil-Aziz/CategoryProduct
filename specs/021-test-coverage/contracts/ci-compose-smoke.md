# Contract: CI job `compose-smoke`

File: `.github/workflows/ci.yml`. Add a fourth job; do not edit `build-test`, `docker-build` or `sqlserver-tests`, and do not edit the workflow-level `env`.

## Job

```yaml
  compose-smoke:
    runs-on: ubuntu-latest
    timeout-minutes: 20
    steps:
      - uses: actions/checkout@v4

      - name: Create throwaway .env (random, CI-only, never committed)
        run: |
          SA_PASSWORD="Ci-$(openssl rand -hex 12)-Aa1"
          JWT_KEY="$(openssl rand -hex 32)"
          echo "::add-mask::$SA_PASSWORD"
          echo "::add-mask::$JWT_KEY"
          {
            echo "SA_PASSWORD=$SA_PASSWORD"
            echo "JWT_KEY=$JWT_KEY"
            echo "API_HOST_PORT=5028"
            echo "MVC_HOST_PORT=5256"
            echo "LOW_STOCK_THRESHOLD=5"
          } > .env

      - name: Start the stack
        run: docker compose up -d --build

      - name: Wait for API /health
        run: |
          for i in $(seq 1 60); do
            code=$(curl -s -o health.json -w '%{http_code}' --max-time 5 http://localhost:5028/health || true)
            if [ "$code" = "200" ]; then
              cat health.json; echo
              grep -q '"status":"Healthy"' health.json
              grep -q '"name":"database"' health.json
              exit 0
            fi
            echo "attempt $i: /health returned '$code'"; sleep 5
          done
          echo "API did not become healthy in time"; exit 1

      - name: Wait for MVC home page
        run: |
          for i in $(seq 1 60); do
            code=$(curl -s -o home.html -w '%{http_code}' --max-time 10 http://localhost:5256/ || true)
            if [ "$code" = "200" ]; then
              grep -q 'CategoryProduct.MVC' home.html
              echo "MVC home OK"; exit 0
            fi
            echo "attempt $i: / returned '$code'"; sleep 5
          done
          echo "MVC did not serve the home page in time"; exit 1

      - name: Show compose state and logs on failure
        if: failure()
        run: |
          docker compose ps -a
          docker compose logs --no-color --tail=200

      - name: Tear down
        if: always()
        run: |
          docker compose down -v --remove-orphans
          rm -f .env health.json home.html
```

(The YAML above is the normative shape; the implementer may adjust shell details but must keep every behavior in the checklist below.)

## Behavior checklist

| # | Requirement | Where |
|---|---|---|
| 1 | `.env` created at runtime; values from `openssl rand`; masked with `::add-mask::`; not committed (`.env` is git-ignored and docker-ignored) | step 2 |
| 2 | `SA_PASSWORD` satisfies SQL Server complexity (upper, lower, digit, symbol, >= 8) and contains none of `; = $` quotes or spaces (`.env.example` rule) | `Ci-<hex>-Aa1` |
| 3 | `JWT_KEY` at least 32 characters | 64 hex chars |
| 4 | Stack built and started from the repo's `docker-compose.yml` unchanged | `docker compose up -d --build` |
| 5 | API readiness polled with timeout (60 x 5 s), asserts HTTP 200, `"status":"Healthy"`, and a `database` check | step 4 |
| 6 | MVC readiness polled with timeout, asserts HTTP 200 and the HTML contains `CategoryProduct.MVC` (page `<title>`); because `/` renders `Categories/Index`, this also proves MVC -> API | step 5 |
| 7 | On any failure, `docker compose ps -a` and `docker compose logs` are printed | `if: failure()` |
| 8 | Always `docker compose down -v --remove-orphans` | `if: always()` |
| 9 | `timeout-minutes` set | job level |
| 10 | No secret appears in the log (masked; the step does not `cat .env`) | steps 2, 7 |
| 11 | Other jobs unchanged | diff |

## Notes and risks

- The JSON writer emits camelCase without spaces (`"status":"Healthy"`), matching the `grep`; if the first run shows spacing differences, switch to `jq -e '.status=="Healthy"'` (pre-installed on `ubuntu-latest`).
- `docker compose up -d --build` returns once containers are started; readiness comes from the polling steps. The `api` container has no healthcheck by design (016).
- Migration on startup (`Database__MigrateOnStartup=true`) takes a few seconds after SQL Server is healthy; the 5-minute polling window covers image pull, build and migration.
- Ports 5028 and 5256 are the compose defaults; keep `.env` values equal to the `curl` URLs.
- Unverified until the first GitHub Actions run (no Docker on the authoring machine). Record the outcome in `spec.md` Known Gaps like 020 did.
- The job duplicates the image builds of `docker-build`; sharing a cache is out of scope.
