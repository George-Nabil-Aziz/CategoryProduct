namespace CategoryProduct.API.Dtos
{
    public record StockMovementResponseDto(int Id, int ProductId, int QuantityChange, int QuantityAfter,
        string Reason, string? Note, int? UserId, string? UserName, DateTime CreatedAtUtc);
}
