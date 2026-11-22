# Research: Search, Filter and Sort

## Decision 1: Case-insensitive substring via `ToLower().Contains`

**Decision**: `term = search.Trim().ToLower()`; `Where(x => x.Name.ToLower().Contains(term))`.

**Rationale**: Provider-independent, explicit, and matches the tests ("widget" finds "Widget A").

**Alternatives considered**: `EF.Functions.Like` (SQL Server specific); rely on database collation
(implicit and easy to break); full-text search (overkill).

## Decision 2: Count before paging, on the filtered query

**Decision**: Build `query`, `CountAsync()` on it, then order/skip/take.

**Rationale**: `totalCount` stays consistent with the filtered list so MVC paging is correct.

## Decision 3: Whitelisted sort fields via a tuple switch

**Decision**: `(sortBy?.ToLowerInvariant(), desc) switch` with cases `name`/`price` x asc/desc, each
followed by `ThenBy(p => p.Id)`, plus `(_, true)` -> `Id` descending and default `Id` ascending.

**Rationale**: Only known columns can be sorted (no dynamic expressions), and the secondary `Id` key
keeps paging stable. Unknown values fall back instead of failing.

**Alternatives considered**: Dynamic LINQ / reflection on property names (risk, extra dependency);
returning 400 for unknown `sortBy` (stricter; not chosen).

## Decision 4: Reject inverted price ranges

**Decision**: If `minPrice > maxPrice`, add a model error on `minPrice` and return
`ValidationProblem(ModelState)` (HTTP 400).

**Rationale**: An inverted range can only be a client error; this is consistent with the validation
responses from `002-request-validation`.

## Decision 5: MVC sends every parameter, keeps filters in links

**Decision**: `CategoryProductApiClient.GetProductsAsync` always appends all parameters
(`Uri.EscapeDataString` for text, invariant culture for decimals, lower-case `desc`); views use
`asp-route-*` tag helpers so Previous/Next/page links preserve filters.

**Rationale**: Simplest way to keep the filter state across pages without server-side session state.
