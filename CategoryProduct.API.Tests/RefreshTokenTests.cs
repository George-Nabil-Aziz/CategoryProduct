using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CategoryProduct.API.Data;
using CategoryProduct.API.Dtos;
using CategoryProduct.API.Models;
using CategoryProduct.API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

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

        [Fact]
        public async Task ReuseRevokesOnlyOwnFamily_OtherSessionSurvives()
        {
            var a1 = await LoginAsync();
            var b1 = await LoginAsync();
            var a2 = (await (await RefreshAsync(a1.RefreshToken)).Content.ReadFromJsonAsync<LoginResponseDto>())!;
            Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(a1.RefreshToken)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(a2.RefreshToken)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(b1.RefreshToken)).StatusCode);
        }

        [Fact]
        public async Task ReuseAfterLogout_RevokesOnlyOwnFamily()
        {
            var t1 = await LoginAsync();
            var t2 = await LoginAsync();
            var lo = await _factory.CreateClient().PostAsJsonAsync("api/account/logout", new { refreshToken = t1.RefreshToken });
            Assert.Equal(HttpStatusCode.NoContent, lo.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(t1.RefreshToken)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(t2.RefreshToken)).StatusCode);
        }

        [Fact]
        public async Task LegacyDefaultFamily_DoesNotAffectOtherUsers()
        {
            var rawRevoked = "legacy-revoked-" + Guid.NewGuid().ToString("N");
            var rawOther = "legacy-other-" + Guid.NewGuid().ToString("N");
            int otherId;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var u1 = db.Users.First(u => u.UserName == ApiFactory.UserName);
                var u2 = db.Users.First(u => u.UserName == ApiFactory.PlainUserName);
                var now = DateTime.UtcNow;
                db.RefreshTokens.Add(new RefreshToken
                {
                    UserId = u1.Id, TokenHash = TokenService.Hash(rawRevoked), FamilyId = Guid.Empty,
                    CreatedAtUtc = now, ExpiresAtUtc = now.AddDays(7), RevokedAtUtc = now
                });
                var other = new RefreshToken
                {
                    UserId = u2.Id, TokenHash = TokenService.Hash(rawOther), FamilyId = Guid.Empty,
                    CreatedAtUtc = now, ExpiresAtUtc = now.AddDays(7)
                };
                db.RefreshTokens.Add(other);
                db.SaveChanges();
                otherId = other.Id;
            }

            Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(rawRevoked)).StatusCode);

            using var verify = _factory.Services.CreateScope();
            var vdb = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Null(vdb.RefreshTokens.Single(r => r.Id == otherId).RevokedAtUtc);
        }

        [Fact]
        public async Task ConcurrencyStamp_DetectsCompetingUpdate()
        {
            var root = new InMemoryDatabaseRoot();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase("stamp-" + Guid.NewGuid(), root).Options;
            using (var seed = new ApplicationDbContext(options))
            {
                var u = new User { UserName = "x", PasswordHash = "x", Role = "User" };
                seed.Users.Add(u);
                seed.RefreshTokens.Add(new RefreshToken
                {
                    User = u, TokenHash = "h", CreatedAtUtc = DateTime.UtcNow, ExpiresAtUtc = DateTime.UtcNow.AddDays(1)
                });
                await seed.SaveChangesAsync();
            }

            using var c1 = new ApplicationDbContext(options);
            using var c2 = new ApplicationDbContext(options);
            var t1 = await c1.RefreshTokens.SingleAsync();
            var t2 = await c2.RefreshTokens.SingleAsync();
            t1.Revoke(DateTime.UtcNow);
            await c1.SaveChangesAsync();
            t2.Revoke(DateTime.UtcNow);
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => c2.SaveChangesAsync());
        }
    }

    public class RefreshTokenConcurrencyTests : IClassFixture<RefreshTokenConcurrencyTests.RacingFactory>
    {
        public class RacingFactory : ApiFactory
        {
            public readonly RaceInterceptor Interceptor = new();

            protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
            {
                base.ConfigureWebHost(builder);
                builder.ConfigureServices(services =>
                {
                    Interceptor.Options = new DbContextOptionsBuilder<ApplicationDbContext>()
                        .UseInMemoryDatabase(DbName, DbRoot).Options;
                    services.ConfigureDbContext<ApplicationDbContext>(o => o.AddInterceptors(Interceptor));
                });
            }
        }

        public class RaceInterceptor : SaveChangesInterceptor
        {
            public DbContextOptions<ApplicationDbContext>? Options;
            private int _armed;
            public void Arm() => Interlocked.Exchange(ref _armed, 1);

            public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
                DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
            {
                var ctx = eventData.Context;
                var modified = ctx?.ChangeTracker.Entries<RefreshToken>()
                    .FirstOrDefault(e => e.State == EntityState.Modified);
                if (modified != null && Interlocked.CompareExchange(ref _armed, 0, 1) == 1)
                {
                    var id = modified.Entity.Id;
                    using var other = new ApplicationDbContext(Options!);
                    var t = await other.RefreshTokens.SingleAsync(r => r.Id == id, cancellationToken);
                    t.ConcurrencyStamp = Guid.NewGuid();
                    await other.SaveChangesAsync(cancellationToken);
                }
                return result;
            }
        }

        private readonly RacingFactory _factory;
        public RefreshTokenConcurrencyTests(RacingFactory factory) => _factory = factory;

        private async Task<LoginResponseDto> LoginAsync()
        {
            var r = await _factory.CreateClient().PostAsJsonAsync("api/account/login",
                new { userName = ApiFactory.UserName, password = ApiFactory.Password });
            r.EnsureSuccessStatusCode();
            return (await r.Content.ReadFromJsonAsync<LoginResponseDto>())!;
        }

        private Task<HttpResponseMessage> RefreshAsync(string token) =>
            _factory.CreateClient().PostAsJsonAsync("api/account/refresh", new { refreshToken = token });

        private int RevokedCount()
        {
            using var scope = _factory.Services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
                .RefreshTokens.Count(r => r.RevokedAtUtc != null);
        }

        [Fact]
        public async Task LostConcurrentRefresh_Returns401_AndDoesNotRevokeFamily()
        {
            var first = await LoginAsync();
            var second = await LoginAsync();
            var revokedBefore = RevokedCount();

            _factory.Interceptor.Arm();
            Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(first.RefreshToken)).StatusCode);

            Assert.Equal(revokedBefore, RevokedCount());
            Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(second.RefreshToken)).StatusCode);
        }

        [Fact]
        public async Task LogoutLosingConcurrentUpdate_StillReturns204()
        {
            var l = await LoginAsync();
            _factory.Interceptor.Arm();
            var r = await _factory.CreateClient().PostAsJsonAsync("api/account/logout", new { refreshToken = l.RefreshToken });
            Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        }
    }
}
