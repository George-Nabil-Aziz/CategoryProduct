using System.ComponentModel.DataAnnotations;

namespace CategoryProduct.MVC.Models
{
    public class Category
    {
        public int Id { get; set; }

        [Required, StringLength(100, MinimumLength = 2)]
        public string Name { get; set; } = string.Empty;

        // Populated only on the Details page (GET api/category/{id}/products).
        public List<Product> Products { get; set; } = new();
    }
}
