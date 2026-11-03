namespace CategoryProduct.MVC.Models
{
    public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount, int TotalPages);

    public record CategoryRequest(string Name);

    public record ProductRequest(string Name, decimal Price, int Quantity, int CategoryId);

    /// <summary>Outcome of a write call (POST/PUT/DELETE).</summary>
    public class ApiResult
    {
        public bool Success { get; init; }
        public bool NotFound { get; init; }
        public bool Unauthorized { get; init; }
        /// <summary>400 validation errors: field name -> messages.</summary>
        public Dictionary<string, string[]> Errors { get; init; } = new();
    }
}
