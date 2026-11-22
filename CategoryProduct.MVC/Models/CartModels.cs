namespace CategoryProduct.MVC.Models
{
    public record CartLine(int ProductId, string ProductName, decimal UnitPrice, int Quantity, decimal LineTotal, int AvailableQuantity);

    public record CartModel(IReadOnlyList<CartLine> Items, int ItemCount, decimal Total);
}
