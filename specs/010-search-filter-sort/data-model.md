# Data Model: Search, Filter and Sort

**No schema change.** No model class was added or modified and no EF Core migration was created
(Constitution Principle II is not triggered).

## Existing entities queried

| Entity | Fields used |
|---|---|
| `Product` | `Name` (search, sort), `Price` (range filter, sort), `CategoryId` (filter), `Id` (default order, tie-breaker) |
| `Category` | `Name` (search), `Id` (order) |

## Query parameters (transient)

| Parameter | Type | Applies to | Default |
|---|---|---|---|
| `page` | int | both | 1 (min 1) |
| `pageSize` | int | both | 5 (clamped 1-50) |
| `search` | string? | both | none |
| `categoryId` | int? | products | none |
| `minPrice` | decimal? | products | none |
| `maxPrice` | decimal? | products | none |
| `sortBy` | string? (`name`, `price`) | products | `Id` order |
| `desc` | bool | products | false |

## Test seed data (`CategoryProduct.API.Tests/ApiFactory.cs`)

Categories "Category 1".."Category 3"; products "Widget A" (30, category 1), "Widget B" (10,
category 1), "Gadget" (20, category 2).
