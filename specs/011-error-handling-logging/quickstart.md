# Quickstart: Verifying Error Handling and Logging

## Automated

```bash
dotnet test CategoryProduct.API.Tests
```

Relevant test: `UnhandledException_Returns500ProblemJson` in
`CategoryProduct.API.Tests/ErrorHandlingTests.cs`.

## Manual - not exercised when this spec was written

Prerequisites: SQL Server reachable, `Jwt:Key` (>= 32 chars) via user secrets or the `Jwt__Key`
environment variable (never commit it), `Api:BaseUrl` configured for the MVC app.

1. **Logs (API)**: `dotnet run --project CategoryProduct.API`, call any endpoint; expect request lines
   on the console and a `logs/categoryproduct-api-<date>.log` file under the API's working directory.
2. **Logs (MVC)**: `dotnet run --project CategoryProduct.MVC`, open a page; expect
   `logs/categoryproduct-mvc-<date>.log`.
3. **Service unavailable page**: stop the API, reload an MVC list page; expect HTTP `503`, the heading
   "Service unavailable", the message "The service is unavailable. Please try again later." and a
   Retry button. The MVC log should contain "API unreachable: ...".
4. Confirm `git status` shows no `logs/` files (git-ignored).
