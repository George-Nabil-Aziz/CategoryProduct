using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CategoryProduct.API.Dtos;

namespace CategoryProduct.API.Tests
{
    public class RefreshTokenTests : IClassFixture<ApiFactory>
    {
        private readonly ApiFactory _factory;
        public RefreshTokenTests(ApiFactory factory) => _factory = factory;

        private async Task<LoginResponseDto> LoginAsync()
        {
            var r = await _factory.CreateClient().PostAsJsonAsync("api/account/login",
                new { userName = ApiFactory.UserName, password = ApiFactory.Password });
            r.EnsureSuccessStatusCode();
            return (await r.Content.ReadFromJsonAsync<LoginResponseDto>())!;
        }

        private Task<HttpResponseMessage> RefreshAsync(string token) =>
            _factory.CreateClient().PostAsJsonAsync("api/account/refresh", new { refreshToken = token });

        [Fact]
        public async Task Login_ReturnsRefreshToken()
        {
            var l = await LoginAsync();
            Assert.False(string.IsNullOrEmpty(l.RefreshToken));
            Assert.True(l.RefreshTokenExpiresAtUtc > DateTime.UtcNow.AddDays(6));
        }

        [Fact]
        public async Task Refresh_ReturnsNewPair_AndNewAccessTokenWorks()
        {
            var l = await LoginAsync();
            var r = await RefreshAsync(l.RefreshToken);
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            var n = (await r.Content.ReadFromJsonAsync<LoginResponseDto>())!;
            Assert.NotEqual(l.RefreshToken, n.RefreshToken);

            var c = _factory.CreateClient();
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", n.Token);
            var w = await c.PostAsJsonAsync("api/category", new { name = "Refresh Cat " + Guid.NewGuid().ToString("N")[..6] });
            Assert.True(w.IsSuccessStatusCode, w.StatusCode.ToString());
        }

        [Fact]
        public async Task ReusedRefreshToken_Returns401_AndRevokesFamily()
        {
            var l = await LoginAsync();
            var n = (await (await RefreshAsync(l.RefreshToken)).Content.ReadFromJsonAsync<LoginResponseDto>())!;
            Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(l.RefreshToken)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(n.RefreshToken)).StatusCode);
        }

        [Fact]
        public async Task UnknownRefreshToken_Returns401()
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync("nope")).StatusCode);
        }

        [Fact]
        public async Task Logout_RevokesRefreshToken()
        {
            var l = await LoginAsync();
            var r = await _factory.CreateClient().PostAsJsonAsync("api/account/logout", new { refreshToken = l.RefreshToken });
            Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(l.RefreshToken)).StatusCode);
        }
    }
}
