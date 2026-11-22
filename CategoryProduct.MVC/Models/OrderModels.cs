namespace CategoryProduct.MVC.Models
{
    public record OrderLine(int Id, int? ProductId, string ProductName, decimal UnitPrice, int Quantity, decimal LineTotal);

    public record OrderModel(int Id, int UserId, string UserName, string Status, decimal Total,
        DateTime CreatedAtUtc, DateTime UpdatedAtUtc, IReadOnlyList<OrderLine> Items, string PaymentStatus = "Unpaid");

    public record OrderSummaryItem(int Id, int UserId, string UserName, string Status, decimal Total, int ItemCount,
        DateTime CreatedAtUtc, DateTime UpdatedAtUtc, string PaymentStatus = "Unpaid");

    public record PaymentModel(int Id, int OrderId, decimal Amount, string Currency, string Status,
        string? ProviderReference, string? FailureReason, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);

    public record PayOrderRequest(string PaymentToken, string IdempotencyKey);
}
