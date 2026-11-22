# Specification Quality Checklist: Security Cleanup

**Purpose**: Validate specification completeness and quality before implementation
**Created**: 2026-11-22
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] Mandatory sections completed (stories, edge cases, requirements, success criteria)
- [x] Each user story has a priority, an independent test and acceptance scenarios
- [x] Technical detail limited to what a security cleanup inherently needs (migration, hash, config keys, statuses)

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous (FR-001..FR-019)
- [x] Success criteria are measurable (SC-001..SC-006)
- [x] Edge cases identified (operator-changed password, zero admins after migration, issued access tokens, name taken, refresh 429)
- [x] Scope bounded; Out of Scope lists deletion, password change, token revocation, API rate limits, other 429 texts
- [x] Assumptions recorded (neutralize vs delete, hash-equality guard, marker, SQL in new migration, bootstrap placement and failure mode, message text)

## Feature Readiness

- [x] Every FR maps to a task in tasks.md (tick after review)
- [x] Every story has tests written before the change
- [x] Constitution Check passes (plan.md); Principle II judgement call in Complexity Tracking
- [x] No schema change confirmed (data-model.md)
- [x] Claims verified against code: only four `AccountController` actions have `[EnableRateLimiting]`; `api/users` and `api/product` do not; `testuser` appears only in two migrations, `AdminDemotionSqlServerTests` and specs; `AddUserRole` already uses `migrationBuilder.Sql`; FK behaviors (Orders Restrict, StockMovements SetNull, CartItems/RefreshTokens Cascade); `DatabaseMigrator` runs before `app.Run()` and skips non-relational providers; `ApiFactory` seeds Admin `tester`; `SqlServerTests` is not in `CategoryProduct.slnx`; registration rules; `UsersController` (MVC) and `ProductsController.AddErrors` have no 429 branch; Cart/Orders/Inventory use "Please try again later."
- [x] Scratch-verified: `/w==` verifies as `Failed`, `!` throws `FormatException`; `Validator.TryValidateObject` does not enforce positional-record attributes
- [x] Known Gaps list what stays open
- [x] Known Gaps of 013, 016, 020, 021, 022 updated (tasks Phase 6)

## Notes

- Deviation from the request wording: the 429 premise is only partly true, since the API never rate-limits Users/Products; the work is defensive (documented in spec and research R7).
- Deviation: the 429 text for new pages is the requested "Try again in a minute."; Cart/Orders/Inventory keep "Please try again later.".
