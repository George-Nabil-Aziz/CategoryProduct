# Data Model: Cart and Orders

## Enum `OrderStatus` (`CategoryProduct.API/Models/OrderStatus.cs`)

`Pending`, `Confirmed`, `Cancelled`. Stored as the enum name string (max 20). Static helper `OrderStatusExtensions.TryParseName(string?, out OrderStatus)` matches `Enum.GetNames` case-insensitively and rejects numeric strings.

Allowed transitions: `Pending -> Confirmed`, `Pending -> Cancelled`. `Confirmed` and `Cancelled` are terminal.

## Entity `CartItem` (`CategoryProduct.API/Models/CartItem.cs`)

| Property | Type | Rules |
|---|---|---|
| `Id` | int | PK |
| `UserId` | int | required FK to `Users`, `OnDelete(Cascade)` |
| `User` | `User?` | navigation |
| `ProductId` | int | required FK to `Products`, `OnDelete(Cascade)` |
| `Product` | `Product?` | navigation |
| `Quantity` | int | 1..100 (enforced by DTOs and the merge check) |
| `AddedAtUtc` | DateTime | `DateTime.UtcNow` when the line is created |

Unique index (`UserId`, `ProductId`).

## Entity `Order` (`CategoryProduct.API/Models/Order.cs`)

| Property | Type | Rules |
|---|---|---|
| `Id` | int | PK |
| `UserId` | int | required FK to `Users`, `OnDelete(Restrict)` |
| `User` | `User?` | navigation |
| `Status` | `OrderStatus` | `HasConversion<string>()`, `HasMaxLength(20)`, required |
| `Total` | decimal | `HasColumnType("decimal(18,2)")`, sum of item `LineTotal` |
| `CreatedAtUtc` | DateTime | set at checkout |
| `UpdatedAtUtc` | DateTime | set at checkout, refreshed on each status change |
| `ConcurrencyStamp` | Guid | `= Guid.NewGuid()`, `IsConcurrencyToken()`, regenerated on every status change |
| `Items` | `List<OrderItem>` | navigation (collection) |

Index (`UserId`, `CreatedAtUtc`).

## Entity `OrderItem` (`CategoryProduct.API/Models/OrderItem.cs`)

| Property | Type | Rules |
|---|---|---|
| `Id` | int | PK |
| `OrderId` | int | required FK to `Orders`, `OnDelete(Cascade)` |
| `Order` | `Order?` | navigation |
| `ProductId` | int? | FK to `Products`, `OnDelete(SetNull)`; no navigation needed (`HasOne<Product>().WithMany().HasForeignKey(i => i.ProductId)`) |
| `ProductName` | string | snapshot, `HasMaxLength(100)`, required |
| `UnitPrice` | decimal | snapshot, `decimal(18,2)` |
| `Quantity` | int | > 0 |
| `LineTotal` | decimal | `UnitPrice * Quantity`, `decimal(18,2)` |

Index on `OrderId` (EF default) and on `ProductId` (EF default).

## `ApplicationDbContext`

Add `DbSet<CartItem> CartItems`, `DbSet<Order> Orders`, `DbSet<OrderItem> OrderItems` and the configuration above in `OnModelCreating`.

## Cascade path check (SQL Server)

| From | To | Behavior |
|---|---|---|
| Users | CartItems | Cascade |
| Users | Orders | Restrict |
| Users | RefreshTokens (existing) | Cascade |
| Users | StockMovements (planned 018) | SetNull |
| Categories | Products (existing) | Cascade |
| Products | CartItems | Cascade |
| Products | OrderItems | SetNull |
| Products | StockMovements (planned 018) | Cascade |
| Orders | OrderItems | Cascade |

No table is reachable by two cascading or set-null paths from the same root table, so SQL Server accepts the schema. The generated migration (task T010) and the quickstart SQL Server step are the proof.

## Migration

`dotnet ef migrations add AddCartAndOrders --project CategoryProduct.API` creates the three tables, the two indexes above and the foreign keys. Not hand-edited; no `database update`. It must run after the planned `AddInventory` migration (it uses no 018 column, but the snapshot must contain 018's model so the diff contains only 019's changes).

## DTOs (`CategoryProduct.API/Dtos`)

- `AddCartItemRequestDto(int ProductId, int Quantity)`: `[Range(1, int.MaxValue)] ProductId`, `[Range(1, 100)] Quantity`.
- `UpdateCartItemRequestDto(int Quantity)`: `[Range(1, 100)]`.
- `CartItemResponseDto(int ProductId, string ProductName, decimal UnitPrice, int Quantity, decimal LineTotal, int AvailableQuantity)`.
- `CartResponseDto(IReadOnlyList<CartItemResponseDto> Items, int ItemCount, decimal Total)` (`ItemCount` = number of lines).
- `UpdateOrderStatusRequestDto(string Status)`: `[Required(AllowEmptyStrings = false)]`.
- `OrderItemResponseDto(int Id, int? ProductId, string ProductName, decimal UnitPrice, int Quantity, decimal LineTotal)`.
- `OrderResponseDto(int Id, int UserId, string? UserName, string Status, decimal Total, DateTime CreatedAtUtc, DateTime UpdatedAtUtc, IReadOnlyList<OrderItemResponseDto> Items)`.
- `OrderSummaryDto(int Id, int UserId, string? UserName, string Status, decimal Total, int ItemCount, DateTime CreatedAtUtc, DateTime UpdatedAtUtc)`.
- Shortage detail in the 409 extension: `{ productId, productName, requested, available }`.

## MVC models (own copies, `CategoryProduct.MVC/Models`)

`CartModel`, `CartLine`, `OrderModel`, `OrderLine`, `OrderSummaryItem` mirroring the DTOs; `ApiResult<T> : ApiResult` with `T? Value`; request records `AddCartItemRequest`, `UpdateCartItemRequest`, `UpdateOrderStatusRequest` in `ApiModels.cs`.

## State rules

- A product is never reduced below 0 by checkout; a cancel adds exactly the ordered quantity back for lines whose product still exists.
- For every non-cancelled order and every product that still exists: the product's `Sale` movements minus its `OrderCancelled` movements equal the ordered quantities of non-cancelled orders (modulo deleted products).
- `Order.Total` equals the sum of its `LineTotal` values and never changes after creation.
- An order's status only ever moves `Pending -> Confirmed` or `Pending -> Cancelled`; its stock is restored exactly once.
- A user's cart has at most one line per product.
