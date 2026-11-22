# Contract: Product image endpoints (CategoryProduct.API)

Controller `ProductController`, route `api/[controller]` (`api/product`).

## POST api/product/{id}/image

- Auth: Bearer JWT, role `Admin` (`[Authorize(Roles = "Admin")]`).
- Request: `multipart/form-data`, single file field `file`. Request size limit 2,359,296 bytes.

| Status | When |
|---|---|
| 200 | stored; body `ProductResponseDto` with non-null `imageUrl` |
| 400 | `application/problem+json` validation problem, errors key `file`: missing/empty, bad extension, bad content type, signature mismatch, over `MaxBytes` |
| 401 | no/invalid token |
| 403 | non-Admin |
| 404 | unknown product |
| 413 | body exceeds the request size limit (problem details) |

Side effects: new file `{id}-{Guid:N}.{ext}` written, `ImageFileName` updated, previous file deleted best effort. Order of checks: auth, product exists (404), file validation (400).

## DELETE api/product/{id}/image

- Auth: Admin. Responses: 204 (removed, or nothing to remove), 401, 403, 404 (unknown product).

## GET api/product/{id}/image

- Auth: anonymous.
- 200: file bytes, `Content-Type` `image/jpeg|png|webp`, `ETag`, `Cache-Control: no-cache`, `X-Content-Type-Options: nosniff`; honors `If-None-Match` (304).
- 404: unknown product, no image, or file missing on disk.

## Changed shape: ProductResponseDto

Adds `imageUrl` (string or null) to `GET api/product`, `GET api/product/{id}`, `POST api/product`, `PUT api/product/{id}`, `GET api/category/{catId}/products`, and the upload response. Example: `"imageUrl": "https://localhost:7183/api/product/7/image"`.

## Configuration

```json
"ProductImages": { "StoragePath": "App_Data/product-images", "MaxBytes": 2097152 }
```

Environment override: `ProductImages__StoragePath`, `ProductImages__MaxBytes`.

## Unchanged

`DELETE api/product/{id}` still returns 200 and now also removes the image file.
