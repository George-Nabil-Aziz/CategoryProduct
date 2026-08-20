using System.ComponentModel.DataAnnotations;

namespace CategoryProduct.API.Dtos
{
    public record CategoryRequestDto(
        [Required(AllowEmptyStrings = false)]
        [StringLength(100, MinimumLength = 2)]
        string Name);
}
