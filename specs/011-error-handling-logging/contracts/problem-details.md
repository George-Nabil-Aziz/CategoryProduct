# Contract: API unexpected-error response

Applies to any API endpoint that throws an unhandled exception.

**Response**: `500 Internal Server Error`, `Content-Type: application/problem+json`

```json
{
  "title": "An unexpected error occurred.",
  "status": 500,
  "traceId": "<activity id or trace identifier>"
}
```

- The exception message and stack trace are never included (asserted by test with the message
  "boom secret").
- The exception is logged server-side as an error with the HTTP method and request path.

## Pipeline

`AddProblemDetails()`, `AddExceptionHandler<GlobalExceptionHandler>()`, `UseExceptionHandler()`,
`UseStatusCodePages()` and `UseSerilogRequestLogging()` are registered ahead of the rest of the
pipeline in `CategoryProduct.API/Program.cs`. Existing endpoint contracts are unchanged.
