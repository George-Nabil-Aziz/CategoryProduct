# Quickstart: Verifying Search, Filter and Sort

## Automated

```bash
dotnet test CategoryProduct.API.Tests
```

Relevant tests (in `CategoryProduct.API.Tests/ApiTests.cs`): `Products_Search_Filters_By_Name`,
`Products_PriceRange_And_Sort_Desc`, `Products_MinPrice_Greater_Than_MaxPrice_Returns_400`,
`Categories_Search_Returns_Matching_TotalCount`.

## Manual (API)

Prerequisites: SQL Server database reachable, `Jwt:Key` (>= 32 chars) from user secrets or the
`Jwt__Key` environment variable; reads need no token. Run `dotnet run --project CategoryProduct.API`.

1. `GET /api/Product?search=<part of a name>` - only matching products, `totalCount` filtered.
2. `GET /api/Product?minPrice=10&maxPrice=50&sortBy=price&desc=true` - in range, highest price first.
3. `GET /api/Product?categoryId=<id>` - only that category.
4. `GET /api/Product?minPrice=50&maxPrice=10` - `400` validation problem on `minPrice`.
5. `GET /api/Category?search=<part of a name>` - matching categories only.

## Manual (MVC) - not exercised when this spec was written

1. Run the API and `dotnet run --project CategoryProduct.MVC`.
2. Open `/Products`: use the filter form (name, category, min/max price, sort, Desc), then page with
   Previous/Next and confirm filters persist; click Reset to clear.
3. Open `/Categories`: search by name, page, Reset.
