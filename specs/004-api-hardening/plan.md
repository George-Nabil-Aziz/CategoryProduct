# Implementation Plan: API Hardening

**Branch**: `004-api-hardening` (retroactive) | **Date**: 2026-11-03 | **Spec**: [spec.md](./spec.md)

## Summary

Four small hardening changes to `CategoryProduct.API`, shipped in `b17b5ee` (validation, key handling, paging) and `3149197` (async refactor).

## Technical Context

**Language/Version**: C# / .NET 10, ASP.NET Core Web API, EF Core 10 + SQL Server. No new packages, no schema change.

**Testing**: Covered by the integration tests in 005 (400 on bad category, paging clamp).

## Files Touched

```text
CategoryProduct.API/
├── Program.cs                         # fail-fast Jwt:Key check; `public partial class Program` for tests
├── appsettings.json                   # Jwt:Key entry removed
├── Dtos/PagedResponseDto.cs           # new record
└── Controllers/
    ├── CategoryController.cs          # paging + async
    ├── ProductController.cs           # paging, CategoryId check on Post/Put, async
    └── AccountController.cs           # async
```

## Key Decisions

- CategoryId existence is checked in the controller and surfaced with `ValidationProblem(ModelState)` to match the 002 validation response format.
- Key lives in user-secrets / `Jwt__Key` env var (Constitution Principle IV); the 32-char minimum is the HMAC-SHA256 safe floor.
- Paging uses `Math.Clamp(pageSize, 1, 50)` and `OrderBy(Id)`; envelope is a generic record.
- Async conversion kept in the same controllers; no service layer added (Principle III).
