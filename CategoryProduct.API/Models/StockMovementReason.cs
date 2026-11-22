namespace CategoryProduct.API.Models
{
    public enum StockMovementReason
    {
        Initial,
        Restock,
        Adjustment,
        Damage,
        Return,
        Sale,
        OrderCancelled
    }

    public static class StockMovementReasonExtensions
    {
        public static readonly IReadOnlyList<StockMovementReason> AdminReasons = new[]
        {
            StockMovementReason.Restock, StockMovementReason.Adjustment,
            StockMovementReason.Damage, StockMovementReason.Return
        };

        public static bool TryParseName(string? value, out StockMovementReason reason)
        {
            reason = default;
            if (string.IsNullOrWhiteSpace(value)) return false;
            var name = Enum.GetNames<StockMovementReason>()
                .FirstOrDefault(n => string.Equals(n, value.Trim(), StringComparison.OrdinalIgnoreCase));
            if (name == null) return false;
            reason = Enum.Parse<StockMovementReason>(name);
            return true;
        }
    }
}
