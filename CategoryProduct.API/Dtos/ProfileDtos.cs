namespace CategoryProduct.API.Dtos
{
    public record UpdateEmailRequestDto(string? Email);

    public record ProfileResponseDto(int Id, string UserName, string Role, string? Email);
}
