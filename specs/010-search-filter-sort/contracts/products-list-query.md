# Contract: GET /api/product (list with search, filter, sort)

**Auth**: none (public read, unchanged).

**Request**: `GET /api/product?page=&pageSize=&search=&categoryId=&minPrice=&maxPrice=&sortBy=&desc=`
(all optional).

| Parameter | Rule |
|---|---|
| `page` | default 1, values below 1 become 1 |
| `pageSize` | default 5, clamped to 1-50 |
| `search` | trimmed, case-insensitive substring of `Name`; blank ignored |
| `categoryId` | exact match on `CategoryId` |
| `minPrice` | `Price >= minPrice` |
| `maxPrice` | `Price <= maxPrice` |
| `sortBy` | `name` or `price` (case-insensitive); anything else = `Id` order |
| `desc` | default false; with no/unknown `sortBy` gives `Id` descending |

Ordering: `name`/`price` ascending or descending, then `Id` ascending.

**Responses**

| Status | When | Body |
|---|---|---|
| `200 OK` | Valid query | Paged result: `items` (product DTOs: id, name, price, quantity, categoryId) and `totalCount` of the filtered set (plus the pre-existing paging fields, unchanged) |
| `400 Bad Request` | `minPrice > maxPrice` | ValidationProblemDetails with error on `minPrice`: "minPrice cannot be greater than maxPrice." |

Example from tests: `?minPrice=15&maxPrice=30&sortBy=price&desc=true` with the seed data returns
prices `[30, 20]`, `totalCount` 2.
