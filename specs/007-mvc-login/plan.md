# Implementation Plan: MVC Login

**Branch**: `007-mvc-login` (retroactive) | **Date**: 2026-11-03 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/007-mvc-login/spec.md`

## Summary

Add cookie sign-in to `CategoryProduct.MVC` that delegates credential checks to the API login, keeps the returned JWT in the auth cookie as an `access_token` claim, protects create/edit/delete actions with `[Authorize]`, and forwards the token to the API on write calls. A 401 from the API signs the user out and redirects to login. Shipped in `142ace4`.

## Technical Context

**Language/Version**: C# / .NET 10, ASP.NET Core MVC cookie authentication, `IHttpContextAccessor`

**Primary Dependencies**: Feature 003 (API login) and 006 (typed client). No new packages.

**Storage**: None; the cookie carries the session. No schema change, no migration.

**Testing**: Manual in the browser (see `quickstart.md`); no automated MVC tests (known gap).

**Target Platform**: ASP.NET Core MVC web app over HTTPS (cookie is `Secure` always).

**Project Type**: Web application (existing `CategoryProduct.MVC`).

**Performance Goals**: N/A.

**Constraints**: MVC must never store or log passwords (Principle IV); credential verification stays in the API (Principle V); the shared typed `HttpClient` must not carry one user's token to another.

**Scale/Scope**: 1 new controller (3 actions), 1 view model, 1 view, layout edit, 2 controllers edited, client and `Program.cs` edited.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Check | Status |
|---|---|---|
| I. Consistent REST API Design | Uses the existing `POST api/account/login` and the existing write endpoints with a bearer header; no API change. | PASS |
| II. Schema Changes Only Through EF Core Migrations | No model or schema change. | PASS |
| III. Simplicity & YAGNI | The cookie is the session (no session store, no Identity tables, no delegating handler); the client change is one method plus a constructor parameter. | PASS |
| IV. Secrets & Credential Hygiene (NON-NEGOTIABLE) | MVC forwards the password once to the API and does not persist or log it; only the JWT is kept, in an HttpOnly, Secure cookie. No secret is committed. | PASS |
| V. API/MVC Separation of Concerns | MVC authenticates users solely through the API's HTTP login endpoint and keeps its own `LoginResult` record; no API type references. | PASS |

No unjustified violations; Complexity Tracking is not needed.

## Project Structure

### Documentation (this feature)

```text
specs/007-mvc-login/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── mvc-routes.md
│   └── api-calls.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
CategoryProduct.MVC/
├── Program.cs                            # AddHttpContextAccessor, AddCookie (LoginPath, LogoutPath, AccessDeniedPath; HttpOnly, Secure=Always, SameSite=Lax), UseAuthentication
├── Controllers/AccountController.cs      # new: Login GET/POST, Logout (POST)
├── Controllers/CategoriesController.cs   # [Authorize] on writes, 401 -> sign out + redirect
├── Controllers/ProductsController.cs     # same
├── Services/CategoryProductApiClient.cs  # LoginAsync, per-request bearer from `access_token` claim
├── Models/LoginViewModel.cs              # new
├── Views/Account/Login.cshtml            # new
├── Views/Shared/_Layout.cshtml           # sign in / sign out UI
└── Views/Categories/Index.cshtml, Views/Products/Index.cshtml  # "Create New" only when authenticated
```

**Structure Decision**: Extend the existing MVC project; add `AccountController` and its view only.

## Key Decisions

- JWT stored as a claim in the cookie (no server session store): simplest approach for a single MVC host.
- Cookie ticket expiry equals the token expiry, `IsPersistent = false`.
- Bearer set per request (`HttpRequestMessage`) rather than on `DefaultRequestHeaders`, to avoid leaking one user's token to another through the shared typed client.
- The API stays the only authority on credentials; MVC never stores passwords.
- Handling 401 by signing out prevents users from being stuck with an expired token.

See `research.md` for rationale and alternatives.

## Complexity Tracking

*No entries.*
