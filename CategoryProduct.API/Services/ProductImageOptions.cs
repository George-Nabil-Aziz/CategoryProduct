namespace CategoryProduct.API.Services
{
    public class ProductImageOptions
    {
        public const string SectionName = "ProductImages";

        public string StoragePath { get; set; } = "App_Data/product-images";
        public long MaxBytes { get; set; } = 2 * 1024 * 1024;
    }
}
