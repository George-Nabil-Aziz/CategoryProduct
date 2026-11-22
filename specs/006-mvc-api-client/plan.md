# Implementation Plan: MVC Consumes the API

**Branch**: `006-mvc-api-client` (retroactive) | **Date**: 2026-11-03 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/006-mvc-api-client/spec.md`

## Summary

Replace direct EF Core access in `CategoryProduct.MVC` with a typed HTTP client against `CategoryProduct.API` (Constitution Principle V: API/MVC separation). Controllers are rewritten on `CategoryProductApiClient`; MVC models become plain classes; the EF Core packages, `ApplicationDbContext` and connection strings are removed. Shipped in `c09170f`.

## Technical Context

**Language/Version**: C# / .NET 10, ASP.NET Core MVC

**Primary Dependencies**: `HttpClient` via `AddHttpClient<CategoryProductApiClient>` (framework, no new package). EF Core packages removed.

**Storage**: None in MVC (the API owns the database). No schema change, no migration.

**Configuration**: `Api:BaseUrl` in `CategoryProduct.MVC/appsettings.json` (default `https://localhost:7183/`); `ConnectionStrings` removed.

**Testing**: No automated MVC tests; manual verification (see `quickstart.md`). API tests (005) remain.

**Target Platform**: ASP.NET Core MVC web app.

**Project Type**: Web application (existing `CategoryProduct.MVC`).

**Performance Goals**: N/A; the Products Index makes extra paged calls to resolve category names.

**Constraints**: No reference from MVC to API types, `DbContext` or models (Principle V); pages must keep working except the removed product-count column.

**Scale/Scope**: 1 new service, 1 new models file, 2 controllers rewritten, 7 view files adjusted, 1 `DbContext` deleted, `Program.cs`/`appsettings.json`/`.csproj` edited.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Check | Status |
|---|---|---|
| I. Consistent REST API Design | MVC consumes the existing `api/Category`/`api/Product` routes and the 004 paged contract without changing them. | PASS |
| II. Schema Changes Only Through EF Core Migrations | No schema change. Removing MVC's `DbContext` also removes the second `DbContext` the principle cautions about. | PASS |
| III. Simplicity & YAGNI | One typed client class; no generated client, no repository layer, no caching. | PASS |
| IV. Secrets & Credential Hygiene (NON-NEGOTIABLE) | MVC connection strings (including its embedded server/database details) are removed from tracked config; no secret is added; `Api:BaseUrl` is a non-secret URL. | PASS |
| V. API/MVC Separation of Concerns | This feature implements it: MVC talks to the API only over HTTP and defines its own DTO-like records, with no project/type reference to the API. | PASS |

No unjustified violations; Complexity Tracking is not needed.

## Project Structure

### Documentation (this feature)

```text
specs/006-mvc-api-client/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── mvc-routes.md
│   └── api-calls.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
CategoryProduct.MVC/
├── CategoryProduct.MVC.csproj            # EF Core packages removed
├── Program.cs                            # AddHttpClient<CategoryProductApiClient>; EF registration removed
├── appsettings.json                      # ConnectionStrings -> Api:BaseUrl
├── Services/CategoryProductApiClient.cs  # new typed client (+ ApplyBearerToken hook)
├── Models/ApiModels.cs                   # new API response/paging models
├── Models/Category.cs, Product.cs        # simplified
├── Data/ApplicationDbContext.cs          # deleted
├── Controllers/CategoriesController.cs   # rewritten on the client
├── Controllers/ProductsController.cs     # rewritten on the client
└── Views/
    ├── Categories/Create.cshtml, Delete.cshtml, Index.cshtml
    └── Products/Create.cshtml, Delete.cshtml, Details.cshtml, Index.cshtml
```

**Structure Decision**: Keep the existing MVC project and folder layout; add one `Services/` folder for the client.

## Key Decisions

- Typed client over scattered `HttpClient` calls: one place for URLs, JSON, and auth headers.
- Remove, rather than keep, the MVC `DbContext`, so the API is the only database owner.
- Product-count column dropped from Category Index rather than adding an extra API call per row (known behavior change).
- Writes send no token at this commit; sign-in follows in 007.

See `research.md` for rationale and alternatives.

## Complexity Tracking

*No entries.*
