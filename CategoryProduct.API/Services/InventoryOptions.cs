namespace CategoryProduct.API.Services
{
    public class InventoryOptions
    {
        public const string SectionName = "Inventory";
        public int LowStockThreshold { get; set; } = 5;
        public int Effective => LowStockThreshold < 0 ? 5 : LowStockThreshold;
    }
}
