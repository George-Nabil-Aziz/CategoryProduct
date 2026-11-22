namespace CategoryProduct.API.Models
{
    public enum OrderStatus
    {
        Pending,
        Confirmed,
        Cancelled
    }

    public static class OrderStatusExtensions
    {
        public static bool TryParseName(string? value, out OrderStatus status)
        {
            status = default;
            if (string.IsNullOrWhiteSpace(value)) return false;
            var name = Enum.GetNames<OrderStatus>()
                .FirstOrDefault(n => string.Equals(n, value.Trim(), StringComparison.OrdinalIgnoreCase));
            if (name == null) return false;
            status = Enum.Parse<OrderStatus>(name);
            return true;
        }
    }
}
