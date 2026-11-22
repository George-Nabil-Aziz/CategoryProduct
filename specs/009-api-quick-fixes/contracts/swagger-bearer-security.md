# Contract: Swagger/OpenAPI bearer security scheme

Applies to the generated OpenAPI document served by Swashbuckle (Swagger UI enabled in Development).

| Item | Value |
|---|---|
| Scheme name | `Bearer` |
| Type | `http` |
| Scheme | `bearer` |
| Bearer format | `JWT` |
| Description | "Paste the JWT returned by the login endpoint." |
| Requirement | Global: every operation lists the `Bearer` scheme |

Swagger UI therefore shows an **Authorize** button; entered tokens are sent as
`Authorization: Bearer <token>`. No runtime API behaviour changes (server-side `[Authorize]` rules are
unchanged).
