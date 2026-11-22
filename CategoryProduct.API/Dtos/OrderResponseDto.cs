namespace CategoryProduct.API.Dtos
{
    public record OrderItemResponseDto(int Id, int? ProductId, string ProductName, decimal UnitPrice,
        int Quantity, decimal LineTotal);

    public record OrderResponseDto(int Id, int UserId, string? UserName, string Status, decimal Total,
        DateTime CreatedAtUtc, DateTime UpdatedAtUtc, IReadOnlyList<OrderItemResponseDto> Items, string PaymentStatus);

    public record OrderSummaryDto(int Id, int UserId, string? UserName, string Status, decimal Total,
        int ItemCount, DateTime CreatedAtUtc, DateTime UpdatedAtUtc, string PaymentStatus);
}
