# Contract: MVC service-unavailable handling

## Trigger (API client, `Services/ApiErrorHandler.cs`)

Applies to every call made through `CategoryProductApiClient` (`Api:BaseUrl`):

| Condition | Action |
|---|---|
| `HttpRequestException` (API unreachable) | log "API unreachable: {Method} {Uri}", throw `ApiUnavailableException` |
| `TaskCanceledException` while the caller's token is not cancelled (timeout) | same as above |
| Response status >= 500 | log "API returned {StatusCode} for {Method} {Uri}", dispose response, throw `ApiUnavailableException` |
| Any other response (2xx, 4xx) | passed through unchanged |

## Result (`ApiUnavailableFilter`, global MVC exception filter)

- Renders view `Views/Shared/ServiceUnavailable.cshtml` with HTTP status `503`.
- `ViewData["Message"]` = "The service is unavailable. Please try again later."
- View content: heading "Service unavailable", warning alert with the message, a Retry button that
  reloads the page (`javascript:location.reload()`).
- Marks the exception handled; other exception types are not touched.

## Routes affected

Every MVC route that calls the API (Categories, Products, Account login, ...); there is no dedicated
route for the page.
