using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace CategoryProduct.MVC.Services
{
    // Shared cookie sign-in: stores API tokens as claims; cookie lifetime follows the refresh token.
    public class AuthSessionService
    {
        public async Task SignInAsync(HttpContext http, string userName, CategoryProductApiClient.LoginResult login)
        {
            var identity = new ClaimsIdentity(
                new[]
                {
                    new Claim(ClaimTypes.Name, userName),
                    new Claim(ClaimTypes.Role, login.Role),
                    new Claim("access_token", login.Token),
                    new Claim("refresh_token", login.RefreshToken),
                    new Claim("access_token_expires_at",
                        DateTime.SpecifyKind(login.ExpiresAtUtc, DateTimeKind.Utc).ToString("O"))
                },
                CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await http.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                new AuthenticationProperties
                {
                    IsPersistent = false,
                    ExpiresUtc = new DateTimeOffset(DateTime.SpecifyKind(login.RefreshTokenExpiresAtUtc, DateTimeKind.Utc))
                });
            // Make the new tokens visible to the rest of this request.
            http.User = principal;
        }
    }
}
