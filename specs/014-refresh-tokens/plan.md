# Implementation Plan: Refresh Tokens

**Branch**: `014-refresh-tokens` | **Date**: 2026-11-22 (retroactive) | **Spec**: [spec.md](./spec.md)

**Status**: Implemented, commit `277a46b`

## Summary

Add rotating, hashed, server-tracked refresh tokens with reuse detection to `CategoryProduct.API`
(`refresh` and `logout` endpoints, `TokenService`, `RefreshTokens` table), and make
`CategoryProduct.MVC` keep sessions alive transparently through a `TokenRefreshHandler` and a shared
`AuthSessionService`.

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: ASP.NET Core, EF Core 10 + SQL Server, JwtBearer (existing),
`System.Security.Cryptography` (SHA-256, RNG), `Microsoft.AspNetCore.WebUtilities` (Base64Url),
ASP.NET Core rate limiting. No new package.

**Storage**: SQL Server; new table `RefreshTokens` via migration `AddRefreshTokens`
(`20261103183150`).

**Testing**: xUnit + `WebApplicationFactory` (`ApiFactory`, EF Core in-memory); 5 tests in
`RefreshTokenTests.cs`. MVC has no automated tests.

**Target Platform**: ASP.NET Core Web API + MVC site

**Project Type**: Two existing web projects

**Constraints**: Raw refresh tokens never stored (Principle IV); MVC talks to the API only over HTTP
(Principle V); login success shape gains fields (additive, but a contract change for consumers).

**Scale/Scope**: 2 endpoints, 1 changed endpoint, 1 entity + migration, 1 service, 1 rate-limit
policy, 2 MVC services, 5 tests.

## Constitution Check

| Principle | Check | Status |
|---|---|---|
| I. Consistent REST API Design | `refresh` and `logout` are action endpoints under `api/Account` like `login`; standard codes (`200`, `204`, `401`, `429`). Not CRUD resources, so pagination does not apply. | PASS |
| II. Schema Changes Only Through EF Core Migrations | `RefreshToken` model, `DbContext` mapping and migration `AddRefreshTokens` (plus snapshot update) ship in the same commit. | PASS |
| III. Simplicity & YAGNI | `TokenService` extracted because login and refresh both issue tokens (two concrete consumers); no repository layer; reuse revocation is a simple per-user sweep rather than chain tracking. | PASS |
| IV. Secrets & Credential Hygiene | Only SHA-256 hashes of refresh tokens persisted; signing key still from configuration/user secrets; no secret values added. | PASS |
| V. API/MVC Separation of Concerns | MVC consumes `api/account/refresh` and `logout` over HTTP with its own `LoginResult` record; no shared types. The additive login response is communicated through the API DTO. | PASS |

Development Workflow: model change and migration are in the same commit; automated API tests exist;
MVC behavior is not verified in a browser (spec Known Gaps).

## Project Structure

### Documentation (this feature)

```text
specs/014-refresh-tokens/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── tasks.md
├── contracts/
│   ├── refresh-api.md
│   └── mvc-session.md
└── checklists/
    └── requirements.md
```

### Source Code (repository root)

```text
CategoryProduct.API/
├── Program.cs                                   # TokenService (scoped), "refresh" rate-limit policy
├── Models/RefreshToken.cs                       # new
├── Data/ApplicationDbContext.cs                 # DbSet + RefreshToken mapping
├── Services/TokenService.cs                     # new: access + refresh token creation, Hash
├── Dtos/LoginResponseDto.cs                     # + refresh fields, RefreshRequestDto
├── Controllers/AccountController.cs             # refresh, logout, shared IssueAsync
└── Migrations/
    ├── 20261103183150_AddRefreshTokens.cs       # new (+ Designer, snapshot update)
    └── ApplicationDbContextModelSnapshot.cs
CategoryProduct.API.Tests/
├── ApiFactory.cs                                # RefreshPermitLimit=1000
└── RefreshTokenTests.cs                         # new
CategoryProduct.MVC/
├── Program.cs                                   # AuthSessionService, TokenRefreshHandler, api-refresh client
├── Controllers/AccountController.cs             # uses AuthSessionService; logout revokes token
└── Services/
    ├── AuthSessionService.cs                    # new
    ├── TokenRefreshHandler.cs                   # new
    └── CategoryProductApiClient.cs              # LoginResult extended, LogoutAsync
```

**Structure Decision**: Extend the two existing projects; one new folder-level file set only
(`Services/` already existed in MVC; `Services/` is new in API).

## Complexity Tracking

*No entries.*
