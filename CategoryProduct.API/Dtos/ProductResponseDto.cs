namespace CategoryProduct.API.Dtos
{
    public record ProductResponseDto(int Id, string Name, decimal Price, int Quantity, int CategoryId);
}
