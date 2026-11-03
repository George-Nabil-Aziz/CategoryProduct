namespace CategoryProduct.API.Dtos
{
    public record LoginResponseDto(string Token, DateTime ExpiresAtUtc, string Role, string RefreshToken, DateTime RefreshTokenExpiresAtUtc);

    public record RefreshRequestDto(string RefreshToken);
}
