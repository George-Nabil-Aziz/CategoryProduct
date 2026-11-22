# Contract: Paged list endpoints (changed)

Base routes: `api/Category`, `api/Product`. Both `GET` list endpoints changed from returning a bare JSON array to returning a paging envelope. Public (no `Authorization` required).

## GET /api/Category?page={page}&pageSize={pageSize}

## GET /api/Product?page={page}&pageSize={pageSize}

**Query parameters**

| Name | Default | Rule |
|------|---------|------|
| `page` | `1` | Values below 1 are treated as 1 |
| `pageSize` | `5` | Clamped to 1..50 |

**Success**: `200 OK` with `PagedResponseDto<T>` (`T` = `CategoryResponseDto { Id, Name }` or `ProductResponseDto { Id, Name, Price, Quantity, CategoryId }`):

```json
{
  "items": [ { "id": 1, "name": "..." } ],
  "page": 1,
  "pageSize": 5,
  "totalCount": 12,
  "totalPages": 3
}
```

- Items are ordered by `Id` ascending.
- `page` beyond the last page returns `items: []` with the real `totalCount`/`totalPages`.
- Empty table returns `totalCount: 0`, `totalPages: 0`.

**Breaking change**: consumers that parsed a bare array must read `items` instead (the MVC app was adapted in 006).

## Unchanged

`GET /api/Category/{id}`, `GET /api/Product/{id}` and `GET /api/Category/{catId}/products` (unpaged array) keep their shapes.
