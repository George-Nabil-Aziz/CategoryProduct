using System.ComponentModel.DataAnnotations;

namespace CategoryProduct.API.Dtos
{
    public record ProductRequestDto(
        [Required(AllowEmptyStrings = false)]
        [StringLength(100, MinimumLength = 2)]
        string Name,
        [Range(typeof(decimal), "0", "79228162514264337593543950335")]
        decimal Price,
        [Range(0, int.MaxValue)]
        int Quantity,
        [Range(1, int.MaxValue)]
        int CategoryId);
}
