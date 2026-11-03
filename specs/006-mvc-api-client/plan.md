# Implementation Plan: MVC Consumes the API

**Branch**: `006-mvc-api-client` (retroactive) | **Date**: 2026-11-03 | **Spec**: [spec.md](./spec.md)

## Summary

Replace direct EF Core access in `CategoryProduct.MVC` with a typed HTTP client against the API (Constitution Principle V: API/MVC separation).

## Technical Context

**Language/Version**: C# / .NET 10, ASP.NET Core MVC, `HttpClient` via `AddHttpClient<T>`.

**Configuration**: `Api:BaseUrl` in `CategoryProduct.MVC/appsettings.json`; `ConnectionStrings` removed.

## Files Touched

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
└── Views/Categories|Products/*.cshtml    # Create, Delete, Details, Index adjusted
```

## Key Decisions

- Typed client over scattered `HttpClient` calls: one place for URLs, JSON, and auth headers.
- Remove, rather than keep, the MVC `DbContext`, so the API is the only database owner.
- Product-count column dropped from Category Index rather than adding an extra API call per row (known behavior change).
