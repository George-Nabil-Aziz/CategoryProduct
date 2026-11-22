namespace CategoryProduct.API.Models
{
    public enum OrderPaymentStatus
    {
        Unpaid,
        Paid,
        Refunded
    }

    public static class OrderPaymentStatusExtensions
    {
        public static bool TryParseName(string? value, out OrderPaymentStatus status)
        {
            status = default;
            if (string.IsNullOrWhiteSpace(value)) return false;
            var name = Enum.GetNames<OrderPaymentStatus>()
                .FirstOrDefault(n => string.Equals(n, value.Trim(), StringComparison.OrdinalIgnoreCase));
            if (name == null) return false;
            status = Enum.Parse<OrderPaymentStatus>(name);
            return true;
        }
    }
}
