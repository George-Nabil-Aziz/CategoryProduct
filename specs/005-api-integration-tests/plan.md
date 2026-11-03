# Implementation Plan: API Integration Tests

**Branch**: `005-api-integration-tests` (retroactive) | **Date**: 2026-11-03 | **Spec**: [spec.md](./spec.md)

## Summary

Add an xUnit project that boots the real API in process and exercises it over HTTP.

## Technical Context

**Language/Version**: C# / .NET 10, xUnit, `Microsoft.AspNetCore.Mvc.Testing`, EF Core InMemory provider.

**Project Type**: Test project referencing `CategoryProduct.API`.

## Files Touched

```text
CategoryProduct.API.Tests/
├── CategoryProduct.API.Tests.csproj
├── ApiFactory.cs        # WebApplicationFactory: InMemory DB, test JWT key, seeded users and categories
└── ApiTests.cs          # 6 [Fact] tests
CategoryProduct.slnx                 # test project added
CategoryProduct.API/Program.cs       # `public partial class Program` (see 004)
```

## Key Decisions

- WebApplicationFactory over unit tests: verifies the real auth pipeline, routing, and validation together.
- InMemory EF provider keeps tests fast and database-free; the SQL Server `DbContext` registration is replaced in the factory.
- Roles added to seeded users in `f868aa7` so the 403 case can be tested.
