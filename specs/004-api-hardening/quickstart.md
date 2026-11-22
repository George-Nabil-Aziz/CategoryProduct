# Quickstart: Validating API Hardening

## Prerequisites

- From `CategoryProduct.API/`, set the signing key once (never commit it):
  ```
  dotnet user-secrets set "Jwt:Key" "<random string of at least 32 characters>"
  ```
- SQL Server reachable via `ConnectionStrings:DefaultConnection`, then from the repo root bring the database up to date (this feature added no migration; later features did):
  ```
  dotnet ef database update --project CategoryProduct.API
  ```
- At least one user to log in with (see the 003 quickstart; from 008 the user must be an Admin to write).

## Automated

```
dotnet test CategoryProduct.API.Tests
```

Includes `PostProduct_NonExistentCategory_Returns400` and `GetCategories_PageSizeAbove50_IsClamped` (see 005).

## Manual scenarios

1. **Missing key (US2)**: with no user-secret and no `Jwt__Key`, run `dotnet run --project CategoryProduct.API` and expect startup to fail with the `InvalidOperationException` message. Repeat with a 10-character key (same failure) and a 32+ character key (starts).
2. **Repository check (SC-002)**: inspect `CategoryProduct.API/appsettings.json`; the `Jwt` section has `Issuer`, `Audience`, `ExpiryMinutes` and no `Key`.
3. **Bad CategoryId (US1)**: log in, then `POST /api/Product` with `"categoryId": 999999` and a bearer token. Expect `400` with `errors.CategoryId = ["Category does not exist."]`, and `GET /api/Product` shows nothing added. Repeat with `PUT /api/Product/{id}`; expect `400` and the product unchanged.
4. **Valid CategoryId**: same requests with an existing category return `200 OK`.
5. **Paging (US3)**: `GET /api/Category?page=1&pageSize=100` returns `pageSize: 50`; `?pageSize=0` returns `pageSize: 1`; `?page=-5` returns `page: 1`; a `page` past the end returns empty `items` with the correct `totalCount`.
6. **Regression (US4)**: create, update and delete a category and a product; status codes match the contracts.

## Success criteria mapping

- SC-001 -> scenario 3. SC-002 -> scenarios 1-2. SC-003 -> scenario 5.
