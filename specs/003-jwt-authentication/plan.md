# Implementation Plan: JWT Authentication

**Branch**: `003-jwt-authentication` | **Date**: 2026-08-20 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/003-jwt-authentication/spec.md`

## Summary

Add username/password login to `CategoryProduct.API` that issues a JWT access credential, and
require a valid, unexpired credential on every `Category`/`Product` create, update, or delete
(`POST`/`PUT`/`DELETE`) request; `GET` endpoints stay fully public. Passwords are hashed with
`Microsoft.AspNetCore.Identity.PasswordHasher<User>` (already available via the shared framework,
no new dependency); tokens are issued and validated with the already-referenced
`Microsoft.AspNetCore.Authentication.JwtBearer` package. The plan also fixes two pre-existing
issues discovered in the codebase that this feature makes security-relevant for the first time: a
JWT signing key committed in plaintext to `appsettings.json` (Constitution Principle IV violation)
and dead/unreachable `UseAuthentication`/`UseAuthorization` calls placed after `app.Run()` in
`Program.cs`.

## Technical Context

**Language/Version**: C# / .NET 10 (matches existing `CategoryProduct.API.csproj`)

**Primary Dependencies**: ASP.NET Core Web API, Entity Framework Core 10 + SQL Server,
`Microsoft.AspNetCore.Authentication.JwtBearer` (already referenced) for issuing/validating JWTs,
`Microsoft.AspNetCore.Identity.PasswordHasher<User>` (part of the shared framework, no new
`PackageReference`) for password hashing. No new NuGet dependency is added.

**Storage**: SQL Server via `ApplicationDbContext`. This feature modifies the existing `User`
model (rename `Password` → `PasswordHash`, add a unique index on `UserName`) via a new EF Core
migration — see `data-model.md`.

**Testing**: No automated test project exists in `CategoryProduct.slnx`. Per constitution
Development Workflow, verification is manual against the running API via Swagger UI (see
`quickstart.md`).

**Target Platform**: ASP.NET Core Web API, same hosting model as today (no platform change).

**Project Type**: Web service (existing `CategoryProduct.API` project); no new project created.

**Performance Goals**: N/A beyond existing behavior — JWT validation is an in-process, synchronous
signature/claims check per request, negligible relative to existing EF Core calls.

**Constraints**: Must not change the success-path response shape of any existing Category/Product
endpoint (FR-006); must not introduce role/permission distinctions (single access level per
Assumptions); must not add self-service registration, logout, or lockout (out of scope per
Assumptions); the JWT signing key must never be committed to source control (Constitution
Principle IV, NON-NEGOTIABLE).

**Scale/Scope**: 1 new endpoint (`POST /api/Account/login`), 6 existing write actions gaining an
`[Authorize]` attribute (`Category` and `Product` × `POST`/`PUT`/`DELETE`), 1 modified model + 1
new migration, 2 new DTOs, 1 `Program.cs` wiring fix, 1 secrets-hygiene fix.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Check | Status |
|---|---|---|
| I. Consistent REST API Design | New `POST /api/Account/login` follows the existing `api/[controller]` route pattern and returns standard `ActionResult`s (`Ok`/`Unauthorized`/`BadRequest`). It is an action endpoint, not a CRUD resource, so it does not need list/pagination semantics. Existing `Category`/`Product` routes, verbs, and response shapes are unchanged. | PASS |
| II. Schema Changes Only Through EF Core Migrations | `User.Password` → `PasswordHash` rename and a new unique index on `UserName` ship as one new EF Core migration in the same change set as the model edit. | PASS |
| III. Simplicity & YAGNI | No repository/service layer added; `AccountController` talks to `ApplicationDbContext` directly like the other controllers. Password hashing uses the shared-framework `PasswordHasher<User>` rather than full ASP.NET Core Identity (no roles/self-service, matching spec Assumptions). Endpoint protection uses per-action `[Authorize]` attributes, the framework's built-in mechanism — no custom middleware or policy scheme. | PASS |
| IV. Secrets & Credential Hygiene (NON-NEGOTIABLE) | **Pre-existing violation found and fixed by this plan**: `appsettings.json` currently commits a literal `Jwt:Key`. This feature removes that literal value from the tracked file, uses .NET user secrets for local development and the `Jwt__Key` environment variable in other environments (see `research.md` Decision 3), and hashes all passwords before storage/comparison (Decision 1). | PASS (post-remediation, in scope) |
| V. API/MVC Separation of Concerns | Change is confined to `CategoryProduct.API`. `CategoryProduct.MVC` is not referenced or modified by this feature (it will need its own future update to send credentials, but that is out of scope here, per spec edge cases). | PASS |

No unjustified violations — Complexity Tracking table below is not needed (Principle IV's finding
is a remediation performed *by* this plan, not a deviation being requested).

## Project Structure

### Documentation (this feature)

```text
specs/003-jwt-authentication/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md         # Phase 1 output (/speckit-plan command)
├── contracts/            # Phase 1 output (/speckit-plan command)
│   ├── account-api.md
│   └── protected-write-endpoints.md
└── tasks.md              # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

Single existing project (`CategoryProduct.API`); no new projects or top-level folders. This
feature touches:

```text
CategoryProduct.API/
├── appsettings.json                  # remove committed Jwt:Key literal; keep Issuer/Audience/ExpiryMinutes
├── Program.cs                        # add AddAuthentication/AddJwtBearer + AddAuthorization;
│                                      # fix UseAuthentication/UseAuthorization ordering & placement
├── Models/
│   └── User.cs                       # rename Password -> PasswordHash
├── Migrations/
│   └── <timestamp>_UpdateUsersTableForAuth.cs   # new: rename column, add unique index on UserName
├── Dtos/
│   ├── LoginRequestDto.cs            # new: UserName, Password (both required)
│   └── LoginResponseDto.cs           # new: Token, ExpiresAtUtc
└── Controllers/
    ├── AccountController.cs          # implement POST login (currently an empty stub)
    ├── CategoryController.cs         # add [Authorize] to Post/Put/Delete only
    └── ProductController.cs          # add [Authorize] to Post/Put/Delete only
```

**Structure Decision**: Single existing project (`CategoryProduct.API`), no new source
directories. Matches Constitution Principle III: the smallest change that satisfies the
requirement, reusing the `AccountController`/`User` scaffolding and package references already
present in the codebase.

## Complexity Tracking

*No entries — Constitution Check reported no unjustified violations.*
