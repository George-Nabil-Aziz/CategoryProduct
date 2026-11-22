namespace CategoryProduct.API.Models
{
    public enum PaymentState
    {
        Pending,
        Succeeded,
        Failed,
        Refunded
    }

    /// <summary>One payment attempt for an order. Never holds a token or card data.</summary>
    public class Payment
    {
        public int Id { get; set; }
        public int OrderId { get; set; }
        public Order? Order { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "USD";
        public PaymentState Status { get; set; }
        public string? ProviderReference { get; set; }
        public string IdempotencyKey { get; set; } = string.Empty;
        public string? FailureReason { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    }
}
