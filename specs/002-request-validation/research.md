# Research: Request Validation

## Decision 1: Validation mechanism

**Decision**: Use `System.ComponentModel.DataAnnotations` validation attributes directly on the
`CategoryRequestDto` and `ProductRequestDto` positional record parameters (applied with no
explicit attribute target, so they bind to the primary constructor parameter — see implementation
note below), and rely on ASP.NET Core's built-in automatic HTTP 400 response for
`[ApiController]`-decorated controllers. No new NuGet package, custom action filter, or middleware
is introduced.

**Implementation note (discovered while implementing)**: an earlier version of this decision
called for the `[property: ...]` target syntax. That throws a `500`
(`System.InvalidOperationException`: "... validation metadata must be associated with the
constructor parameter") at request time, because ASP.NET Core's record model-validation reads
attributes from the constructor parameter, not the synthesized property. Applying the attributes
with no explicit target (letting the compiler bind them to the parameter, which is C#'s default
for positional records) fixes this and was verified end-to-end against the running API.

**Rationale**:
- Both `CategoryController` and `ProductController` are already decorated with `[ApiController]`.
  ASP.NET Core automatically runs model validation during model binding for any action parameter
  and short-circuits with a `400 Bad Request` + `ValidationProblemDetails` body when the
  `ModelState` is invalid — before the action method body ever executes. No controller code
  changes are required to wire this up.
- `ModelState` validation evaluates every property on the bound object (not just the first
  failure), so a request that fails multiple rules at once (e.g., bad name *and* negative price)
  surfaces every failing field in one response — satisfying FR-007 and the corresponding edge
  case without extra work.
- `System.ComponentModel.Annotations` is part of the shared framework already referenced
  implicitly by the SDK; it requires no new `PackageReference`.
- This is the smallest change that satisfies every functional requirement, matching Constitution
  Principle III (Simplicity & YAGNI): "Add an abstraction only when... a genuine cross-cutting
  concern... demands it." Validation is exactly that cross-cutting concern, and DataAnnotations +
  the framework's existing automatic-400 behavior is the abstraction already built into the stack
  the project uses — nothing further needs to be added.
- `[Required]` on a `string` property in .NET already trims and rejects whitespace-only values
  (its `IsValid` override does `stringValue.Trim().Length != 0` when `AllowEmptyStrings` is
  `false`, the default), so the "whitespace-only name is invalid" edge case is covered without a
  custom attribute.

**Alternatives considered**:
- **FluentValidation** (`FluentValidation.AspNetCore` or DI-registered validators): more
  expressive and testable in isolation, but adds a new dependency, a new `Validators/` folder, and
  DI registration for a rule set that is fully expressible with built-in attributes (required,
  string length, numeric range). Rejected as unjustified complexity for the current scope — would
  be reconsidered if validation rules later need cross-field logic, async/DB-backed checks (e.g.,
  "CategoryId must reference an existing Category" — explicitly out of scope per spec Assumptions),
  or reuse across many more DTOs.
- **Manual validation in each controller action** (`if` checks + `return BadRequest(...)`): keeps
  everything inline like the rest of the controller code, but duplicates the same rules across
  `Post`/`Put` for both controllers (4 call sites), is easy to let drift out of sync, and does not
  match the "consistent" requirement as well as a single declarative source of truth on the DTO.
  Rejected in favor of the declarative attribute approach, which is both less code and single-
  sourced.

## Decision 2: Numeric range attributes for `decimal`/`int` fields

**Decision**: Use `[Range(typeof(decimal), "0", "79228162514264337593543950335")]` for `Price`
(bounding at `decimal.MaxValue`) and `[Range(0, int.MaxValue)]` for `Quantity`. Use
`[Range(1, int.MaxValue)]` for `CategoryId` (">= 1" is equivalent to the spec's "greater than 0"
for an integer).

**Rationale**: `RangeAttribute`'s numeric overload compares via `double`, which is fine for `int`
but can misbehave at the extremes of `decimal` precision; the `typeof(decimal)` string-bounds
overload avoids that conversion and is the documented pattern for validating `decimal` ranges.

**Alternatives considered**: `[Range(0, double.MaxValue)]` directly on the `decimal Price` —
simpler to write, but risks edge-case precision mismatches at very large values; rejected in favor
of the exact-typed overload since it costs nothing extra to specify correctly.

## Decision 3: Testing / verification approach

**Decision**: No automated test project exists in the solution (`CategoryProduct.slnx` contains
only `CategoryProduct.API` and `CategoryProduct.MVC`). Per Constitution "Development Workflow",
this feature will be manually verified against the running API via Swagger UI: for each rule,
exercise one request that violates it and one boundary-valid request, confirming a `400` with the
expected field error, or a `200`/`201` with successful persistence, respectively.

**Rationale**: Matches the constitution's explicit statement that "until an automated test suite
exists for a project, changes to controller behavior MUST be manually verified against the running
API (via Swagger UI)." Introducing a test project is out of scope for this feature and not
requested.

**Alternatives considered**: Adding an xUnit test project — would be valuable long-term but is a
separate, larger change (new project, test infrastructure, CI wiring) not requested by this
feature and disproportionate to "keep the implementation simple."

## Summary of resolved unknowns

No `NEEDS CLARIFICATION` markers remained from the spec. The only technical decisions needed were
the validation mechanism and attribute choices above, both resolved by reusing the framework
capability already implicit in the existing `[ApiController]` controllers.
