# Implementation Plan: API Integration Tests

**Branch**: `005-api-integration-tests` (retroactive) | **Date**: 2026-11-03 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/005-api-integration-tests/spec.md`

## Summary

Add an xUnit project, `CategoryProduct.API.Tests`, that boots the real API in process with `WebApplicationFactory<Program>`, swaps SQL Server for the EF Core InMemory provider, and exercises login, write protection, paging and validation over HTTP. Delivered in `b17b5ee` (5 tests); the Admin/plain-user seeding and the 403 test came with `f868aa7` (6 tests total).

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`)

**Primary Dependencies**: xUnit 2.9.3, `Microsoft.AspNetCore.Mvc.Testing` 10.0.10, `Microsoft.EntityFrameworkCore.InMemory` 10.0.10, `Microsoft.NET.Test.Sdk`, `coverlet.collector`, `xunit.runner.visualstudio`.

**Storage**: EF Core InMemory (no SQL Server, no migration, no schema change).

**Testing**: `dotnet test CategoryProduct.API.Tests`.

**Target Platform**: Developer machine / CI with the .NET 10 SDK.

**Project Type**: Test project referencing `CategoryProduct.API`.

**Performance Goals**: N/A (tests run in memory).

**Constraints**: No real secrets committed (Principle IV); the fail-fast key check from 004 must pass using a test-only key; tests must not depend on each other's writes.

**Scale/Scope**: 1 new project (3 files), 1 `.slnx` line, 1 `Program.cs` line (from 004); 6 tests.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Check | Status |
|---|---|---|
| I. Consistent REST API Design | Tests only consume existing endpoints and assert standard status codes (200, 400, 401, 403); no API shape change. | PASS |
| II. Schema Changes Only Through EF Core Migrations | No schema change; InMemory is used only in tests and never replaces migrations for real databases. | PASS |
| III. Simplicity & YAGNI | One test class, one factory, no mocking framework, no test base classes or helper libraries beyond a private `LoginAsync` helper. | PASS |
| IV. Secrets & Credential Hygiene (NON-NEGOTIABLE) | JWT key and test password are test-only constants confined to the test project; seeded passwords are hashed with `PasswordHasher<User>`; no real key is read or committed. | PASS |
| V. API/MVC Separation of Concerns | The test project references only `CategoryProduct.API` and tests it over HTTP; `CategoryProduct.MVC` is untouched. | PASS |

The constitution's Development Workflow ("Until an automated test suite exists... changes MUST be manually verified") is now satisfied for the API by automation; the MVC remains manual.

## Project Structure

### Documentation (this feature)

```text
specs/005-api-integration-tests/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── test-scenarios.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
CategoryProduct.API.Tests/
├── CategoryProduct.API.Tests.csproj
├── ApiFactory.cs        # WebApplicationFactory: InMemory DB, test JWT key, seeded users and categories
└── ApiTests.cs          # 5 [Fact] tests at b17b5ee, 6 from f868aa7
CategoryProduct.slnx                 # test project added
CategoryProduct.API/Program.cs       # `public partial class Program` (see 004)
```

**Structure Decision**: A separate test project beside the API (not inside it), referenced to the API project only.

## Key Decisions

- WebApplicationFactory over unit tests: verifies the real auth pipeline, routing, and validation together.
- InMemory EF provider keeps tests fast and database-free; the SQL Server `DbContext` registration is replaced in the factory.
- Roles added to seeded users in `f868aa7` (`tester` Admin, `plainuser` User) so the 403 case can be tested.

See `research.md` for rationale and alternatives.

## Complexity Tracking

*No entries.*
