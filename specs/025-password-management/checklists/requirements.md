# Specification Quality Checklist: Password Management

**Purpose**: Validate specification completeness and quality before implementation
**Created**: 2026-11-22
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] Mandatory sections completed (stories, edge cases, requirements, success criteria)
- [x] Each user story has a priority, an independent test and acceptance scenarios
- [x] Technical detail limited to what the feature inherently needs (endpoints, statuses, config keys, token handling)

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous (FR-001..FR-016)
- [x] Success criteria are measurable (SC-001..SC-007)
- [x] Edge cases identified (enumeration, unverified emails, token in URL, stateless access tokens, InMemory vs SQL Server, concurrency, mail scanners)
- [x] Scope bounded; Out of Scope lists admin reset, verification, JWT stamp, lockout, HTML mail
- [x] Assumptions recorded (fresh tokens on change, 204 on reset, 202 on forgot, single error message, 400 for wrong current, per-user interval, hash choice, link base from config, shared `password` policy)

## Feature Readiness

- [ ] Every FR maps to a task in tasks.md (tick after review)
- [ ] Every story has tests written before the change (tick after implementation)
- [x] Constitution Check passes (plan.md); judgement calls in Complexity Tracking
- [x] Schema change will ship as a generated migration (data-model.md, T012)
- [x] Claims verified against code by reading: no change/reset endpoint exists; `PasswordHasher<User>` use in Register/Login; register password rules (`Required` + `MinLength(8)`); refresh token hashing (`TokenService.Hash`, SHA-256 hex) and `Revoke`; `IssueAsync`; rate-limit policy pattern and `ApiFactory` limits; 024 `IEmailSender`/`EmailQueue`/`NotificationService`/`EmailAddressRules`; `RecordingEmailSender`; `RefreshTokenCleanupService`/`Worker`; `TimeProvider` only in the worker and `FakeTimeProvider` package already referenced; MVC `AccountController`, `AuthSessionService`, `ApiResult`, `ApiRateLimit`
- [ ] To verify during implementation: `Program.cs` has no other `TimeProvider` registration (R5); nested compose interpolation `${A:-http://localhost:${B:-5256}}` is accepted (CI `compose-smoke`); the MVC `FakeApi` returns 202 and 204 with empty bodies as `ApiResult` expects; `HasPendingModelChanges()` false after the migration
- [x] Known Gaps list what stays open (CI verification task T041 left open on purpose)
