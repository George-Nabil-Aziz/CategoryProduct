# Specification Quality Checklist: Test Coverage

**Purpose**: Validate specification completeness and quality before implementation
**Created**: 2026-11-22
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] Mandatory sections completed (stories, edge cases, requirements, success criteria)
- [x] Each user story has a priority, an independent test and acceptance scenarios
- [x] Technical detail is limited to what a test-infrastructure/CI feature inherently needs (named classes, config keys, endpoints)

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous (FR-001..FR-015)
- [x] Success criteria are measurable (SC-001..SC-005)
- [x] Edge cases are identified (cookie isolation, parallel classes, 60 s window, HTML encoding, antiforgery, unknown filter, unhandled exceptions)
- [x] Scope is clearly bounded; out-of-scope items kept as Known Gaps (no browser/JS, compose verified only by CI, found-while-writing issues)
- [x] Assumptions recorded (fake at HTTP boundary, `public partial class Program`, no HTML parser, Development environment, real clock, compose scope, worker approach)

## Feature Readiness

- [x] Every FR maps to a task in tasks.md
- [x] Every public MVC controller action appears in `contracts/mvc-test-matrix.md`
- [x] Groups G2, G3, G4 own disjoint files and depend only on G1 (ownership table in tasks.md)
- [x] Constitution Check passes with no violations (plan.md)
- [x] No schema change confirmed (data-model.md)
- [x] Claims verified against code: `Program` is top-level statements (internal), no `HomeController`, `UsersController` has no 401 handling, `ProductsController.AddErrors` has no Conflict branch, `LogoutAsync` sends no bearer, `ConfigureHttpClientDefaults` is applicable to both clients, worker uses `PeriodicTimer` with integer minutes
- [x] Known Gaps state that compose smoke is verified only by its first CI run and that no real browser is used

## Notes

- Deviations from the request wording, all from code: logout sends no `Authorization` header (only the refresh token body); public GETs (Categories, Products, image) intentionally carry no bearer; Users 401 and Products 409 messages are not handled by the MVC, so those are Known Gaps, not tests.
