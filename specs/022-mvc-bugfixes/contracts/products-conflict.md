# Contract: Products show API 409 details

## Change

`ProductsController.AddErrors` gets the same branch as `CategoriesController.AddErrors`, between the Forbidden check and the `Errors` loop:

```csharp
if (result.Conflict != null)
    ModelState.AddModelError(string.Empty, result.Conflict);
```

No other change. `result.Conflict` is the `detail` of the API's 409 body, or "The request conflicts with existing data." (set by `CategoryProductApiClient`).

## Where it shows

| Action | API call | Re-rendered view | Summary tag |
|---|---|---|---|
| `Create` (POST) | `POST api/product` | `Products/Create` | `asp-validation-summary="ModelOnly"` |
| `Edit` (POST) | `PUT api/product/{id}` | `Products/Edit` | `ModelOnly` |
| `DeleteConfirmed` | `DELETE api/product/{id}` | `Products/Delete` (product reloaded) | `ModelOnly` |
| `UploadImage` | `POST api/product/{id}/image` | `Products/Details` | `All` |
| `RemoveImage` | `DELETE api/product/{id}/image` | `Products/Details` | `All` |

All five already call `AddErrors(result)` for results that are not Success, NotFound or Unauthorized, so no controller action changes.

## Tests

| Where | Case | Expect |
|---|---|---|
| `ProductsTests` | create 409 `ApiJson.ConflictProblem("Product name already exists.")` | 200; `HtmlHelpers.FieldError(html, "")` contains that text; API called once |
| `ProductsTests` | edit 409 (`Conflict while updating.`) | 200; summary contains the text |
| `ProductsTests` | delete 409 (`Product is referenced by orders.`) | 200; Delete page ("Are you sure you want to delete this?" present) with the text |
| `ProductImageTests` | upload 409 (`Image conflict on upload.`) | 200 Details with the text |
| `ProductImageTests` | remove image 409 (NEW; scripts `GET api/product/{id}` for the reload and `DELETE api/product/{id}/image` 409, text `Image conflict on remove.`) | 200 Details with the text |
| `ProductsTests` | 409 with an empty or non-JSON body | 200; summary contains "The request conflicts with existing data." |
| existing | 400 field errors, 403, 404, 401 session-expiry tests | unchanged and green |

The four existing `*_409_rerenders_without_exception` tests are renamed `*_409_shows_detail` and assert the text; the detail strings are unique per test so a wrong binding cannot pass by accident.
