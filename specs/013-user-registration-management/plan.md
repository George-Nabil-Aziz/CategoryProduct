# Implementation Plan: User Registration & Management

**Branch**: `013-user-registration-management` | **Date**: 2026-11-22 (retroactive) | **Spec**: [spec.md](./spec.md)

**Status**: Implemented, commit `025d652`

## Summary

Add `POST api/account/register` (rate limited, role `User`), an admin-only `api/users` controller
(paged list with search, role change with last-admin protection) to `CategoryProduct.API`, and
matching Register and Users pages in `CategoryProduct.MVC` that talk to the API over HTTP.

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: ASP.NET Core Web API/MVC, EF Core 10 + SQL Server, JwtBearer (existing),
`PasswordHasher<User>` (shared framework), ASP.NET Core rate limiting (framework). No new package.

**Storage**: SQL Server via `ApplicationDbContext`. **No schema change; no migration** - the unique
index `IX_Users_UserName` (migration `20260820083821_UpdateUsersTableForAuth`) and the `Role`
column (`20261103173834_AddUserRole`) already existed.

**Testing**: xUnit + `WebApplicationFactory` (`ApiFactory`, EF Core in-memory). 7 tests in
`UsersTests.cs`. MVC has no automated tests.

**Target Platform**: ASP.NET Core Web API + MVC site

**Project Type**: Two existing web projects

**Constraints**: Hash passwords (Principle IV); MVC must not reference API internals (Principle V);
never return hashes.

**Scale/Scope**: 3 API endpoints, 3 DTOs, 1 rate-limit policy, 2 MVC controllers/actions groups,
3 views, 1 view model, 3 API-client methods, 7 new tests plus a fixture tweak.

## Constitution Check

| Principle | Check | Status |
|---|---|---|
| I. Consistent REST API Design | `api/users` uses `api/[controller]`-style plural route, `GET` list with `page`/`pageSize` pagination (clamped 1-50), standard status codes (`200/201/400/404/409`). `PUT {id}/role` is a sub-resource update. Register is an action endpoint under `api/Account` like login. | PASS |
| II. Schema Changes Only Through EF Core Migrations | No schema change, hence no migration; existing index and column verified in migrations. | PASS |
| III. Simplicity & YAGNI | Controllers use `ApplicationDbContext` directly; roles are two fixed strings; no Identity framework or service layer. | PASS |
| IV. Secrets & Credential Hygiene | Passwords hashed via `PasswordHasher<User>`; hash never returned; no secrets added. | PASS |
| V. API/MVC Separation of Concerns | MVC uses `CategoryProductApiClient` over HTTP with its own models; no API project reference. | PASS |

Development Workflow: automated API tests exist; MVC pages are unverified in a browser (see spec
Known Gaps).

## Project Structure

### Documentation (this feature)

```text
specs/013-user-registration-management/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── tasks.md
├── contracts/
│   ├── account-register-api.md
│   ├── users-api.md
│   └── mvc-pages.md
└── checklists/
    └── requirements.md
```

### Source Code (repository root)

```text
CategoryProduct.API/
├── Program.cs                          # "register" rate-limit policy, RegisterPermitLimit
├── Controllers/
│   ├── AccountController.cs            # + Register action
│   └── UsersController.cs              # new, [Authorize(Roles = "Admin")]
└── Dtos/
    ├── RegisterRequestDto.cs           # new
    ├── UpdateRoleRequestDto.cs         # new
    └── UserResponseDto.cs              # new
CategoryProduct.API.Tests/
├── ApiFactory.cs                       # RegisterPermitLimit=1000
└── UsersTests.cs                       # new (UsersTests, LastAdminTests, AuthHelper)
CategoryProduct.MVC/
├── Controllers/
│   ├── AccountController.cs            # + Register GET/POST
│   └── UsersController.cs              # new, [Authorize(Roles = "Admin")]
├── Models/
│   ├── ApiModels.cs                    # ApiResult.TooManyRequests
│   └── RegisterViewModel.cs            # new (+ UserItem)
├── Services/CategoryProductApiClient.cs  # RegisterAsync, GetUsersAsync, ChangeUserRoleAsync, 429 mapping
└── Views/
    ├── Account/Register.cshtml         # new
    ├── Account/Login.cshtml            # + Register link
    ├── Users/Index.cshtml              # new
    └── Shared/_Layout.cshtml           # Users / Register nav links
```

**Structure Decision**: Extend the two existing projects; no new projects.

## Complexity Tracking

*No entries.*
