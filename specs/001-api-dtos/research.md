# Phase 0 Research: API DTOs for Category & Product

No `NEEDS CLARIFICATION` markers remain in the Technical Context — the one open design question
(whether Product responses should include a denormalized category name) was already resolved
during `/speckit-clarify` and is recorded in `spec.md`'s Clarifications section. This document
records the remaining implementation-level decisions needed before design.

## Decision: Mapping strategy (manual vs. a mapping library)

- **Decision**: Map between entities and DTOs manually — either as small static/extension methods
  (e.g., `CategoryMappingExtensions.ToResponseDto(this Category c)`) or inline in the controller
  action, consistent with the existing controller style.
- **Rationale**: The surface is tiny (2 entities, 4 DTOs, no nested object graphs to flatten). A
  mapping library (AutoMapper, Mapster) would add a new dependency and a configuration/registration
  step for a problem that a couple of one-line methods already solve. Constitution Principle III
  (Simplicity & YAGNI) directs adding an abstraction only when a concrete second need demands it —
  no second need exists here.
- **Alternatives considered**:
  - *AutoMapper*: rejected — new dependency, profile registration, and convention "magic" are
    unjustified overhead for 4 DTOs.
  - *Mapster*: same rejection rationale as AutoMapper, plus it is less commonly known to reviewers
    of this codebase.

## Decision: One shared request DTO per entity (not separate Create/Update DTOs)

- **Decision**: `CategoryRequestDto` and `ProductRequestDto` are each used for both the `POST`
  (create) and `PUT` (update) actions of their controller.
- **Rationale**: Per FR-003/FR-004, a category create and update request both carry only `Name`.
  Per FR-005/FR-006, a product create and update request both carry `Name`, `Price`, `Quantity`,
  and `CategoryId`. Since the field sets are identical today, a single request DTO per entity is
  the smallest change that satisfies the requirement; splitting into `*CreateDto`/`*UpdateDto` pairs
  would duplicate identical property lists with no behavioral difference.
- **Alternatives considered**:
  - *Separate `CategoryCreateDto`/`CategoryUpdateDto` (and the Product equivalents)*: rejected for
    now as unnecessary duplication — nothing in the spec differentiates create-time and
    update-time fields. If a future feature needs them to diverge (e.g., an update-only field),
    splitting them is a small, localized change at that point.

## Decision: DTO shape (C# `record` vs. `class`)

- **Decision**: Implement DTOs as C# `record` types.
- **Rationale**: Records are immutable-by-default, support concise positional-or-init-property
  syntax, and are fully supported by ASP.NET Core's `System.Text.Json` model binding for both
  `[FromBody]` request deserialization and response serialization on .NET 10 — no extra
  configuration needed. This matches the project's existing `<Nullable>enable</Nullable>` /
  `<ImplicitUsings>enable</ImplicitUsings>` modern-C# style.
- **Alternatives considered**:
  - *Plain classes with `{ get; set; }`*: rejected — works identically at runtime but is more
    verbose than a record for a pure data-carrier type with no behavior.

## Decision: Field set per DTO

- **Decision**:
  - `CategoryResponseDto`: `Id`, `Name`.
  - `CategoryRequestDto`: `Name`.
  - `ProductResponseDto`: `Id`, `Name`, `Price`, `Quantity`, `CategoryId`.
  - `ProductRequestDto`: `Name`, `Price`, `Quantity`, `CategoryId`.
- **Rationale**: Directly derived from FR-001 through FR-006 and the clarified decision to exclude
  any category-name field from the product response. No field is added beyond what the entities
  already expose today (no new business data), satisfying the "DTOs only" scope limit.
- **Alternatives considered**: None — field sets are fully determined by the spec, not an open
  design choice.

## Decision: Where mapping happens for the `GetAllProducts` (products-by-category) endpoint

- **Decision**: `CategoryController.GetAllProducts` maps each `Product` to the same
  `ProductResponseDto` used by `ProductController`, keeping one definition of "what a product
  response looks like."
- **Rationale**: FR-007 requires this endpoint to return the same representation as the main
  product endpoints; reusing `ProductResponseDto` (rather than defining a second, similar DTO) is
  the direct way to guarantee that.
- **Alternatives considered**: A dedicated DTO for this endpoint — rejected, would duplicate
  `ProductResponseDto` for no behavioral difference.
