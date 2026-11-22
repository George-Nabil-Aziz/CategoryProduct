# Implementation Plan: Role-Based Authorization

**Branch**: `008-role-authorization` (retroactive) | **Date**: 2026-11-03 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/008-role-authorization/spec.md`

## Summary

Add a `Role` to users, emit it as a JWT claim and in the login response, require the `Admin` role for API and MVC writes, and surface permission failures in MVC (Access Denied page for signed-in non-Admins, a form error for API 403s). Shipped in `f868aa7`.

## Technical Context

**Language/Version**: C# / .NET 10 (API and MVC)

**Primary Dependencies**: ASP.NET Core role-based `[Authorize(Roles = ...)]`, EF Core 10 + SQL Server. No new packages.

**Storage**: One new column `User.Role` (`nvarchar(20)`, required, default `User`) via EF Core migration `20261103173834_AddUserRole` (Principle II); the migration also promotes `testuser` to `Admin`.

**Testing**: API integration test `PostCategory_NonAdminToken_Returns403` added to 005 (6th test); MVC not automated.

**Target Platform**: Same hosting as today.

**Project Type**: Web service + MVC web app (existing projects).

**Performance Goals**: N/A; the role comes from the token/cookie, with no per-request database lookup.

**Constraints**: Reads stay public (FR-007); existing Admin flows must keep working after the migration; role changes apply at next login.

**Scale/Scope**: 1 column + migration, 1 DTO field, 6 API write actions and 6 write actions in each MVC controller re-attributed, 1 new MVC action/view, 4 views edited.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Check | Status |
|---|---|---|
| I. Consistent REST API Design | Routes and verbs unchanged; adds the standard `403 Forbidden` for authenticated-but-not-permitted callers alongside existing `401`; login response gains one field (`Role`). | PASS |
| II. Schema Changes Only Through EF Core Migrations | `User.Role` ships with migration `20261103173834_AddUserRole` (plus designer and snapshot) in the same commit as the model change. | PASS |
| III. Simplicity & YAGNI | A single string column and the built-in `[Authorize(Roles = "Admin")]`; no roles tables, policies or custom authorization handlers. | PASS |
| IV. Secrets & Credential Hygiene (NON-NEGOTIABLE) | No secret or password handling changes; the seeded promotion references only a user name. Test users' passwords stay hashed. | PASS |
| V. API/MVC Separation of Concerns | MVC learns the role only from the API's login response (`Role`) and keeps its own `LoginResult`/`ApiResult` types; the API's `LoginResponseDto` change is a contract change communicated through the DTO. | PASS |

No unjustified violations; Complexity Tracking is not needed.

## Project Structure

### Documentation (this feature)

```text
specs/008-role-authorization/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── api-role-authorization.md
│   └── mvc-role-routes.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
CategoryProduct.API/
├── Models/User.cs                          # Role property
├── Data/ApplicationDbContext.cs            # Role config (required, max 20, default "User")
├── Migrations/20261103173834_AddUserRole.cs (+ designer, snapshot)  # adds column; promotes testuser to Admin via SQL
├── Dtos/LoginResponseDto.cs                # + Role
└── Controllers/
    ├── AccountController.cs                # Role claim in JWT, Role in response
    ├── CategoryController.cs               # [Authorize(Roles = "Admin")] on writes
    └── ProductController.cs                # same
CategoryProduct.API.Tests/
├── ApiFactory.cs                           # tester (Admin) + plainuser (User)
└── ApiTests.cs                             # PostCategory_NonAdminToken_Returns403
CategoryProduct.MVC/
├── Program.cs                              # AccessDeniedPath -> /Account/AccessDenied
├── Controllers/AccountController.cs        # role claim in cookie, AccessDenied action
├── Controllers/CategoriesController.cs     # Admin roles, Forbidden form error
├── Controllers/ProductsController.cs       # same
├── Models/ApiModels.cs                     # ApiResult.Forbidden
├── Services/CategoryProductApiClient.cs    # LoginResult.Role, 403 -> Forbidden
└── Views/
    ├── Account/AccessDenied.cshtml         # new
    ├── Categories/Index.cshtml, Details.cshtml
    └── Products/Index.cshtml, Details.cshtml   # hide write links from non-Admins
```

**Structure Decision**: Extend existing files; one new view. No new projects.

## Key Decisions

- Single string role on `User` rather than ASP.NET Identity roles tables (Principle III).
- Role travels in the JWT and the MVC cookie; no per-request database lookup, so role changes apply at next login.
- 403 and 401 handled differently in MVC: signed-in non-Admins reach Access Denied via the cookie middleware, an API 403 shows a form error, and 401 signs out.
- The migration promotes `testuser` to Admin so existing demo flows keep working.

See `research.md` for rationale and alternatives.

## Complexity Tracking

*No entries.*
