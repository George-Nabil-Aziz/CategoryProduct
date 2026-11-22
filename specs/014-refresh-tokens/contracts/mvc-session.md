# Contract: MVC session handling

No new routes. Behavior changes in existing flows of `CategoryProduct.MVC`.

| Flow | Behavior | API call |
|------|----------|----------|
| `POST /Account/Login` and `POST /Account/Register` (after success) | `AuthSessionService.SignInAsync` creates the cookie with claims `Name`, `Role`, `access_token`, `refresh_token`, `access_token_expires_at`; non-persistent; cookie `ExpiresUtc` = refresh token expiry; sets `HttpContext.User` for the current request | `POST api/account/login` |
| Any authenticated API call from `CategoryProductApiClient` | `TokenRefreshHandler`: (1) if the access token expires within 60 seconds, refresh first and use the new bearer; (2) send; (3) on `401`, refresh once per HTTP request and retry once with a cloned request; if refresh fails return the original response | `POST api/account/refresh` (client `api-refresh`) |
| `POST /Account/Logout` | Best-effort API logout with the cookie's refresh token, then cookie sign-out, redirect to `Categories/Index` | `POST api/account/logout` |

## Handler guard conditions

The handler does nothing unless the request has an `Authorization` header and the current user is
authenticated. Request content is buffered so it can be replayed. Failures of the refresh call
(HTTP error, timeout, bad JSON) are logged as warnings and treated as "not refreshed".

## Registration

`CategoryProduct.MVC/Program.cs`: `AuthSessionService` singleton, `TokenRefreshHandler` transient,
named client `api-refresh` (base address `Api:BaseUrl`), handler chained after `ApiErrorHandler`.
