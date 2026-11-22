namespace CategoryProduct.API.Models
{
    public class StockMovement
    {
        public int Id { get; set; }
        public int ProductId { get; set; }
        public Product? Product { get; set; }
        public int QuantityChange { get; set; }
        public int QuantityAfter { get; set; }
        public StockMovementReason Reason { get; set; }
        public string? Note { get; set; }
        public int? UserId { get; set; }
        public User? User { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }
}
