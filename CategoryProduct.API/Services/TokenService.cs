using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using CategoryProduct.API.Models;
using Microsoft.IdentityModel.Tokens;

namespace CategoryProduct.API.Services
{
    public class TokenService
    {
        private readonly IConfiguration _configuration;

        public TokenService(IConfiguration configuration) => _configuration = configuration;

        public (string Token, DateTime ExpiresAtUtc) CreateAccessToken(User user)
        {
            var expiryMinutes = _configuration.GetValue<int>("Jwt:ExpiryMinutes", 60);
            var expiresAtUtc = DateTime.UtcNow.AddMinutes(expiryMinutes);

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.UserName),
                new Claim(ClaimTypes.Role, user.Role)
            };

            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(_configuration["Jwt:Key"] ?? string.Empty));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256Signature);

            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                claims: claims,
                expires: expiresAtUtc,
                signingCredentials: credentials);

            return (new JwtSecurityTokenHandler().WriteToken(token), expiresAtUtc);
        }

        /// <summary>Creates a refresh token entity (hash only) and returns the raw token to hand to the client.</summary>
        public (string RawToken, RefreshToken Entity) CreateRefreshToken(int userId)
        {
            var raw = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(64));
            var days = _configuration.GetValue<int>("Jwt:RefreshTokenDays", 7);
            var now = DateTime.UtcNow;
            return (raw, new RefreshToken
            {
                UserId = userId,
                TokenHash = Hash(raw),
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddDays(days)
            });
        }

        public static string Hash(string rawToken) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
    }
}
