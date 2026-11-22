using System.Net;
using System.Net.Http.Json;
using CategoryProduct.API.Data;
using CategoryProduct.API.Models;
using CategoryProduct.API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CategoryProduct.API.Tests
{
    public class RefreshLimitTwoFactory : ApiFactory
    {
        protected override int RefreshPermitLimit => 2;
    }

    public class RefreshEdgeCaseTests : IClassFixture<ApiFactory>, IClassFixture<RefreshLimitTwoFactory>
    {
        private readonly ApiFactory _factory;
        private readonly RefreshLimitTwoFactory _limitFactory;

        public RefreshEdgeCaseTests(ApiFactory factory, RefreshLimitTwoFactory limitFactory)
        {
            _factory = factory;
            _limitFactory = limitFactory;
        }

        [Fact]
        public async Task Refresh_ExpiredToken_Returns401()
        {
            const string raw = "expired-raw-token";
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var user = await db.Users.FirstAsync(u => u.UserName == ApiFactory.UserName);
                db.RefreshTokens.Add(new RefreshToken
                {
                    UserId = user.Id,
                    TokenHash = TokenService.Hash(raw),
                    CreatedAtUtc = DateTime.UtcNow.AddDays(-10),
                    ExpiresAtUtc = DateTime.UtcNow.AddDays(-1),
                    FamilyId = Guid.NewGuid()
                });
                await db.SaveChangesAsync();
            }

            var response = await GraceHelper.RefreshAsync(_factory, raw);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Refresh_DeletedUser_Returns401()
        {
            using var factory = new ApiFactory();
            var login = await GraceHelper.LoginAsync(factory);
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var user = await db.Users.FirstAsync(u => u.UserName == ApiFactory.UserName);
                db.Users.Remove(user);
                await db.SaveChangesAsync();
            }

            var response = await GraceHelper.RefreshAsync(factory, login.RefreshToken);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Refresh_EmptyJsonBody_ReturnsBadRequest()
        {
            var response = await _factory.CreateClient().PostAsJsonAsync("api/account/refresh", new { });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task Refresh_AbovePermitLimit_Returns429()
        {
            var client = _limitFactory.CreateClient();
            var body = new { refreshToken = "does-not-matter" };

            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("api/account/refresh", body)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("api/account/refresh", body)).StatusCode);
            Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync("api/account/refresh", body)).StatusCode);
        }

        [Fact]
        public async Task GetUsers_WithoutToken_Returns401()
        {
            var response = await _factory.CreateClient().GetAsync("api/users");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }
}
