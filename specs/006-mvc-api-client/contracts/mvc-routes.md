# Contract: MVC routes (CategoriesController, ProductsController)

Default route: `{controller=Categories}/{action=Index}/{id?}`. All actions below were public at this commit (no `[Authorize]`; added in 007). Every data operation goes through `CategoryProductApiClient`; see `api-calls.md` for the HTTP calls.

## Categories (`/Categories/...`)

| Route | Verb | Behavior |
|-------|------|----------|
| `Index?page=1` | GET | Fetches one page (5 per page) via the API; passes `CurrentPage`/`TotalPages` to the view; `page < 1` becomes 1 |
| `Details/{id}` | GET | 404 if `id` missing or API returns 404; otherwise shows the category with its products |
| `Create` | GET | Empty form |
| `Create` | POST | Binds `Id,Name`; invalid model re-renders the form; API success redirects to `Index`; API 404 returns 404; API 401/403 or 400 adds model errors and re-renders |
| `Edit/{id}` | GET | 404 if the category is missing; otherwise the form |
| `Edit/{id}` | POST | 404 if route `id` differs from model `Id`; same result handling as Create |
| `Delete/{id}` | GET | 404 if missing; otherwise the confirmation page |
| `Delete/{id}` | POST (`DeleteConfirmed`) | API success redirects to `Index`; 404 returns 404; failures add model errors and re-render the confirmation page |

POST actions use `[ValidateAntiForgeryToken]`.

## Products (`/Products/...`)

Same set of routes (`Index`, `Details`, `Create`, `Edit`, `Delete`) with these differences:

- `Index` also fetches all categories (50 per page, looping until `TotalPages`) to fill `CategoryName` for each row.
- `Details` and `Delete` look up the product's category name with `GET api/category/{id}`.
- `Create`/`Edit` forms load the category list into a dropdown (`ViewData["CategoryId"]`); POST binds `Id,Name,Price,Quantity,CategoryId`; on any failure the dropdown is reloaded with the selected category.
- A 400 from the API (for example "Category does not exist." on `CategoryId`) is shown against the field it names.

## Error messages

- 401 or 403 from the API on a write: model error "You must be signed in to make changes."
- 400 from the API: each message in the `errors` object is added to the model state under its field name.
