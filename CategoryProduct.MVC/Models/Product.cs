using System.ComponentModel.DataAnnotations;

namespace CategoryProduct.MVC.Models
{
    public class Product
    {
        public int Id { get; set; }

        [Required, StringLength(100, MinimumLength = 2)]
        public string Name { get; set; } = string.Empty;

        [Range(0, double.MaxValue)]
        public decimal Price { get; set; }

        [Range(0, int.MaxValue)]
        public int Quantity { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "Please select a category.")]
        public int CategoryId { get; set; }

        // Resolved by the controller from the categories list (API returns only CategoryId).
        public string? CategoryName { get; set; }
    }
}
