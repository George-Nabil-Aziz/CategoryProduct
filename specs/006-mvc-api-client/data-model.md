# Data Model: MVC Consumes the API

**No schema change.** The API database is untouched. The MVC project no longer has a database or migrations; its models are plain classes.

## MVC models (`CategoryProduct.MVC/Models/`)

### Category (`Category.cs`, simplified)

| Field | Type | Rule(s) |
|-------|------|---------|
| `Id` | `int` | |
| `Name` | `string` | `[Required]`, `[StringLength(100, MinimumLength = 2)]` |
| `Products` | `List<Product>` | Populated only on the Details page (from `GET api/category/{id}/products`); previously an EF navigation collection |

### Product (`Product.cs`, simplified)

| Field | Type | Rule(s) |
|-------|------|---------|
| `Id` | `int` | |
| `Name` | `string` | `[Required]`, `[StringLength(100, MinimumLength = 2)]` |
| `Price` | `decimal` | `[Range(0, double.MaxValue)]` |
| `Quantity` | `int` | `[Range(0, int.MaxValue)]` |
| `CategoryId` | `int` | `[Range(1, int.MaxValue, ErrorMessage = "Please select a category.")]` |
| `CategoryName` | `string?` | Resolved by the controller; the API returns only `CategoryId`. Previously an EF `Category` navigation |

### API models (`ApiModels.cs`, new)

| Type | Shape |
|------|-------|
| `PagedResult<T>` | `record(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount, int TotalPages)` mirrors the API's `PagedResponseDto<T>` (004) |
| `CategoryRequest` | `record(string Name)` request body for create/update |
| `ProductRequest` | `record(string Name, decimal Price, int Quantity, int CategoryId)` request body |
| `ApiResult` | Outcome of a write: `Success`, `NotFound`, `Unauthorized` (set for 401 and 403 at this commit), `Errors` (`Dictionary<string, string[]>` of 400 validation errors) |

## Removed

| Item | Notes |
|------|-------|
| `CategoryProduct.MVC/Data/ApplicationDbContext.cs` | Deleted |
| EF Core packages (`Microsoft.EntityFrameworkCore.SqlServer`, `Microsoft.EntityFrameworkCore.Tools`) | Removed from `CategoryProduct.MVC.csproj` |
| `ConnectionStrings` section of `appsettings.json` | Replaced by `Api:BaseUrl` |

## Configuration

| Key | Value |
|-----|-------|
| `Api:BaseUrl` | `https://localhost:7183/` (default in `CategoryProduct.MVC/appsettings.json`) |
