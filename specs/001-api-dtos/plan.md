# Implementation Plan: API DTOs for Category & Product

**Branch**: `001-api-dtos` | **Date**: 2026-08-19 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/001-api-dtos/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the execution workflow.

## Summary

Replace direct exposure of the `Category` and `Product` EF Core entities in
`CategoryProduct.API` request/response bodies with dedicated DTOs. Each entity gets one response
DTO (its own fields only, no navigation properties) and one shared request DTO (used for both
create and update, since the field sets are identical per the spec). `CategoryController` and
`ProductController` are updated to map to/from these DTOs; routes, HTTP verbs, status codes,
pagination, and the database schema are all unchanged.

## Technical Context

**Language/Version**: C# 13 / .NET 10 (matches existing `CategoryProduct.API.csproj`)

**Primary Dependencies**: ASP.NET Core Web API (existing), Swashbuckle.AspNetCore for
Swagger/OpenAPI (existing, will pick up new DTO shapes automatically), Entity Framework Core 10 +
SQL Server (existing, unchanged by this feature — no new packages introduced)

**Storage**: SQL Server via EF Core — unaffected; no schema or migration changes (FR-010)

**Testing**: No automated test project exists yet for `CategoryProduct.API`. Per the constitution's
Development Workflow, changes are manually verified against the running API via Swagger UI until
an automated test suite exists; this feature does not introduce one (out of scope per the feature
description's explicit scope limit)

**Target Platform**: ASP.NET Core Web API (Kestrel), same hosting model as today

**Project Type**: Web service (single existing project modified: `CategoryProduct.API`)

**Performance Goals**: N/A — this is a request/response shape change, not a performance-sensitive
path; existing pagination (`page`/`pageSize`) on list endpoints is preserved as-is

**Constraints**: No new endpoints, fields, validation rules, or authentication (per feature
description); no schema/migration changes (FR-010); existing routes/verbs/status codes preserved
(FR-009)

**Scale/Scope**: 2 entities (`Category`, `Product`), 2 controllers, 4 new DTO types
(`CategoryResponseDto`, `CategoryRequestDto`, `ProductResponseDto`, `ProductRequestDto`)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Check | Result |
|---|---|---|
| I. Consistent REST API Design | Routes (`api/[controller]`), HTTP verbs, pagination params, and `Ok`/`NotFound` status codes are all preserved (FR-009); only body shapes change | PASS |
| II. Schema Changes Only Through EF Core Migrations | No entity/model changes, no schema change, no migration needed (FR-010) | PASS |
| III. Simplicity & YAGNI | DTOs mapped manually (no AutoMapper/Mapster dependency added) for a 2-entity, 4-DTO surface; one shared request DTO per entity (not separate Create/Update DTOs) since create and update accept identical field sets — smallest change that satisfies the requirement | PASS |
| IV. Secrets & Credential Hygiene | Not touched by this feature — no config/secrets involved | N/A |
| V. API/MVC Separation of Concerns | New DTOs live entirely inside `CategoryProduct.API`; `CategoryProduct.MVC` is untouched and keeps its own independent data access | PASS |

No violations — Complexity Tracking table below is not needed and has been omitted.

**Post-Phase 1 re-check**: Design artifacts (`research.md`, `data-model.md`, `contracts/`,
`quickstart.md`) confirm the decisions above without introducing anything new — manual mapping,
one shared request DTO per entity, DTOs confined to `CategoryProduct.API/Dtos/`, no schema change,
no new routes/verbs/status codes. All gates remain PASS.

## Project Structure

### Documentation (this feature)

```text
specs/001-api-dtos/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
CategoryProduct.API/
├── Controllers/
│   ├── CategoryController.cs   # modified: map to/from Category DTOs
│   └── ProductController.cs    # modified: map to/from Product DTOs
├── Dtos/                       # new
│   ├── CategoryResponseDto.cs
│   ├── CategoryRequestDto.cs
│   ├── ProductResponseDto.cs
│   └── ProductRequestDto.cs
├── Models/                     # unchanged (Category.cs, Product.cs, User.cs)
├── Data/                       # unchanged (ApplicationDbContext.cs)
└── Program.cs                  # unchanged

CategoryProduct.MVC/            # unaffected — separate project, own data access
```

**Structure Decision**: Single existing project modified in place
(`CategoryProduct.API`). No new project is introduced. DTOs live in a new
`CategoryProduct.API/Dtos/` folder, sitting alongside the existing `Controllers/`, `Models/`, and
`Data/` folders, following the same flat-namespace convention already used in this project. No
automated `tests/` directory is added — per Technical Context, this project has none today and
this feature does not introduce one.

## Complexity Tracking

*No Constitution Check violations — this section is intentionally empty.*
