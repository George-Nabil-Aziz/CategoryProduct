namespace CategoryProduct.API.Dtos
{
    public record LoginResponseDto(string Token, DateTime ExpiresAtUtc, string Role);
}
