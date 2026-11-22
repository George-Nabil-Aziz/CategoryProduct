# Quickstart: Docker and CI

Not validated locally (Docker is not installed on the authoring machine); see Known Gaps in `spec.md`.

## Run the stack

1. Copy `.env.example` to `.env` (`cp .env.example .env`).
2. Edit `.env`: set `SA_PASSWORD` (8+ chars with upper, lower, digit, symbol; avoid `; = $` and quotes) and `JWT_KEY` (32+ random characters, e.g. `openssl rand -base64 48`).
3. `docker compose up --build`.
4. Wait for the `api` logs to show migrations applied and the app listening on 8080.
5. Open `http://localhost:5028/health` and expect `Healthy` with a `database` check.
6. Open `http://localhost:5256` (MVC) and sign in with an existing account.

## Stop / reset

- `docker compose down` keeps data; `docker compose down -v` deletes the `sqldata` volume.

## Troubleshooting

- `sql` exits: weak password; read `docker compose logs sql`.
- `api` exits with `Jwt:Key is missing or shorter than 32 characters`: fix `JWT_KEY`.
- `api` cannot log in to SQL after changing `SA_PASSWORD`: stale volume; `docker compose down -v`.
- Compose reports a required variable is missing: `.env` is absent or incomplete.

## Verify code-level behavior (no Docker needed)

- `dotnet build CategoryProduct.slnx -c Release` and `dotnet test CategoryProduct.slnx -c Release`; the new `MigrationStartupTests` must pass.

## CI

Push a branch or open a PR; confirm `build-test` and `docker-build` both pass in the Actions tab.
