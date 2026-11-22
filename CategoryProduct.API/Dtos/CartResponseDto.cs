namespace CategoryProduct.API.Dtos
{
    public record CartItemResponseDto(int ProductId, string ProductName, decimal UnitPrice, int Quantity,
        decimal LineTotal, int AvailableQuantity);

    public record CartResponseDto(IReadOnlyList<CartItemResponseDto> Items, int ItemCount, decimal Total);
}
