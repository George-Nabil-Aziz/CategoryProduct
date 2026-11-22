# Implementation Plan: Auth Hardening

**Branch**: `015-auth-hardening` | **Date**: 2026-11-22 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/015-auth-hardening/spec.md`

## Summary

Close five gaps in `CategoryProduct.API` auth: (1) scope refresh-token reuse revocation to the
token's family via a new `RefreshToken.FamilyId`; (2) make rotation optimistic-concurrency safe with
a `Guid ConcurrencyStamp`; (3) purge old tokens with a `BackgroundService` calling a testable scoped
`RefreshTokenCleanupService`; (4) add a `logout` rate-limit policy; (5) wrap the last-admin check
and update in a serializable transaction. One EF Core migration (`AuthHardening`).

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: ASP.NET Core Web API, EF Core 10 (SqlServer; InMemory in tests),
`Microsoft.AspNetCore.RateLimiting` (framework). No new NuGet package.

**Storage**: SQL Server; `RefreshTokens` gains two columns (see `data-model.md`).

**Testing**: xUnit + `WebApplicationFactory<Program>` in `CategoryProduct.API.Tests` (`ApiFactory`,
EF InMemory). Concurrency cannot be truly reproduced on InMemory; see spec Known Gaps.

**Target Platform**: unchanged.

**Project Type**: existing web service; no new project.

**Performance Goals**: purge is a periodic bulk delete (default hourly); refresh adds one
concurrency predicate to an `UPDATE`.

**Constraints**: no response-shape changes (FR-012); never run `dotnet ef database update`; MVC
unchanged.

**Scale/Scope**: 1 model, 1 DbContext config, 3 controllers/actions touched (`Refresh`, `Logout`,
`UpdateRole`), `TokenService` signature change, 2 new service classes, `Program.cs` wiring,
1 migration, tests.

## Constitution Check

| Principle | Check | Status |
|---|---|---|
| I. Consistent REST API Design | No new routes; existing routes and verbs unchanged; added status `429` on logout and `401` on a lost refresh race use standard results. | PASS |
| II. Schema Changes Only Through EF Core Migrations | New columns ship in `dotnet ef migrations add AuthHardening`, committed with the model change. The generated migration and snapshot are not hand-edited; legacy rows keep the default `Guid.Empty` family (see research Decision 2). | PASS |
| III. Simplicity & YAGNI | Controllers keep using `ApplicationDbContext` directly. The only new abstraction is `RefreshTokenCleanupService`, justified by a concrete second consumer (the hosted worker and unit tests) and by `ExecuteDelete` incompatibility with InMemory. No repository/unit-of-work layer, no Options classes. | PASS |
| IV. Secrets & Credential Hygiene | No secrets added; refresh tokens remain stored as SHA-256 hashes; new config keys are non-secret. | PASS |
| V. API/MVC Separation of Concerns | Change confined to `CategoryProduct.API` and its tests; MVC unchanged (logout is already best-effort). Contract: `429` on logout is additive. | PASS |

## Project Structure

### Documentation (this feature)

```text
specs/015-auth-hardening/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── account-api-changes.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
CategoryProduct.API/
├── Models/RefreshToken.cs                      # + FamilyId, ConcurrencyStamp, Revoke(utcNow)
├── Data/ApplicationDbContext.cs                # IsConcurrencyToken on ConcurrencyStamp
├── Services/
│   ├── TokenService.cs                         # CreateRefreshToken(userId, familyId) sets FamilyId + stamp
│   ├── RefreshTokenCleanupService.cs           # NEW scoped: PurgeAsync(DateTime utcNow)
│   └── RefreshTokenCleanupWorker.cs            # NEW BackgroundService (PeriodicTimer)
├── Controllers/
│   ├── AccountController.cs                    # family on login/rotation, scoped sweep, conflict handling,
│   │                                           #   [EnableRateLimiting("logout")], Revoke() usage
│   └── UsersController.cs                      # serializable transaction + retry in UpdateRole
├── Program.cs                                  # logout policy, DI for service + worker
├── appsettings.json                            # RefreshTokenCleanup section (non-secret defaults)
└── Migrations/<timestamp>_AuthHardening*.cs    # generated only (no hand edits); snapshot regenerated

CategoryProduct.API.Tests/
├── ApiFactory.cs                               # + RateLimiting:LogoutPermitLimit = 1000
├── RefreshTokenTests.cs                        # family, legacy, concurrency interceptor tests
├── RefreshTokenCleanupTests.cs                 # NEW PurgeAsync unit-level tests
├── ApiTests.cs                                 # logout 429 in RateLimitTests (LimitedFactory)
└── UsersTests.cs                               # LastAdminTests additions
```

**Structure Decision**: single existing API project plus its test project; the two new classes live in
`Services/` next to `TokenService`.

## Complexity Tracking

| Deviation | Why needed | Simpler alternative rejected because |
|---|---|---|
| Extra class `RefreshTokenCleanupService` (Principle III) | Testable purge logic separate from timer; second consumer exists | Putting logic in the worker makes it testable only with real timers |
