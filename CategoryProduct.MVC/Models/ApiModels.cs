namespace CategoryProduct.MVC.Models
{
    public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount, int TotalPages);

    public record CategoryRequest(string Name);

    public record ProductRequest(string Name, decimal Price, int Quantity, int CategoryId);

    public record StockMovementItem(int Id, int ProductId, int QuantityChange, int QuantityAfter, string Reason, string? Note, int? UserId, string? UserName, DateTime CreatedAtUtc);

    public record StockMovementRequest(int QuantityChange, string Reason, string? Note);

    public record AddCartItemRequest(int ProductId, int Quantity);

    public record UpdateCartItemRequest(int Quantity);

    public record UpdateOrderStatusRequest(string Status);

    /// <summary>Outcome of a call that returns a body on success.</summary>
    public class ApiResult<T> : ApiResult
    {
        public T? Value { get; init; }
    }

    /// <summary>Outcome of a write call (POST/PUT/DELETE).</summary>
    public class ApiResult
    {
        public bool Success { get; init; }
        public bool NotFound { get; init; }
        public bool Unauthorized { get; init; }
        public bool Forbidden { get; init; }
        public bool TooManyRequests { get; init; }
        /// <summary>402 payment declined: the API message ("Payment declined: ...").</summary>
        public string? Declined { get; init; }
        /// <summary>409 conflict message from the API.</summary>
        public string? Conflict { get; init; }
        /// <summary>400 validation errors: field name -> messages.</summary>
        public Dictionary<string, string[]> Errors { get; init; } = new();
    }
}
