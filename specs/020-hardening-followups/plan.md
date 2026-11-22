# Implementation Plan: Hardening Follow-ups

**Branch**: `020-hardening-followups` | **Date**: 2026-11-22 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/020-hardening-followups/spec.md`

## Summary

Four independent changes closing Known Gaps of 015-019: (1) a new test project run against SQL Server in a new CI job, (2) a refresh-token reuse grace window in `AccountController.Refresh`, (3) 409 handling in the two product image endpoints, (4) optional persisted Data Protection keys for the MVC app plus compose/Dockerfile support. No schema change, no new runtime dependency in the shipped apps.

## Technical Context

**Language/Version**: C# / .NET 10; EF Core 10 (SQL Server provider already referenced by `CategoryProduct.API.csproj`).

**Primary Dependencies**: existing only. New test project uses the package versions of `CategoryProduct.API.Tests`: `Microsoft.AspNetCore.Mvc.Testing` 10.0.10, `Microsoft.NET.Test.Sdk` 17.14.1, `xunit` 2.9.3, `xunit.runner.visualstudio` 3.1.4, `coverlet.collector` 6.0.4 (no InMemory provider). `Microsoft.Data.SqlClient` and the SQL Server EF provider flow transitively through the API project reference. MVC uses `Microsoft.AspNetCore.DataProtection` from the shared framework.

**Storage**: SQL Server 2022 in CI (service container). No migration.

**Testing**: xunit. InMemory tests in `CategoryProduct.API.Tests` for US2/US3; real SQL Server tests in `CategoryProduct.API.SqlServerTests` for US1.

**Target Platform**: GitHub Actions `ubuntu-latest`, Docker Compose.

**Project Type**: existing web API + MVC; one new test project.

**Constraints**: the new project is NOT in `CategoryProduct.slnx`; existing CI jobs unchanged; no secrets committed other than an obviously test-only CI SA password.

**Scale/Scope**: 1 new project (~4 files + tests), 1 workflow job, 2 controller edits, 1 test-factory property, 1 MVC `Program.cs` edit, 1 Dockerfile line, 3 compose lines, 5 Known Gaps edits.

## Constitution Check

| Principle | Check | Status |
|---|---|---|
| I. Consistent REST API Design | No route, verb or response-shape change. New 409 responses reuse the existing `ConcurrencyConflict()` `ProblemDetails` used by `ProductController` Update/Delete. Grace path returns the same empty 401. | PASS |
| II. Schema Changes Only Through EF Core Migrations | No model change, no migration. The SQL tests assert `HasPendingModelChanges()` is false and that all migrations apply on an empty database, which guards this principle. | PASS |
| III. Simplicity & YAGNI | Grace window is an `IConfiguration` read and one `if`, no options class, no new service. Image fix is two `catch` blocks. Data Protection is one conditional block. The test factory is the minimum needed (no shared base library between the two test projects; it deliberately duplicates the small `ApiFactory` setup). | PASS |
| IV. Secrets & Credential Hygiene | The only committed credential-like value is the CI SA password for an ephemeral, unexposed container; it is labeled test-only in the workflow and in Assumptions. JWT key in tests is a test-only literal, same as `ApiFactory`. `SQLSERVER_TEST_CONNECTION` for local runs is supplied by the developer, never committed. Data Protection keys are unencrypted on a volume (declared Known Gap). | PASS (documented) |
| V. API/MVC Separation | MVC change touches only MVC files and configuration; it does not reference API types. The SQL test project references the API (like the existing test project), never MVC. | PASS |

## Project Structure

### Documentation

```text
specs/020-hardening-followups/
  spec.md  plan.md  research.md  data-model.md  quickstart.md  tasks.md
  contracts/{refresh-grace.md, image-endpoints.md, ci-and-config.md}
  checklists/requirements.md
```

### Source changes

```text
CategoryProduct.API.SqlServerTests/                 (NEW, not in slnx)
  CategoryProduct.API.SqlServerTests.csproj
  AssemblyInfo.cs                 (DisableTestParallelization)
  SqlServerApiFactory.cs          (env var, unique DB, MigrateAsync, drop on dispose, helpers)
  MigrationTests.cs
  CheckoutSqlServerTests.cs       (insufficient stock rollback, parallel same-cart checkout)
  StockMovementSqlServerTests.cs  (parallel movements)
  AdminDemotionSqlServerTests.cs
  RefreshTokenSqlServerTests.cs
  ProductDeleteSqlServerTests.cs  (SetNull, cascade)
  CartConstraintSqlServerTests.cs (unique index)
.github/workflows/ci.yml                            (new job sqlserver-tests)

CategoryProduct.API/Controllers/AccountController.cs   (grace in Refresh; needs IConfiguration injected)
CategoryProduct.API/appsettings.json                   (Jwt:RefreshReuseGraceSeconds = 10)
CategoryProduct.API.Tests/ApiFactory.cs                (virtual RefreshReuseGraceSeconds => 0)
CategoryProduct.API.Tests/RefreshGraceTests.cs         (NEW)

CategoryProduct.API/Controllers/ProductController.cs   (UploadImage, DeleteImage catch)
CategoryProduct.API.Tests/ProductImageConcurrencyTests.cs (NEW)

CategoryProduct.MVC/Program.cs
CategoryProduct.MVC/Dockerfile
docker-compose.yml
```

Parallel work split (no shared files): **US1** = `CategoryProduct.API.SqlServerTests/**` + `.github/workflows/ci.yml`; **US2** = `AccountController.cs`, `appsettings.json`, `ApiFactory.cs`, `RefreshGraceTests.cs`; **US3** = `ProductController.cs`, `ProductImageConcurrencyTests.cs`; **US4** = MVC `Program.cs`, MVC `Dockerfile`, `docker-compose.yml`. Polish edits the five older `spec.md` files only.

## Design notes

**US2.** `AccountController` currently takes `ApplicationDbContext` and `TokenService`; inject `IConfiguration` and read `Jwt:RefreshReuseGraceSeconds` (default 10) per request. In the `stored.RevokedAtUtc != null` branch, before loading the active family: `if (grace > 0 && stored.ReplacedByTokenHash != null && now - stored.RevokedAtUtc.Value < TimeSpan.FromSeconds(grace)) return Unauthorized();`. Nothing is saved on that path.

**US3.** `UploadImage`: replace `catch { delete; throw; }` with `catch (DbUpdateConcurrencyException) { delete new file; return ConcurrencyConflict(); }` followed by the existing generic `catch { delete; throw; }`. `DeleteImage`: wrap `SaveChangesAsync` in `try/catch (DbUpdateConcurrencyException)` returning `ConcurrencyConflict()`; the `_storage.Delete(fileName)` stays after a successful save.

**US4.** In MVC `Program.cs`, before `AddAuthentication`: read `DataProtection:KeysPath`; if not whitespace, `builder.Services.AddDataProtection().SetApplicationName("CategoryProduct.MVC").PersistKeysToFileSystem(new DirectoryInfo(path));`.

**US1 factory.** Override `ConfigureWebHost`: set `Jwt:*` and rate-limit settings, remove the API's `DbContextOptions` descriptors (same filter as `ApiFactory`), register `AddDbContext` with `UseSqlServer(connectionString)`, then build a temporary provider and `MigrateAsync().GetAwaiter().GetResult()` so the hosted `RefreshTokenCleanupWorker` never starts against an unmigrated database. `Dispose(bool)` runs `EnsureDeletedAsync`. Helpers: `CreateUserAsync(name, role)`, `LoginAsync(name)`, `AuthClient(token)`, `WithDbAsync(Func<ApplicationDbContext, Task>)`, `SeedProductAsync(quantity)`.

**CI job** (see `contracts/ci-and-config.md` for the full YAML): independent of `build-test`; `runs-on: ubuntu-latest`; workflow-level `env.SQLSERVER_CI_SA_PASSWORD`.

## Complexity Tracking

No constitution violations to justify.
