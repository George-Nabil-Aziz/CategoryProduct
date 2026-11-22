# Data Model: API Quick Fixes

**No schema change.** No model class was added or modified and no EF Core migration was created
(Constitution Principle II is therefore not triggered).

## Existing entities used

| Entity | Use in this feature |
|---|---|
| `Product` | `Product.CategoryId` is queried (`AnyAsync`) to decide whether a category may be deleted. |
| `Category` | Target of the delete guard. |

## Configuration

| Key | Default | Meaning |
|---|---|---|
| `RateLimiting:LoginPermitLimit` | `5` | Login requests permitted per client IP per 1-minute fixed window |

## Transient types (not persisted)

- MVC `ApiResult.Conflict` (`string?`): 409 detail text.
- MVC `LoginResult.TooManyRequests`: static sentinel meaning "API returned 429".
