# Contract: GET /api/category (list with search)

**Auth**: none (public read, unchanged).

**Request**: `GET /api/category?page=&pageSize=&search=` (all optional).

| Parameter | Rule |
|---|---|
| `page` | default 1, values below 1 become 1 |
| `pageSize` | default 5, clamped to 1-50 |
| `search` | trimmed, case-insensitive substring of `Name`; blank ignored |

Ordering: `Id` ascending.

**Responses**

| Status | When | Body |
|---|---|---|
| `200 OK` | Always for a valid request | Paged result: `items` and `totalCount` of the filtered set (other paging fields unchanged) |

Example from tests: `?search=category 2` with the seed data returns `totalCount` 1.
