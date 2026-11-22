# Data Model: API Hardening

**No schema change.** No model class was edited and no EF Core migration was added (Constitution Principle II is not triggered).

## New DTO

`CategoryProduct.API/Dtos/PagedResponseDto.cs`

| Field | Type | Notes |
|-------|------|-------|
| `Items` | `IReadOnlyList<T>` | The page of results, ordered by `Id` |
| `Page` | `int` | The page served (after `Math.Max(page, 1)`) |
| `PageSize` | `int` | The page size used (after clamping to 1..50) |
| `TotalCount` | `int` | Total rows in the table |
| `TotalPages` | `int` | `ceil(TotalCount / PageSize)`; 0 when the table is empty |

Used as `PagedResponseDto<CategoryResponseDto>` and `PagedResponseDto<ProductResponseDto>`.

## Validation behavior (no new fields)

`ProductRequestDto.CategoryId` keeps its 002 rule (`[Range(1, int.MaxValue)]`); this feature adds an existence check in the controller. The 400 response contains an `errors.CategoryId` entry `["Category does not exist."]`.

## Configuration

| Key | Rule |
|-----|------|
| `Jwt:Key` | Required, at least 32 characters; supplied via user-secrets or `Jwt__Key`; never present in `appsettings.json` |
| `Jwt:Issuer`, `Jwt:Audience`, `Jwt:ExpiryMinutes` | Unchanged, remain in `appsettings.json` |

## Unaffected

`Category`, `Product`, `User`, `CategoryRequestDto`, `ProductRequestDto`, `LoginRequestDto`, `LoginResponseDto`.
