# Data Model: Browser end-to-end tests

## Schema

**No schema change.** No entity, column, index or migration is added or changed; production models, DTOs and the MVC models are untouched. The suite reads and writes ordinary application data only through the browser UI.

## Data the tests create (through the UI, never directly in SQL)

| Data | Created by | Naming | Lifetime |
|---|---|---|---|
| User (`Role = User`) | Register form | `E2EData.Name("u")` -> `e2e-u-<8 hex>` (matches `^[A-Za-z0-9._-]+$`, 3-50), password `E2EData.Password()` = `E2e-<24 hex>-Aa1` (>= 8 chars) | stays in the database; thrown away with the stack (`down -v`) |
| Category | Admin, Categories/Create | `e2e-cat-<8 hex>` | same |
| Product | Admin, Products/Create | `e2e-prod-<8 hex>`, price `12.50`, quantity 5 (stock tests) | same |
| Product image | Admin upload | in-memory 1x1 PNG, never written to the repository | stored on the API image volume of the stack |
| Order, Payment | user checkout and pay | derived | same |

The Admin is the 023 bootstrap admin (not created by tests).

## Test-side types (not persisted)

- `E2EEnvironment`: `BaseUrl` (Uri, trailing slash normalized), `AdminUserName`, `AdminPassword`, `Headed`, `SlowMoMs`, `ResultsDir`; `IsConfigured` (base URL set).
- `E2EData`: `Name(prefix)`, `Password()`.
- `TestImage.Png`: `byte[]` of a valid 1x1 PNG.

## Compatibility

Re-running the suite on a stack that already contains earlier test data is safe because names are unique per run and tests never assume empty lists; the dashboard test asserts structure, not totals.
