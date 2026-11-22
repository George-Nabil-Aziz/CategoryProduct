using System.ComponentModel.DataAnnotations;

namespace CategoryProduct.API.Dtos
{
    public record AddCartItemRequestDto(
        [Range(1, int.MaxValue)] int ProductId,
        [Range(1, 100)] int Quantity);

    public record UpdateCartItemRequestDto([Range(1, 100)] int Quantity);
}
