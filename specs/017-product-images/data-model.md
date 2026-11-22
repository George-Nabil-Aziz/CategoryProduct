# Data Model: Product Images

## Product (modified)

`CategoryProduct.API/Models/Product.cs`

| Field | Type | Rule(s) |
|---|---|---|
| `Id`, `Name`, `Price`, `Quantity`, `CategoryId`, `Category` | unchanged | existing |
| `ImageFileName` | `string?` | NULL when no image; max length 100 (`HasMaxLength(100)` in `ApplicationDbContext.OnModelCreating`); only ever a server-generated `{productId}-{Guid:N}.{jpg|png|webp}` |

**Migration** (Constitution II): `dotnet ef migrations add AddProductImage --project CategoryProduct.API` adds a nullable `nvarchar(100)` column `ImageFileName` to `Products`. No backfill. Never hand-edited; `dotnet ef database update` is not run here (016 applies migrations only inside containers).

## ProductResponseDto (modified)

`public record ProductResponseDto(int Id, string Name, decimal Price, int Quantity, int CategoryId, string? ImageUrl = null);`

`ImageUrl` is computed, not stored: absolute URL to `GET api/product/{id}/image` when `ImageFileName` is not null, else null.

## ProductImageOptions (new, configuration)

`CategoryProduct.API/Services/ProductImageOptions.cs`, section `ProductImages`:

| Key | Type | Default | Notes |
|---|---|---|---|
| `StoragePath` | string | `App_Data/product-images` | relative = against `IWebHostEnvironment.ContentRootPath`; created if missing |
| `MaxBytes` | long | `2097152` | per-file maximum |

## Stored image file

Path = storage directory + `ImageFileName`. Allowed formats:

| ext | content type | leading bytes |
|---|---|---|
| `jpg` (from `.jpg`/`.jpeg`) | `image/jpeg` | `FF D8 FF` |
| `png` | `image/png` | `89 50 4E 47 0D 0A 1A 0A` |
| `webp` | `image/webp` | `52 49 46 46`, 4 any bytes, `57 45 42 50` |

## MVC model (modified)

`CategoryProduct.MVC/Models/Product.cs` gains `string? ImageUrl` (deserialized from the API; used only to decide whether to render `<img>`; never rendered as a URL).

## State transitions

`no image` -- upload --> `has image (file A)` -- upload --> `has image (file B; A deleted)` -- remove --> `no image (B deleted)`. Product delete from any state deletes the current file.
