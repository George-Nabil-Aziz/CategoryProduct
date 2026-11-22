namespace CategoryProduct.MVC.Models
{
    public record OrderLine(int Id, int? ProductId, string ProductName, decimal UnitPrice, int Quantity, decimal LineTotal);

    public record OrderModel(int Id, int UserId, string UserName, string Status, decimal Total,
        DateTime CreatedAtUtc, DateTime UpdatedAtUtc, IReadOnlyList<OrderLine> Items);

    public record OrderSummaryItem(int Id, int UserId, string UserName, string Status, decimal Total, int ItemCount,
        DateTime CreatedAtUtc, DateTime UpdatedAtUtc);
}
