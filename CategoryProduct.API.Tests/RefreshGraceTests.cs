using System.Net;
using System.Net.Http.Json;
using CategoryProduct.API.Data;
using CategoryProduct.API.Dtos;
using CategoryProduct.API.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CategoryProduct.API.Tests
{
    public class GraceFactory : ApiFactory
    {
        protected override int RefreshReuseGraceSeconds => 10;
    }

    public class GraceKeyAbsentFactory : ApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Jwt:RefreshReuseGraceSeconds", "");
        }
    }

    public class GraceNegativeFactory : ApiFactory
    {
        protected override int RefreshReuseGraceSeconds => -5;
    }

    internal static class GraceHelper
    {
        public static async Task<LoginResponseDto> LoginAsync(ApiFactory f)
        {
            var r = await f.CreateClient().PostAsJsonAsync("api/account/login",
                new { userName = ApiFactory.UserName, password = ApiFactory.Password });
            r.EnsureSuccessStatusCode();
            return (await r.Content.ReadFromJsonAsync<LoginResponseDto>())!;
        }

        public static Task<HttpResponseMessage> RefreshAsync(ApiFactory f, string token) =>
            f.CreateClient().PostAsJsonAsync("api/account/refresh", new { refreshToken = token });

        public static async Task BackdateRevocationAsync(ApiFactory f, string rawToken, TimeSpan ago)
        {
            using var scope = f.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var hash = TokenService.Hash(rawToken);
            var row = await db.RefreshTokens.SingleAsync(r => r.TokenHash == hash);
            row.RevokedAtUtc = DateTime.UtcNow - ago;
            await db.SaveChangesAsync();
        }

        public static async Task<(int total, int revoked)> CountsAsync(ApiFactory f)
        {
            using var scope = f.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            return (await db.RefreshTokens.CountAsync(), await db.RefreshTokens.CountAsync(r => r.RevokedAtUtc != null));
        }
    }

    public class RefreshGraceTests : IClassFixture<GraceFactory>
    {
        private readonly GraceFactory _f;
        public RefreshGraceTests(GraceFactory f) => _f = f;

        [Fact]
        public async Task RotatedToken_InsideGrace_Returns401_WithoutRevokingFamily_OrIssuing()
        {
            var l = await GraceHelper.LoginAsync(_f);
            var rotated = await GraceHelper.RefreshAsync(_f, l.RefreshToken);
            Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
            var n = (await rotated.Content.ReadFromJsonAsync<LoginResponseDto>())!;
            var before = await GraceHelper.CountsAsync(_f);

            var reuse = await GraceHelper.RefreshAsync(_f, l.RefreshToken);

            Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);
            Assert.Equal(before, await GraceHelper.CountsAsync(_f)); // (e) no new row, no extra revoke
            Assert.Equal(HttpStatusCode.OK, (await GraceHelper.RefreshAsync(_f, n.RefreshToken)).StatusCode);
        }

        [Fact]
        public async Task RotatedToken_OutsideGrace_RevokesFamily()
        {
            var l = await GraceHelper.LoginAsync(_f);
            var n = (await (await GraceHelper.RefreshAsync(_f, l.RefreshToken)).Content.ReadFromJsonAsync<LoginResponseDto>())!;
            await GraceHelper.BackdateRevocationAsync(_f, l.RefreshToken, TimeSpan.FromSeconds(11));

            Assert.Equal(HttpStatusCode.Unauthorized, (await GraceHelper.RefreshAsync(_f, l.RefreshToken)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await GraceHelper.RefreshAsync(_f, n.RefreshToken)).StatusCode);
        }

        [Fact]
        public async Task LogoutRevokedToken_ReusedImmediately_RevokesFamily()
        {
            var l = await GraceHelper.LoginAsync(_f);
            var other = await GraceHelper.LoginAsync(_f); // separate family, must be unaffected
            var n = (await (await GraceHelper.RefreshAsync(_f, l.RefreshToken)).Content.ReadFromJsonAsync<LoginResponseDto>())!;
            await _f.CreateClient().PostAsJsonAsync("api/account/logout", new { refreshToken = n.RefreshToken });

            Assert.Equal(HttpStatusCode.Unauthorized, (await GraceHelper.RefreshAsync(_f, n.RefreshToken)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await GraceHelper.RefreshAsync(_f, other.RefreshToken)).StatusCode);
        }
    }

    public class RefreshGraceZeroTests : IClassFixture<ApiFactory>
    {
        private readonly ApiFactory _f;
        public RefreshGraceZeroTests(ApiFactory f) => _f = f;

        [Fact]
        public async Task GraceZero_ImmediateReuse_RevokesFamily()
        {
            var l = await GraceHelper.LoginAsync(_f);
            var n = (await (await GraceHelper.RefreshAsync(_f, l.RefreshToken)).Content.ReadFromJsonAsync<LoginResponseDto>())!;
            Assert.Equal(HttpStatusCode.Unauthorized, (await GraceHelper.RefreshAsync(_f, l.RefreshToken)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await GraceHelper.RefreshAsync(_f, n.RefreshToken)).StatusCode);
        }
    }

    public class RefreshGraceDefaultTests : IClassFixture<GraceKeyAbsentFactory>
    {
        private readonly GraceKeyAbsentFactory _f;
        public RefreshGraceDefaultTests(GraceKeyAbsentFactory f) => _f = f;

        [Fact]
        public async Task KeyAbsent_BehavesAs10Seconds()
        {
            var l = await GraceHelper.LoginAsync(_f);
            var n = (await (await GraceHelper.RefreshAsync(_f, l.RefreshToken)).Content.ReadFromJsonAsync<LoginResponseDto>())!;
            Assert.Equal(HttpStatusCode.Unauthorized, (await GraceHelper.RefreshAsync(_f, l.RefreshToken)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await GraceHelper.RefreshAsync(_f, n.RefreshToken)).StatusCode);
        }
    }

    public class RefreshGraceNegativeTests : IClassFixture<GraceNegativeFactory>
    {
        private readonly GraceNegativeFactory _f;
        public RefreshGraceNegativeTests(GraceNegativeFactory f) => _f = f;

        [Fact]
        public async Task NegativeValue_BehavesAsZero()
        {
            var l = await GraceHelper.LoginAsync(_f);
            var n = (await (await GraceHelper.RefreshAsync(_f, l.RefreshToken)).Content.ReadFromJsonAsync<LoginResponseDto>())!;
            Assert.Equal(HttpStatusCode.Unauthorized, (await GraceHelper.RefreshAsync(_f, l.RefreshToken)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await GraceHelper.RefreshAsync(_f, n.RefreshToken)).StatusCode);
        }
    }
}
