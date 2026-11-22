using CategoryProduct.API.Data;
using CategoryProduct.API.Models;
using CategoryProduct.API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace CategoryProduct.API.Tests
{
    public class RefreshTokenCleanupTests
    {
        private static ApplicationDbContext NewDb() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase("cleanup-" + Guid.NewGuid()).Options);

        private static IConfiguration Config(string? retention = null)
        {
            var values = new Dictionary<string, string?>();
            if (retention != null) values["RefreshTokenCleanup:RetentionDays"] = retention;
            return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        }

        private static async Task SeedAsync(ApplicationDbContext db, DateTime now)
        {
            var user = new User { UserName = "u", PasswordHash = "x", Role = "User" };
            db.Users.Add(user);
            RefreshToken T(string h, DateTime expires, DateTime? revoked) => new()
            {
                User = user, TokenHash = h, CreatedAtUtc = now.AddDays(-30), ExpiresAtUtc = expires, RevokedAtUtc = revoked
            };
            db.RefreshTokens.AddRange(
                T("expired-old", now.AddDays(-10), null),
                T("revoked-old", now.AddDays(5), now.AddDays(-10)),
                T("expired-recent", now.AddDays(-1), null),
                T("revoked-recent", now.AddDays(5), now.AddDays(-1)),
                T("active", now.AddDays(5), null));
            await db.SaveChangesAsync();
        }

        [Fact]
        public async Task PurgeAsync_RemovesOnlyOldExpiredOrRevoked()
        {
            using var db = NewDb();
            var now = DateTime.UtcNow;
            await SeedAsync(db, now);

            var removed = await new RefreshTokenCleanupService(db, Config("7")).PurgeAsync(now);

            Assert.Equal(2, removed);
            var left = db.RefreshTokens.Select(r => r.TokenHash).OrderBy(h => h).ToList();
            Assert.Equal(new[] { "active", "expired-recent", "revoked-recent" }, left);
        }

        [Theory]
        [InlineData("0")]
        [InlineData("-3")]
        public async Task PurgeAsync_NonPositiveRetention_FallsBackToSeven(string retention)
        {
            using var db = NewDb();
            var now = DateTime.UtcNow;
            await SeedAsync(db, now);

            var removed = await new RefreshTokenCleanupService(db, Config(retention)).PurgeAsync(now);

            Assert.Equal(2, removed);
            Assert.Equal(3, db.RefreshTokens.Count());
        }
    }
}
