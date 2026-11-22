# Contract: API calls added by MVC login

Base address: `Api:BaseUrl`. The API-side contracts are in `specs/003-jwt-authentication/contracts/`.

## Login

`CategoryProductApiClient.LoginAsync(userName, password)`:

- Request: `POST api/account/login` with JSON `{ "userName": ..., "password": ... }` (anonymous).
- `200 OK`: deserialized as `LoginResult { Token, ExpiresAtUtc }` and returned.
- `401 Unauthorized`: returns `null` (shown to the user as "Invalid username or password.").
- Any other non-success status: `EnsureSuccessStatusCode` throws.

## Bearer token on writes

`SendAsync` (used by all six create/update/delete methods listed in `specs/006-mvc-api-client/contracts/api-calls.md`) now builds an `HttpRequestMessage` and calls `ApplyBearerToken(request)`:

- Token source: the current request's `HttpContext.User` claim `access_token` (via `IHttpContextAccessor`).
- If a token exists, the request carries `Authorization: Bearer <token>`; otherwise no header is added.
- The header is set on the individual request, not on `HttpClient.DefaultRequestHeaders`.
- Read calls (`GET`) are unchanged and send no token.

## Status mapping on writes (unchanged from 006)

`401` or `403` -> `ApiResult.Unauthorized` (at this commit both map to the same flag; the controller then signs the user out). 403 is split out in 008.
