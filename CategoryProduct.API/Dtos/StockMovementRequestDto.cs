using System.ComponentModel.DataAnnotations;

namespace CategoryProduct.API.Dtos
{
    public class StockMovementRequestDto
    {
        [Range(-1000000, 1000000)]
        public int QuantityChange { get; set; }

        [Required(AllowEmptyStrings = false)]
        public string Reason { get; set; } = "";

        [StringLength(200)]
        public string? Note { get; set; }
    }
}
