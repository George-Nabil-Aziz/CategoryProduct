# Contract: MVC routes (CategoryProduct.MVC)

| Route | Verb | Access | Behavior |
|---|---|---|---|
| `Products/Image/{id}` | GET | anonymous | Fetch bytes via `CategoryProductApiClient.GetProductImageAsync`; `File(bytes, contentType)`; 404 if none |
| `Products/UploadImage/{id}` | POST | `Admin`, anti-forgery | `IFormFile? file`; `[RequestSizeLimit(2_359_296)]`; forwards multipart to `POST api/product/{id}/image`; success redirects to `Details`; 400 re-renders `Details` with messages; 401 signs out and redirects to login; 403/404 handled like the other write actions |
| `Products/RemoveImage/{id}` | POST | `Admin`, anti-forgery | calls `DELETE api/product/{id}/image`; redirects to `Details` |

Views: `Views/Products/Details.cshtml` (image, Admin upload form with `accept=".jpg,.jpeg,.png,.webp"` and `enctype="multipart/form-data"`, remove button, validation summary), `Views/Products/Index.cshtml` (thumbnail, max height 48px). The `<img src>` is always `@Url.Action("Image", "Products", new { id })`.

API client additions in `Services/CategoryProductApiClient.cs`:
- `GetProductImageAsync(int id)` returns `(byte[] Bytes, string ContentType)?` (null on 404).
- `UploadProductImageAsync(int id, Stream content, string fileName, string contentType)` returns `ApiResult`.
- `DeleteProductImageAsync(int id)` returns `ApiResult`.
