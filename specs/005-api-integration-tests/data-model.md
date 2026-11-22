# Data Model: API Integration Tests

**No schema change.** No model class, DTO or migration was added or changed by this feature. The test project only uses existing models in an InMemory database.

## Test fixture data (`CategoryProduct.API.Tests/ApiFactory.cs`)

Constants exposed by `ApiFactory`:

| Constant | Value | Introduced |
|----------|-------|------------|
| `UserName` | `tester` | `b17b5ee` |
| `Password` | test-only constant (not repeated here) | `b17b5ee` |
| `SeedCategoryCount` | `3` | `b17b5ee` |
| `PlainUserName` | `plainuser` | `f868aa7` |

Seeded rows (InMemory database, new GUID name per factory instance):

| Entity | Rows | Notes |
|--------|------|-------|
| `User` | `tester` | `PasswordHash` produced by `PasswordHasher<User>.HashPassword`. In `b17b5ee` the `User` had no `Role`; from `f868aa7` `Role = "Admin"` |
| `User` | `plainuser` | Added in `f868aa7`; `Role = "User"`, same password |
| `Category` | 3 | Named `Category 1` .. `Category 3` |
| `Product` | none | |

## Host settings injected by the factory

| Key | Value |
|-----|-------|
| `Jwt:Key` | test-only string of 32+ characters (not repeated here) |
| `Jwt:Issuer` | `CategoryProduct.API` |
| `Jwt:Audience` | `CategoryProduct.Client` |
| `ConnectionStrings:DefaultConnection` | `unused` |

## Project

`CategoryProduct.API.Tests/CategoryProduct.API.Tests.csproj`: `net10.0`, `IsPackable=false`, packages `Microsoft.AspNetCore.Mvc.Testing`, `Microsoft.EntityFrameworkCore.InMemory`, `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, `coverlet.collector`; project reference to `CategoryProduct.API`; `<Using Include="Xunit" />`.
