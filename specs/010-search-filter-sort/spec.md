# Feature Specification: Search, Filter and Sort

**Feature Branch**: `010-search-filter-sort` (documented retroactively; implemented directly on `master`)

**Created**: 2026-11-03

**Status**: Implemented (commit `aebb0fc`)

**Input**: Let consumers find products and categories without paging through everything: search by
name, filter products by category and price range, and sort products by name or price - in the API
and in the MVC list pages.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Search products by name (Priority: P1)

A consumer types part of a product name and only matching products are listed, with correct paging
totals.

**Why this priority**: The most common way to find an item in a growing list.

**Independent Test**: `GET /api/product?search=widget` returns only products whose name contains
"widget" (any case) and `totalCount` reflects the filtered set. Covered by
`Products_Search_Filters_By_Name` (seed data: "Widget A", "Widget B", "Gadget" gives `totalCount` 2).

**Acceptance Scenarios**:

1. **Given** products "Widget A", "Widget B", "Gadget", **When** `search=widget`, **Then** two items
   match and `totalCount` is 2.
2. **Given** an empty or whitespace-only `search`, **When** the list is requested, **Then** no name
   filter is applied.
3. **Given** a search term with surrounding spaces or different case, **When** requested, **Then** it
   is trimmed and matched case-insensitively as a substring.

---

### User Story 2 - Filter by category and price range, and sort (Priority: P1)

A consumer narrows products to a category and/or a price range and orders the result by name or price,
ascending or descending.

**Why this priority**: Core browsing of a product catalogue.

**Independent Test**: `GET /api/product?minPrice=15&maxPrice=30&sortBy=price&desc=true` returns the
products priced 30 then 20 with `totalCount` 2 (`Products_PriceRange_And_Sort_Desc`).

**Acceptance Scenarios**:

1. **Given** `categoryId`, `minPrice`, `maxPrice` are supplied, **When** listing products, **Then**
   only products matching all supplied filters are returned (price bounds are inclusive).
2. **Given** `sortBy=name` or `sortBy=price` (case-insensitive) with `desc` true/false, **When**
   listing, **Then** results are ordered accordingly with `Id` as the tie-breaker.
3. **Given** no `sortBy` (or an unrecognised value), **When** listing, **Then** results are ordered by
   `Id` ascending, or `Id` descending when `desc=true`.
4. **Given** `minPrice` greater than `maxPrice`, **When** listing, **Then** the API returns `400` with
   a validation problem on `minPrice` ("minPrice cannot be greater than maxPrice.")
   (`Products_MinPrice_Greater_Than_MaxPrice_Returns_400`).

---

### User Story 3 - Search categories by name (Priority: P2)

A consumer searches the category list by name.

**Independent Test**: `GET /api/category?search=category 2` returns `totalCount` 1
(`Categories_Search_Returns_Matching_TotalCount`).

**Acceptance Scenarios**:

1. **Given** seeded categories "Category 1..3", **When** `search=category 2`, **Then** one category
   matches and `totalCount` is 1.
2. **Given** no `search`, **When** listing, **Then** behaviour is unchanged (ordered by `Id`).

---

### User Story 4 - Use search/filter/sort in the MVC pages (Priority: P2)

On the Products and Categories index pages a user can submit a filter form, keep the filters while
paging, and reset them.

**Independent Test**: Manual (see quickstart); no automated MVC tests exist.

**Acceptance Scenarios**:

1. **Given** the Categories page, **When** the user submits a name, **Then** the list is filtered and
   Previous/Next/page links keep the `search` value.
2. **Given** the Products page, **When** the user submits name, category, min/max price, sort field and
   "Desc", **Then** the filtered list is shown, the form shows the current values and paging links keep
   every filter.
3. **Given** active filters, **When** the user clicks Reset, **Then** the unfiltered first page is shown.

---

### Edge Cases

- `page` below 1 is raised to 1; `pageSize` is clamped to 1-50 (existing rule, unchanged).
- `minPrice` equal to `maxPrice` is allowed; the 400 only applies when min is strictly greater.
- A `categoryId` that matches nothing returns an empty page with `totalCount` 0 (no error).
- `search` is a substring match on name only (not description or other fields).
- Unknown `sortBy` values are ignored rather than rejected.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: `GET /api/product` MUST accept optional `search`, `categoryId`, `minPrice`, `maxPrice`,
  `sortBy` (`name` | `price`) and `desc` (default false) in addition to `page`/`pageSize`.
- **FR-002**: `search` MUST be trimmed and matched as a case-insensitive substring of the name; blank
  values MUST be ignored.
- **FR-003**: `categoryId`, `minPrice` (>=) and `maxPrice` (<=) MUST filter results when supplied.
- **FR-004**: Sorting MUST support `name` and `price` ascending/descending with `Id` ascending as the
  tie-breaker; the default MUST be `Id` ascending (descending when `desc=true`).
- **FR-005**: `minPrice > maxPrice` MUST return `400` with a validation problem keyed `minPrice`.
- **FR-006**: `GET /api/category` MUST accept optional `search` with the same matching rules and keep
  ordering by `Id`.
- **FR-007**: `totalCount` MUST reflect the filtered set so paging stays correct.
- **FR-008**: The MVC Products and Categories index pages MUST provide the filter form and preserve
  filters in pagination links; the MVC client MUST pass every parameter to the API.
- **FR-009**: The pre-existing response shape and `page`/`pageSize` behaviour MUST NOT change.

### Key Entities

- **Product**: `Id`, `Name`, `Price`, `Quantity`, `CategoryId` (unchanged).
- **Category**: `Id`, `Name` (unchanged).
- **List query**: the set of optional query parameters above (not persisted).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The four tests `Products_Search_Filters_By_Name`, `Products_PriceRange_And_Sort_Desc`,
  `Products_MinPrice_Greater_Than_MaxPrice_Returns_400` and `Categories_Search_Returns_Matching_TotalCount`
  pass.
- **SC-002**: Existing list calls with no new parameters return the same items and order as before.
- **SC-003**: In the MVC pages, paging while filtered never loses the active filters (manual check).

## Assumptions

- List endpoints remain public (`GET` is not protected, per `003`).
- Filtering is done in the database query (EF Core); no caching or indexing was added.
- MVC page size stays at 5 (default of the client).

## Known Gaps

- MVC filter forms and pagination links have no automated tests and were not exercised in a browser
  while documenting.
- No test covers `categoryId`, `minPrice`-only/`maxPrice`-only, `sortBy=name`, ascending price, or the
  default/unknown `sortBy` fallback.
- No index was added on `Name` or `Price`; `ToLower().Contains(...)` queries may scan the table on
  large data sets.
- The MVC form posts `sortBy` values `id`, `name` and `price`; "Sort: Id" relies on the API's default
  fallback.
