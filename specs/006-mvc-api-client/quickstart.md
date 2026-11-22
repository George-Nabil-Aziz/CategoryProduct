# Quickstart: Validating MVC Consumes the API

Manual verification (there are no automated MVC tests).

## Prerequisites

- API configured and running (see the 004 quickstart for the JWT key via user-secrets):
  ```
  dotnet user-secrets set "Jwt:Key" "<random string of at least 32 characters>"   # run once inside CategoryProduct.API/
  dotnet ef database update --project CategoryProduct.API
  dotnet run --project CategoryProduct.API
  ```
- `CategoryProduct.MVC/appsettings.json` `Api:BaseUrl` points at the running API (default `https://localhost:7183/`).
- Start the MVC site: `dotnet run --project CategoryProduct.MVC`.
- API tests still pass: `dotnet test CategoryProduct.API.Tests`.

## Scenarios

1. **Lists (US1.1)**: open `/Categories` and `/Products`; rows come from the API, 5 per page, and the pager moves between pages. Products show their category name.
2. **Details (US1.1, US1.4)**: open `/Categories/Details/{id}` (shows the category and its products) and `/Products/Details/{id}`; an unknown id returns 404.
3. **Validation (US1.3)**: open `/Products/Create`, submit a too-short name -> client/server errors on the form. (With a category that does not exist, the API's "Category does not exist." appears on `CategoryId`.)
4. **Writes without sign-in (US1.2)**: at this commit, submit a valid create/edit/delete -> the form shows "You must be signed in to make changes." (the API returned 401). Sign-in arrives in 007; with it (and Admin role from 008) the same actions succeed and redirect to the Index.
5. **No database (US2)**: confirm `CategoryProduct.MVC.csproj` has no EF Core `PackageReference`, `CategoryProduct.MVC/Data/` is gone, and `appsettings.json` has `Api:BaseUrl` but no `ConnectionStrings`.
6. **Stop the API** and reload `/Categories` -> an unhandled error (known gap; later improved, see "Later changes" in the spec).

## Success criteria mapping

- SC-001 -> scenario 5 plus the lists in scenario 1.
- SC-002 -> scenarios 1-2 (every page reachable; Categories Index has no product-count column).
