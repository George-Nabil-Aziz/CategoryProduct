# Specification Quality Checklist: MVC Bug Fixes

**Purpose**: Validate specification completeness and quality before implementation
**Created**: 2026-11-22
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] Mandatory sections completed (stories, edge cases, requirements, success criteria)
- [x] Each user story has a priority, an independent test and acceptance scenarios
- [x] Technical detail is limited to what a bug-fix feature inherently needs (named classes, methods, statuses)

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous (FR-001..FR-014)
- [x] Success criteria are measurable (SC-001..SC-006)
- [x] Edge cases are identified (POST re-execution, direct `/Home/Error`, expired token on logout, POST-only returnUrl, 409 without detail)
- [x] Scope is clearly bounded; Out of Scope lists the seeded `testuser`, API logout auth, 429 handling, status pages
- [x] Assumptions recorded (FakeApi throw as test seam, Production factory, dead views deleted, Users 403 handling, list URL as returnUrl, options key marker)

## Feature Readiness

- [x] Every FR maps to a task in tasks.md
- [x] Every user story has tests listed in tasks.md written before the fix
- [x] Constitution Check passes with no violations (plan.md)
- [x] No schema change, no migration, no new dependency confirmed (data-model.md)
- [x] Claims verified against code: no `HomeController` and `Views/Home` contains only `Index.cshtml` and `Privacy.cshtml`; `Program.cs` registers `UseExceptionHandler("/Home/Error")` outside Development; `UsersController` has no 401 handling and `GetUsersAsync` uses `EnsureSuccessStatusCode`; `ProductsController.AddErrors` has no Conflict branch and `CategoriesController.AddErrors` has one; `LogoutAsync` posts only `{ refreshToken }` without bearer; `TokenRefreshHandler` refreshes any request with a bearer (expiry < 60 s or 401) and the API `Refresh` rotates the refresh token while `Logout` is anonymous and ignores already revoked tokens; `MvcFactory` is `sealed` with `UseEnvironment("Development")`; the 021 test `Logout_clears_cookie_and_revokes_refresh_token` asserts `Assert.Null(call.Authorization)`
- [x] Known Gaps list what stays open (testuser, 429, POST returnUrl in other controllers, Development error page, bearer ignored by the API)
- [x] The 021 Known Gaps update is a task (T025-T026)

## Notes

- Deviation from the original request wording: the unhandled exception for the error-page test is produced with the existing `FakeApi.Throw` (a non-`HttpRequestException`) instead of a new test-only endpoint or filter; see spec Assumptions and research R8.
- The logout "no refresh" requirement is the riskiest part: see `contracts/logout-bearer.md` for the API behavior it protects.
