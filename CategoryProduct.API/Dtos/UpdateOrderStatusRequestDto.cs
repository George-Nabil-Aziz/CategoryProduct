using System.ComponentModel.DataAnnotations;

namespace CategoryProduct.API.Dtos
{
    public record UpdateOrderStatusRequestDto([Required(AllowEmptyStrings = false)] string Status);
}
