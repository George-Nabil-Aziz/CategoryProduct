# Implementation Plan: Search, Filter and Sort

**Branch**: `010-search-filter-sort` | **Date**: 2026-11-03 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/010-search-filter-sort/spec.md`

**Status**: Implemented (`aebb0fc`)

## Summary

Extend `GET /api/product` and `GET /api/category` with optional query parameters, build the filtered
`IQueryable` before counting and paging, and expose the options in the MVC Products/Categories index
pages through the API client.

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: ASP.NET Core Web API, EF Core 10 + SQL Server; MVC uses `HttpClient` through
`CategoryProductApiClient`

**Storage**: SQL Server; no schema change

**Testing**: xUnit in `CategoryProduct.API.Tests` (4 new tests; `ApiFactory` seeds 3 products); no MVC
tests

**Target Platform**: ASP.NET Core web hosts

**Project Type**: Web service + MVC client

**Performance Goals**: N/A (substring search uses `ToLower().Contains`, no index)

**Constraints**: Response shape and paging rules unchanged; `pageSize` clamped 1-50

**Scale/Scope**: 2 controller actions extended, 1 MVC client method pair, 2 MVC controller actions,
2 views, 4 tests

## Constitution Check

| Principle | Check | Status |
|---|---|---|
| I. Consistent REST API Design | Same routes (`api/product`, `api/category`) and verb; pagination (`page`, `pageSize`) retained; invalid range returns `400` through `ValidationProblem`. | PASS |
| II. Schema Changes Only Through EF Core Migrations | No model or schema change. | PASS |
| III. Simplicity & YAGNI | Inline query composition in the controllers; no specification/repository layer. | PASS |
| IV. Secrets & Credential Hygiene | No secrets touched. | PASS |
| V. API/MVC Separation of Concerns | MVC only builds query strings for the API and renders results; no shared types. | PASS |

## Project Structure

### Documentation (this feature)

```text
specs/010-search-filter-sort/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── spec.md
├── contracts/
│   ├── products-list-query.md
│   ├── categories-list-query.md
│   └── mvc-list-pages.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
CategoryProduct.API/Controllers/
├── ProductController.cs                   # search, categoryId, minPrice, maxPrice, sortBy, desc
└── CategoryController.cs                  # search
CategoryProduct.API.Tests/
├── ApiFactory.cs                          # seeds Widget A / Widget B / Gadget
└── ApiTests.cs                            # 4 new tests
CategoryProduct.MVC/
├── Controllers/ProductsController.cs      # Index accepts filters, fills ViewBag
├── Controllers/CategoriesController.cs    # Index accepts search
├── Services/CategoryProductApiClient.cs   # GetProductsAsync / GetCategoriesAsync pass parameters
└── Views/
    ├── Products/Index.cshtml              # filter form + paging links keep filters
    └── Categories/Index.cshtml            # search form + paging links keep search
```

**Structure Decision**: Edit existing files; no new source files.

## Complexity Tracking

*No entries - no constitution violations.*
