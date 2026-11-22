# Contract: product image endpoints (concurrency)

Only the failure behavior changes. Routes, auth (`Admin`), validation and success responses are as in `specs/017-product-images/contracts`.

## POST api/product/{id}/image

| Situation | Status | Disk |
|---|---|---|
| Success | 200 product response (unchanged) | new file kept, old file deleted |
| Validation failures (400), unknown id (404) | unchanged | no file written |
| `DbUpdateConcurrencyException` on save (product changed or removed since read) | **409** `ProblemDetails` (`Status` 409, `Title` "Conflict", `Detail` "The product was changed by another request. Reload and try again.") | **new file deleted**, old file and reference untouched |
| Any other exception on save | 500 via the global exception handler (unchanged) | new file deleted |

## DELETE api/product/{id}/image

| Situation | Status | Disk |
|---|---|---|
| Image present, success | 204 (unchanged), file deleted after the save | file deleted |
| No image | 204 (unchanged) | n/a |
| `DbUpdateConcurrencyException` on save | **409** same `ProblemDetails` | file and reference kept |
| Unknown id | 404 (unchanged) | n/a |

The 409 body is the existing `ProductController.ConcurrencyConflict()` result, shared with Update, Delete and stock movements.
