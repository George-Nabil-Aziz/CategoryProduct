# Quickstart: Validating MVC Login

Manual verification (no automated MVC tests).

## Prerequisites

- API running with a signing key and an up-to-date database:
  ```
  dotnet user-secrets set "Jwt:Key" "<random string of at least 32 characters>"   # run once inside CategoryProduct.API/
  dotnet ef database update --project CategoryProduct.API
  dotnet run --project CategoryProduct.API
  ```
- At least one pre-provisioned user (see the 003 quickstart; the 003 migrations include a seed test user). From 008 a user needs the Admin role to complete writes.
- `CategoryProduct.MVC/appsettings.json` `Api:BaseUrl` points at the API, then:
  ```
  dotnet run --project CategoryProduct.MVC
  ```
  Serve over HTTPS (the cookie is `Secure`).
- API tests still pass: `dotnet test CategoryProduct.API.Tests`.

## Scenarios

1. **Anonymous reads (US2.4)**: signed out, open `/Categories` and `/Products` -> lists and details work; no "Create New" link.
2. **Redirect to login (US2.1)**: open `/Categories/Create` -> redirected to `/Account/Login?ReturnUrl=%2FCategories%2FCreate`.
3. **Bad credentials (US1.2)**: submit a wrong password -> "Invalid username or password." and no cookie. Submit blank fields -> field errors, no API call.
4. **Sign in (US1.1)**: valid credentials -> redirected back to `/Categories/Create` (or Categories Index when opened directly); the nav shows "Hello, <name>" and a Logout button.
5. **Write carries the token (US2.2)**: create a category -> redirect to Index and the new row exists. Confirm in the API logs or a proxy that the `POST api/category` carried `Authorization: Bearer`.
6. **Expired token (US2.3)**: set `Jwt:ExpiryMinutes` to `1` for the API, sign in, wait past expiry, submit a write -> you are signed out and returned to the login page with the original path as `returnUrl`.
7. **Logout (US1.3)**: click Logout -> redirected to Categories Index; `/Categories/Create` redirects to login again.

## Success criteria mapping

- SC-001 -> scenarios 2 and 6. SC-002 -> scenarios 4 and 5.
