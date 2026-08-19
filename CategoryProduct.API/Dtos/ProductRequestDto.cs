namespace CategoryProduct.API.Dtos
{
    public record ProductRequestDto(string Name, decimal Price, int Quantity, int CategoryId);
}
