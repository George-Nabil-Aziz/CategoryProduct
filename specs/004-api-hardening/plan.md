# Implementation Plan: API Hardening

**Branch**: `004-api-hardening` (retroactive) | **Date**: 2026-11-03 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/004-api-hardening/spec.md`

## Summary

Four small hardening changes to `CategoryProduct.API`, shipped in `b17b5ee` (CategoryId validation, key handling, paging) and `3149197` (async refactor): reject products whose `CategoryId` does not exist, require the JWT signing key to come from user-secrets or `Jwt__Key` (fail fast at startup, nothing committed), wrap list responses in `PagedResponseDto<T>` with a 1..50 page-size clamp, and convert the API controllers to async EF Core calls.

## Technical Context

**Language/Version**: C# / .NET 10 (`CategoryProduct.API`)

**Primary Dependencies**: ASP.NET Core Web API, EF Core 10 + SQL Server. No new packages.

**Storage**: SQL Server via `ApplicationDbContext`. No schema change, no migration (see `data-model.md`).

**Testing**: xUnit integration tests added in the same commit as 005 (`dotnet test CategoryProduct.API.Tests`): `PostProduct_NonExistentCategory_Returns400`, `GetCategories_PageSizeAbove50_IsClamped`. Remaining scenarios verified manually (see `quickstart.md`).

**Target Platform**: Same hosting model as today.

**Project Type**: Web service (existing `CategoryProduct.API`); no new project.

**Performance Goals**: Async I/O so requests do not block threads; `pageSize` capped at 50.

**Constraints**: No key value in tracked configuration (Principle IV); 400 error shape must match the 002 `ValidationProblem` format; no service layer (Principle III).

**Scale/Scope**: 1 new DTO, 3 controllers edited, `Program.cs` startup check, 1 config key removed.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Check | Status |
|---|---|---|
| I. Consistent REST API Design | List endpoints gain the `page`/`pageSize` pagination the principle requires, bounded to 50 items per page; routes and verbs unchanged; failures use standard `ValidationProblem` (400). The list body shape change is breaking and is called out in `contracts/paged-list-endpoints.md`. | PASS |
| II. Schema Changes Only Through EF Core Migrations | No model change, so no migration needed. | PASS |
| III. Simplicity & YAGNI | The `CategoryId` check is one `AnyAsync` call in each controller action; no repository/service/validator layer. One generic record serves both lists. | PASS |
| IV. Secrets & Credential Hygiene (NON-NEGOTIABLE) | Removes the `Jwt:Key` entry from `appsettings.json` and refuses to start without a 32+ character key from user-secrets or `Jwt__Key`. No key value appears in this feature's files. | PASS |
| V. API/MVC Separation of Concerns | Change confined to `CategoryProduct.API`; the changed list contract is communicated through the API's own DTO type, and the MVC consumer was adapted separately in 006. | PASS |

No unjustified violations; Complexity Tracking is not needed.

## Project Structure

### Documentation (this feature)

```text
specs/004-api-hardening/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── paged-list-endpoints.md
│   └── product-categoryid-validation.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
CategoryProduct.API/
├── Program.cs                         # fail-fast Jwt:Key check; `public partial class Program` for tests
├── appsettings.json                   # Jwt:Key entry removed
├── Dtos/PagedResponseDto.cs           # new record
└── Controllers/
    ├── CategoryController.cs          # paging + async
    ├── ProductController.cs           # paging, CategoryId check on Post/Put, async
    └── AccountController.cs           # async
CategoryProduct.slnx                   # (test project added in the same commit, see 005)
```

**Structure Decision**: Single existing project, no new directories (Principle III).

## Key Decisions

- CategoryId existence is checked in the controller and surfaced with `ValidationProblem(ModelState)` to match the 002 validation response format.
- Key lives in user-secrets / `Jwt__Key` env var (Constitution Principle IV); the 32-character minimum is the chosen HMAC-SHA256 floor.
- Paging uses `Math.Clamp(pageSize, 1, 50)` and `OrderBy(Id)`; the envelope is a generic record.
- Async conversion kept in the same controllers; no service layer added (Principle III).

See `research.md` for rationale and alternatives.

## Complexity Tracking

*No entries.*
