# Implementation Plan: Docker and CI

**Branch**: `016-docker-ci` | **Date**: 2026-11-22 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/016-docker-ci/spec.md`

## Summary

Containerize both apps (multi-stage, non-root, HTTP 8080), add `docker-compose.yml` with SQL Server, a flag-controlled migrate-on-startup step in the API (skipped for non-relational providers), flags for HTTPS redirection and MVC cookie secure policy, secrets via gitignored `.env`, and a GitHub Actions workflow (build, test, docker build). Only small, additive C# changes; the rest is configuration files.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0` in all three csproj files)

**Primary Dependencies**: existing only (EF Core 10 SqlServer, Serilog.AspNetCore, JwtBearer). No new NuGet packages.

**Storage**: SQL Server 2022 container with named volume; no schema change.

**Testing**: xUnit + `WebApplicationFactory<Program>` with EF InMemory (`CategoryProduct.API.Tests`). New `MigrationStartupTests`.

**Target Platform**: Linux containers (Docker Compose); GitHub Actions `ubuntu-latest`.

**Project Type**: existing web API + MVC solution (`CategoryProduct.slnx`).

**Constraints**: no secrets committed; migrations never auto-run outside the container; no behavior change for local runs or tests.

**Scale/Scope**: 2 Dockerfiles, 1 `.dockerignore`, 1 compose file, `.env.example`, 1 workflow, 1 new helper class, small edits to 2 `Program.cs`, 1 `appsettings.json`, `.gitignore`, 1 new test file.

## Constitution Check

| Principle | Check | Status |
|---|---|---|
| I. Consistent REST API Design | No endpoints added or changed. | PASS |
| II. Schema Changes Only Through EF Core Migrations | Startup applies only committed migrations via `MigrateAsync()`; no model or migration edits; auto-apply is opt-in via env in compose only. | PASS |
| III. Simplicity & YAGNI | Single static helper, constant retry values, no init container, no registry push, no image healthchecks that need extra tooling. Two small flags are the minimum to make HTTP-only containers work. | PASS |
| IV. Secrets & Credential Hygiene (NON-NEGOTIABLE) | `Jwt__Key` and SA password come from gitignored `.env`; `.env` added to `.gitignore` (currently missing); `.env.example` has obvious placeholders; compose uses `${VAR:?}`; no secret in `appsettings.json` or the workflow. Note: Dockerfiles/compose must not bake secrets into image layers (env only at run time). | PASS |
| V. API/MVC Separation of Concerns | MVC image builds only the MVC project and talks to the API over HTTP (`Api__BaseUrl=http://api:8080/`); no project reference added. Constitution text claiming MVC has its own EF/SQL is outdated (Known Gaps). | PASS |

No unjustified violations; Complexity Tracking not needed.

## Project Structure

### Documentation (this feature)

```text
specs/016-docker-ci/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── spec.md
├── contracts/
│   └── environment-and-compose.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
.dockerignore                                   # new
.env.example                                    # new (placeholders only)
.gitignore                                      # modify: add .env
docker-compose.yml                              # new
.github/workflows/ci.yml                        # new
CategoryProduct.API/
├── Dockerfile                                  # new
├── appsettings.json                            # modify: "Database": { "MigrateOnStartup": false }
├── Program.cs                                  # modify: gate UseHttpsRedirection; call migrator before app.Run()
└── Data/
    └── DatabaseMigrator.cs                     # new: static MigrateIfEnabledAsync(IServiceProvider, IConfiguration, ILogger)
CategoryProduct.MVC/
├── Dockerfile                                  # new
└── Program.cs                                  # modify: gate UseHttpsRedirection/UseHsts; Cookie:SecurePolicy override
CategoryProduct.API.Tests/
└── MigrationStartupTests.cs                    # new
```

**Structure Decision**: No new projects. Dockerfiles live beside each project; compose, `.dockerignore`, and workflow at repo root.

## Design Notes

- `DatabaseMigrator.MigrateIfEnabledAsync`: returns immediately if `configuration.GetValue<bool>("Database:MigrateOnStartup")` is false; creates a scope, resolves `ApplicationDbContext`, returns if `!db.Database.IsRelational()`; otherwise loops up to 10 attempts calling `db.Database.MigrateAsync()`, logging and waiting 3 s between failures, rethrowing on the last.
- `Program.cs` (API): `if (builder.Configuration.GetValue("Hosting:UseHttpsRedirection", true)) app.UseHttpsRedirection();` and `await DatabaseMigrator.MigrateIfEnabledAsync(app.Services, app.Configuration, app.Logger)` before `app.Run()` (top-level statements allow `await`).
- `Program.cs` (MVC): same redirection guard (also around `UseHsts()`); cookie `SecurePolicy` parsed from `Cookie:SecurePolicy` with `Enum.TryParse`, falling back to `Always`.
- Dockerfile (API) outline: `FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build`, `WORKDIR /src`, copy `CategoryProduct.API/CategoryProduct.API.csproj`, restore, copy `CategoryProduct.API/`, publish; `FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final`, `WORKDIR /app`, `ENV ASPNETCORE_HTTP_PORTS=8080`, `EXPOSE 8080`, `RUN mkdir -p /app/logs && chown $APP_UID /app/logs`, `COPY --from=build /app/publish .`, `USER $APP_UID`, `ENTRYPOINT`. MVC analogous.

## Complexity Tracking

*No entries.*
