# Contract: MVC list pages (search/filter/sort)

## GET /Products (`ProductsController.Index`)

Query: `page` (default 1), `search`, `categoryId`, `minPrice`, `maxPrice`, `sortBy`, `desc`.

- API call: `CategoryProductApiClient.GetProductsAsync(page, 5, search, categoryId, minPrice,
  maxPrice, sortBy, desc)` -> `GET api/product?page=&pageSize=5&search=&categoryId=&minPrice=&maxPrice=&sortBy=&desc=`
  (all parameters always appended; text URL-escaped, decimals invariant culture, `desc` lower-case).
- Also calls `GetAllCategoriesAsync()` to fill the category dropdown (`ViewBag.Categories`) and to
  resolve category names for rows.
- View `Views/Products/Index.cshtml`: GET form with name, category (with "All categories"), min/max
  price, sort (`id`, `name`, `price`), Desc checkbox, Filter button and Reset link; paging links carry
  all filters through `asp-route-*`.

## GET /Categories (`CategoriesController.Index`)

Query: `page` (default 1), `search`.

- API call: `GetCategoriesAsync(page, search: search)` -> `GET api/category?page=&pageSize=5&search=`.
- View `Views/Categories/Index.cshtml`: GET form with name box, Search button and Reset link; paging
  links carry `search`.

No MVC automated tests exist for these pages.
