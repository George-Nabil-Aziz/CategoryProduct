using System.ComponentModel.DataAnnotations;

namespace CategoryProduct.API.Dtos
{
    public record LoginRequestDto(
        [Required(AllowEmptyStrings = false)]
        string UserName,

        [Required(AllowEmptyStrings = false)]
        string Password);
}
