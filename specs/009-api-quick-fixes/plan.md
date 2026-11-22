# Implementation Plan: API Quick Fixes

**Branch**: `009-api-quick-fixes` | **Date**: 2026-11-03 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/009-api-quick-fixes/spec.md`

**Status**: Implemented (`59e6836`, `94b68a8`, `d643b40`, `89d69df`)

## Summary

Four small changes: pin `System.Security.Cryptography.Pkcs` 10.0.14; register a Bearer security
scheme in Swashbuckle; return 409 ProblemDetails when deleting a category that has products (and show
it in MVC); add an ASP.NET Core fixed-window rate limiter named `login` on the login action (and show
a 429 message in MVC).

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: ASP.NET Core Web API, Swashbuckle.AspNetCore 10.2.3 (`Microsoft.OpenApi`
types), built-in `Microsoft.AspNetCore.RateLimiting`, new explicit `System.Security.Cryptography.Pkcs`
10.0.14

**Storage**: SQL Server via EF Core; no schema change (one extra `AnyAsync` query on delete)

**Testing**: xUnit in `CategoryProduct.API.Tests` (WebApplicationFactory via `ApiFactory`); MVC has no
test project

**Target Platform**: ASP.NET Core web hosts (API and MVC)

**Project Type**: Web service + MVC client

**Performance Goals**: N/A

**Constraints**: Login limit configurable via `RateLimiting:LoginPermitLimit` (default 5, 1-minute
window, `QueueLimit = 0`, 429 rejection); success-path shapes unchanged

**Scale/Scope**: 1 csproj line, 1 Swagger block, 1 controller guard, 1 limiter policy + attribute,
4 small MVC edits, 2 tests

## Constitution Check

| Principle | Check | Status |
|---|---|---|
| I. Consistent REST API Design | 409 is a standard status returned through `Problem(...)` (ProblemDetails); no route/verb change; login stays `POST api/account/login`. | PASS |
| II. Schema Changes Only Through EF Core Migrations | No model change, no migration needed. | PASS |
| III. Simplicity & YAGNI | Guard is one inline `AnyAsync` in the controller; limiter uses the framework's built-in middleware; no new layers. | PASS |
| IV. Secrets & Credential Hygiene | No secret added. `ApiFactory` sets a test-only `Jwt:Key` literal in test code (not a deployment secret). Rate limiting reduces credential-guessing risk. | PASS |
| V. API/MVC Separation of Concerns | MVC reacts to API status codes (409, 429) over HTTP only; no shared types. | PASS |

## Project Structure

### Documentation (this feature)

```text
specs/009-api-quick-fixes/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── spec.md
├── contracts/
│   ├── category-delete-conflict.md
│   ├── login-rate-limit.md
│   └── swagger-bearer-security.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
CategoryProduct.API/
├── CategoryProduct.API.csproj             # + System.Security.Cryptography.Pkcs 10.0.14
├── Program.cs                             # AddSwaggerGen Bearer scheme; AddRateLimiter "login"; UseRateLimiter
└── Controllers/
    ├── AccountController.cs               # [EnableRateLimiting("login")] on Login
    └── CategoryController.cs              # 409 guard in Delete
CategoryProduct.API.Tests/
├── ApiFactory.cs                          # LoginPermitLimit (1000) -> RateLimiting:LoginPermitLimit
└── ApiTests.cs                            # DeleteCategory_WithProducts_Returns409, SendAsync helper, RateLimitTests
CategoryProduct.MVC/
├── Controllers/AccountController.cs       # 429 message
├── Controllers/CategoriesController.cs    # AddErrors shows Conflict
├── Models/ApiModels.cs                    # ApiResult.Conflict
└── Services/CategoryProductApiClient.cs   # LoginResult.TooManyRequests sentinel; 409 handling + ConflictProblem
```

**Structure Decision**: Edit existing files only; no new source files.

## Complexity Tracking

*No entries - no constitution violations.*
