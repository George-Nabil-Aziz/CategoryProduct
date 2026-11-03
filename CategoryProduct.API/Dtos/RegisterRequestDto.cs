using System.ComponentModel.DataAnnotations;

namespace CategoryProduct.API.Dtos
{
    public record RegisterRequestDto(
        [Required(AllowEmptyStrings = false)]
        [StringLength(50, MinimumLength = 3)]
        [RegularExpression(@"^[A-Za-z0-9._-]+$", ErrorMessage = "UserName may contain only letters, digits, '.', '_' and '-'.")]
        string UserName,

        [Required(AllowEmptyStrings = false)]
        [MinLength(8)]
        string Password);
}
