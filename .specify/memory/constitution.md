<!--
Sync Impact Report
- Version change: (unratified template) → 1.0.0
- Modified principles: n/a (initial ratification; all 5 principle slots newly defined)
  - [PRINCIPLE_1_NAME] → I. Consistent REST API Design
  - [PRINCIPLE_2_NAME] → II. Schema Changes Only Through EF Core Migrations
  - [PRINCIPLE_3_NAME] → III. Simplicity & YAGNI
  - [PRINCIPLE_4_NAME] → IV. Secrets & Credential Hygiene (NON-NEGOTIABLE)
  - [PRINCIPLE_5_NAME] → V. API/MVC Separation of Concerns
- Added sections: Technology Stack, Development Workflow, Governance (all populated for the first time)
- Removed sections: none
- Templates requiring updates:
  - .specify/templates/plan-template.md — ⚠ pending manual review (Constitution Check gates should reference the 5 principles above)
  - .specify/templates/spec-template.md — ✅ no constitution-specific references
  - .specify/templates/tasks-template.md — ✅ no constitution-specific references
  - .specify/templates/checklist-template.md — ✅ no constitution-specific references
- Follow-up TODOs:
  - TODO(RATIFICATION_DATE): original adoption date predates this formal ratification and was not recorded; using the date this constitution was first authored (2026-08-19) as the ratification date.
-->

# CategoryProduct Constitution

## Core Principles

### I. Consistent REST API Design
CategoryProduct.API controllers MUST follow the conventions already established in the codebase:
route pattern `api/[controller]`, plural resource nouns, and standard HTTP verbs mapped to
CRUD (`GET` list/single, `POST` create, `PUT` update, `DELETE` remove). List endpoints MUST
support pagination (`page`, `pageSize`) rather than returning unbounded result sets. Responses
MUST use standard `ActionResult` status codes (`Ok`, `NotFound`, `BadRequest`, etc.) instead of
ad-hoc response shapes, so API consumers (including CategoryProduct.MVC) can rely on predictable
behavior.

**Rationale**: The API is the shared contract between CategoryProduct.API and any client
(including CategoryProduct.MVC and future consumers). Consistency here is what keeps the two
projects decoupled while still interoperable.

### II. Schema Changes Only Through EF Core Migrations
All database schema changes MUST be made by adding an EF Core Code-First migration
(`dotnet ef migrations add ...`) and MUST be committed alongside the model change in the same
change set. Manually editing the database schema outside of migrations, or hand-editing generated
migration/snapshot files, is prohibited. Each project's `ApplicationDbContext` and `Models` remain
that project's own source of truth — do not share a `DbContext` or model assembly across
CategoryProduct.API and CategoryProduct.MVC.

**Rationale**: Migrations are the only record of how the schema evolved; skipping them or editing
around them makes the database state undiscoverable from source control and breaks reproducibility
across environments.

### III. Simplicity & YAGNI
Controllers MAY talk to `ApplicationDbContext` directly, as they do today — do not introduce
repository, service, or unit-of-work layers speculatively. Add an abstraction only when a second,
concrete consumer or a genuine cross-cutting concern (e.g., shared validation, caching) demands it,
not in anticipation of future needs. Prefer the smallest change that satisfies the current
requirement.

**Rationale**: The codebase is small and early-stage; premature layering adds indirection that
slows down the very features it's meant to support, without a second use case to justify it.

### IV. Secrets & Credential Hygiene (NON-NEGOTIABLE)
Real secrets (JWT signing keys, API keys, passwords, connection strings with embedded credentials)
MUST NOT be committed to source control, including in `appsettings.json` /
`appsettings.Development.json`. Local development secrets belong in user secrets or an untracked
local override file; production secrets are supplied via environment/deployment configuration.
User passwords MUST be hashed with a modern password hashing algorithm before storage or
comparison — plaintext passwords MUST NEVER be persisted, logged, or transmitted outside of the
initial credential submission.

**Rationale**: Committed secrets and plaintext passwords are irreversible-by-default leaks —
once pushed, rotation is the only remedy — so this is treated as a hard gate rather than a
guideline.

### V. API/MVC Separation of Concerns
CategoryProduct.MVC MUST treat CategoryProduct.API as an external service consumed over HTTP (or
MUST operate on its own independent data access, as it currently does) — it MUST NOT reference
CategoryProduct.API's project, `DbContext`, or model types directly. Contract changes to the API
(routes, request/response shapes) MUST be considered breaking for any consumer and communicated
through the API's own model/DTO types, not by sharing internals.

**Rationale**: Keeping the two projects independently deployable and independently testable is the
reason the solution is split into two projects in the first place; direct references would collapse
that boundary silently.

## Technology Stack

CategoryProduct targets .NET 10 across both projects. CategoryProduct.API is an
ASP.NET Core Web API using Entity Framework Core 10 with SQL Server, documented via
Swagger/OpenAPI (Swashbuckle), and authenticates using JWT bearer tokens
(`Microsoft.AspNetCore.Authentication.JwtBearer`). CategoryProduct.MVC is an ASP.NET Core MVC
application with its own Entity Framework Core 10 + SQL Server data access. Introducing a new
runtime, ORM, or database engine requires updating this section as part of the same change.

## Development Workflow

Every change that adds or modifies a `Model` class MUST include the corresponding EF Core
migration in the same commit/PR. Reviewers MUST check that no secret values were introduced into
tracked configuration files (see Principle IV) before approving a PR. Until an automated test
suite exists for a project, changes to controller behavior MUST be manually verified against the
running API (via Swagger UI) or MVC views before being considered complete.

## Governance

This constitution supersedes any conflicting informal practice. Amendments are made by editing
this file, incrementing `CONSTITUTION_VERSION` per semantic versioning (MAJOR: principle removed
or redefined incompatibly; MINOR: principle or section added or materially expanded; PATCH:
wording/clarification only), and updating `LAST_AMENDED_DATE`. Every PR and `/speckit-plan`
Constitution Check MUST verify compliance with the principles above; any deviation must be
justified in the plan's Complexity Tracking section rather than silently merged.

**Version**: 1.0.0 | **Ratified**: 2026-08-19 | **Last Amended**: 2026-08-19
