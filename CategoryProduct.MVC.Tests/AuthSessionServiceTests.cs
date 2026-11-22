using Microsoft.AspNetCore.Http;
using System.Globalization;
using System.Security.Claims;
using CategoryProduct.MVC.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;

namespace CategoryProduct.MVC.Tests;

public class AuthSessionServiceTests
{
    [Fact]
    public async Task SignIn_stores_claims_cookie_properties_and_replaces_http_user()
    {
        var auth = new RecordingAuthenticationService();
        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton<IAuthenticationService>(auth).BuildServiceProvider()
        };
        var before = http.User;
        // Unspecified kinds must be treated as UTC.
        var accessExpiry = new DateTime(2030, 1, 2, 3, 4, 5, 678, DateTimeKind.Unspecified);
        var refreshExpiry = new DateTime(2030, 1, 9, 10, 11, 12, DateTimeKind.Unspecified);
        var login = new CategoryProductApiClient.LoginResult("tok", accessExpiry, "Admin", "ref", refreshExpiry);

        await new AuthSessionService().SignInAsync(http, "bob", login);

        var call = Assert.Single(auth.SignIns);
        Assert.Equal(CookieAuthenticationDefaults.AuthenticationScheme, call.Scheme);
        Assert.NotSame(before, http.User);
        Assert.Same(call.Principal, http.User);

        var p = call.Principal;
        Assert.Equal("bob", p.FindFirst(ClaimTypes.Name)!.Value);
        Assert.Equal("Admin", p.FindFirst(ClaimTypes.Role)!.Value);
        Assert.Equal("tok", p.FindFirst("access_token")!.Value);
        Assert.Equal("ref", p.FindFirst("refresh_token")!.Value);
        var expiresClaim = p.FindFirst("access_token_expires_at")!.Value;
        Assert.Equal(DateTime.SpecifyKind(accessExpiry, DateTimeKind.Utc).ToString("O"), expiresClaim);
        var parsed = DateTime.Parse(expiresClaim, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        Assert.Equal(DateTimeKind.Utc, parsed.Kind);
        Assert.Equal(accessExpiry.Ticks, parsed.Ticks);
        Assert.True(p.Identity!.IsAuthenticated);

        Assert.NotNull(call.Properties);
        Assert.False(call.Properties!.IsPersistent);
        Assert.Equal(new DateTimeOffset(DateTime.SpecifyKind(refreshExpiry, DateTimeKind.Utc)), call.Properties.ExpiresUtc);
        Assert.Equal(TimeSpan.Zero, call.Properties.ExpiresUtc!.Value.Offset);
    }
}
