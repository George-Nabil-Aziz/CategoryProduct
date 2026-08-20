# Implementation Plan: Request Validation

**Branch**: `002-request-validation` | **Date**: 2026-08-20 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/002-request-validation/spec.md`

## Summary

Add request validation to `CategoryProduct.API` so that `POST`/`PUT` requests for `Category` and
`Product` are rejected — with field-level error detail and no persisted data — when they violate
the rules already specified (required/length-bounded names; non-negative price and quantity;
positive category reference). The chosen approach is to declare
`System.ComponentModel.DataAnnotations` validation attributes directly on the
`CategoryRequestDto`/`ProductRequestDto` record properties and rely on ASP.NET Core's existing
automatic `[ApiController]` model-validation behavior (already active on both controllers) to
short-circuit invalid requests with `400 Bad Request` before the action body runs. No new
dependency, service layer, or controller logic is introduced.

## Technical Context

**Language/Version**: C# / .NET 10 (matches existing `CategoryProduct.API.csproj`)

**Primary Dependencies**: ASP.NET Core Web API (`Microsoft.AspNetCore.OpenApi`,
`Swashbuckle.AspNetCore`), Entity Framework Core 10 + SQL Server — all already referenced; no new
package required. Validation uses the framework-provided `System.ComponentModel.DataAnnotations`
namespace plus ASP.NET Core's built-in automatic HTTP 400 response for `[ApiController]`.

**Storage**: SQL Server via `ApplicationDbContext` (`CategoryProduct.API/Data`) — unchanged; this
feature does not touch schema, models, or migrations.

**Testing**: No automated test project exists in `CategoryProduct.slnx`. Per constitution
Development Workflow, verification is manual against the running API via Swagger UI (see
`quickstart.md`).

**Target Platform**: ASP.NET Core Web API, same hosting model as today (no platform change).

**Project Type**: Web service (existing `CategoryProduct.API` project); no new project created.

**Performance Goals**: N/A beyond existing behavior — validation attributes run in-process during
model binding, adding negligible overhead to already-synchronous EF Core calls.

**Constraints**: Must not modify Response DTOs; must not add authentication/authorization; must
apply only to `POST`/`PUT` on `Category` and `Product`; must stay consistent with existing
controller/DTO style (plain records, no service layer).

**Scale/Scope**: 2 DTOs (`CategoryRequestDto`, `ProductRequestDto`), 4 endpoints (`POST`/`PUT` ×
`Category`/`Product`), ~7 validation rules total.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Check | Status |
|---|---|---|
| I. Consistent REST API Design | Feature only adds validation ahead of existing `POST`/`PUT` actions; routes, verbs, and `ActionResult`-based responses (now including the framework's standard `400 ValidationProblemDetails`) are unchanged. | PASS |
| II. Schema Changes Only Through EF Core Migrations | No `Model` or schema change; no migration needed. | PASS |
| III. Simplicity & YAGNI | No repository/service/validation-library layer introduced. Validation is declared on the DTOs themselves and enforced by capability the controllers already have (`[ApiController]` automatic model validation) — the constitution's own example of a "genuine cross-cutting concern" that justifies *some* structure, satisfied here by the framework's built-in mechanism rather than a new abstraction. | PASS |
| IV. Secrets & Credential Hygiene | Not applicable — no secrets, credentials, or passwords involved in this feature. | PASS (N/A) |
| V. API/MVC Separation of Concerns | Change is confined to `CategoryProduct.API`'s own DTOs; `CategoryProduct.MVC` is not referenced or modified. | PASS |

No violations — Complexity Tracking table below is not needed.

## Project Structure

### Documentation (this feature)

```text
specs/002-request-validation/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
│   ├── category-api.md
│   └── product-api.md
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

No new projects, folders, or files beyond edits to the two existing DTO files. This feature
modifies only:

```text
CategoryProduct.API/
└── Dtos/
    ├── CategoryRequestDto.cs   # add DataAnnotations attributes on Name
    └── ProductRequestDto.cs    # add DataAnnotations attributes on Name, Price, Quantity, CategoryId
```

`CategoryController.cs` and `ProductController.cs` require no code changes — ASP.NET Core's
existing `[ApiController]` automatic model-validation behavior applies to the annotated DTOs
without any action-method changes.

**Structure Decision**: Single existing project (`CategoryProduct.API`), no new source directories.
This matches Constitution Principle III: the smallest change that satisfies the requirement, using
only the two files that already define the shape being validated.

## Complexity Tracking

*No entries — Constitution Check reported no violations.*
