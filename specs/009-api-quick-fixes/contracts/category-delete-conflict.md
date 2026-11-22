# Contract: DELETE /api/category/{id} (conflict rule)

**Auth**: requires a valid bearer token (unchanged by this feature; role rules per `008`).

**Request**: `DELETE /api/category/{id}`, no body.

| Status | When | Body |
|---|---|---|
| success | Category exists and has no products (behaviour unchanged) | unchanged |
| `404` | Category does not exist | unchanged |
| `409 Conflict` | At least one product has `CategoryId == id` | ProblemDetails: `status` 409, `title` "Category has products", `detail` "Delete or move its products first." |
| `401`/`403` | Missing/invalid token or insufficient role | unchanged |

The category and products are not modified on `409`.

## MVC consumer

- Route: `POST /Categories/Delete/{id}` (`DeleteConfirmed`, anti-forgery, Admin role).
- API call: `CategoryProductApiClient.DeleteCategoryAsync` -> `DELETE api/category/{id}`.
- On `409`, `ApiResult.Conflict` = response `detail` (fallback "The request conflicts with existing
  data."); `AddErrors` adds it as a form-level model error and the Delete view is re-rendered.
