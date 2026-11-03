# Implementation Plan: Role-Based Authorization

**Branch**: `008-role-authorization` (retroactive) | **Date**: 2026-11-03 | **Spec**: [spec.md](./spec.md)

## Summary

Add a `Role` to users, emit it as a JWT claim, require `Admin` for API and MVC writes, and surface 403s in MVC with an Access Denied page.

## Technical Context

**Language/Version**: C# / .NET 10, EF Core 10 + SQL Server, ASP.NET Core role-based `[Authorize]`.

**Storage**: One new column `User.Role` via EF Core migration `20261103173834_AddUserRole` (Principle II).

**Testing**: API integration test added to 005 (6th test); MVC not automated.

## Files Touched

```text
CategoryProduct.API/
├── Models/User.cs                          # Role property
├── Data/ApplicationDbContext.cs            # Role config + seed (testuser -> Admin)
├── Migrations/20261103173834_AddUserRole.cs (+ designer/snapshot)
├── Dtos/LoginResponseDto.cs                # + Role
└── Controllers/AccountController.cs        # Role claim in JWT
    Controllers/CategoryController.cs, ProductController.cs   # [Authorize(Roles = "Admin")] on writes
CategoryProduct.API.Tests/ApiFactory.cs, ApiTests.cs          # plain user + 403 test
CategoryProduct.MVC/
├── Program.cs                              # AccessDeniedPath -> /Account/AccessDenied
├── Controllers/AccountController.cs        # role claim in cookie, AccessDenied action
├── Controllers/CategoriesController.cs, ProductsController.cs # Admin roles, 403 handling
├── Models/ApiModels.cs                     # LoginResult + Role
├── Services/CategoryProductApiClient.cs    # carries Role
└── Views/Account/AccessDenied.cshtml; Views/Categories|Products/Index.cshtml, Details.cshtml # hide write links
```

## Key Decisions

- Single string role on `User` rather than ASP.NET Identity roles tables (Principle III).
- Role travels in the JWT and the MVC cookie; no per-request database lookup, so role changes apply at next login.
- 403 and 401 handled differently in MVC: 403 shows Access Denied, 401 signs out.
- Migration promotes `testuser` to Admin so existing demo flows keep working.
