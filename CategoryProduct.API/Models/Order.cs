namespace CategoryProduct.API.Models
{
    public class Order
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public User? User { get; set; }
        public OrderStatus Status { get; set; }
        public decimal Total { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
        public List<OrderItem> Items { get; set; } = new();
    }
}
