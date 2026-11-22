using System.Net;
using System.Net.Http.Json;
using CategoryProduct.API.Data;
using CategoryProduct.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CategoryProduct.API.SqlServerTests
{
    public class SeededTestUserNeutralizationSqlServerTests
    {
        private const string PreviousMigration = "20261122120100_AddCartAndOrders";
        private const string SeededHash =
            "AQAAAAIAAYagAAAAENKkunRHnzjDucj641tHfwyMEdkA6lL7wKrwYWVP4LKRA2L9/9W536fomTkCKhKHSw==";
        private const string Marker = "/w==";

        private static async Task WithDatabaseAtPreviousMigrationAsync(Func<ApplicationDbContext, Task> body)
        {
            var cs = SqlServerEnv.BuildConnectionString(SqlServerEnv.NewDatabaseName());
            using var db = new ApplicationDbContext(SqlServerEnv.Options(cs));
            try
            {
                await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
                await body(db);
            }
            finally
            {
                await db.Database.EnsureDeletedAsync();
            }
        }

        private static Task<User> TestUserAsync(ApplicationDbContext db) =>
            db.Users.AsNoTracking().SingleAsync(u => u.UserName == "testuser");

        [Fact]
        public async Task SeededAdmin_IsNeutralized_TokensRevoked_OrderKept()
        {
            await WithDatabaseAtPreviousMigrationAsync(async db =>
            {
                var seeded = await TestUserAsync(db);
                Assert.Equal("Admin", seeded.Role);
                Assert.Equal(SeededHash, seeded.PasswordHash);

                var now = DateTime.UtcNow;
                db.RefreshTokens.Add(new RefreshToken
                {
                    UserId = seeded.Id, TokenHash = "hash-live", CreatedAtUtc = now,
                    ExpiresAtUtc = now.AddDays(1), FamilyId = Guid.NewGuid()
                });
                db.Orders.Add(new Order
                {
                    UserId = seeded.Id, Status = OrderStatus.Pending, Total = 10,
                    CreatedAtUtc = now, UpdatedAtUtc = now
                });
                await db.SaveChangesAsync();
                var stampBefore = (await db.RefreshTokens.AsNoTracking().SingleAsync()).ConcurrencyStamp;

                await db.Database.MigrateAsync();

                var after = await TestUserAsync(db);
                Assert.Equal(Marker, after.PasswordHash);
                Assert.Equal("User", after.Role);
                Assert.Equal(seeded.Id, after.Id);
                var token = await db.RefreshTokens.AsNoTracking().SingleAsync();
                Assert.NotNull(token.RevokedAtUtc);
                Assert.NotEqual(stampBefore, token.ConcurrencyStamp);
                Assert.Equal(1, await db.Orders.CountAsync(o => o.UserId == seeded.Id));
            });
        }

        [Fact]
        public async Task ChangedPassword_IsLeftUntouched()
        {
            await WithDatabaseAtPreviousMigrationAsync(async db =>
            {
                var seeded = await TestUserAsync(db);
                await db.Database.ExecuteSqlRawAsync(
                    "UPDATE Users SET PasswordHash = 'operator-changed-hash', Role = 'Admin' WHERE UserName = 'testuser'");
                var now = DateTime.UtcNow;
                db.RefreshTokens.Add(new RefreshToken
                {
                    UserId = seeded.Id, TokenHash = "hash-live", CreatedAtUtc = now,
                    ExpiresAtUtc = now.AddDays(1), FamilyId = Guid.NewGuid()
                });
                await db.SaveChangesAsync();

                await db.Database.MigrateAsync();

                var after = await TestUserAsync(db);
                Assert.Equal("operator-changed-hash", after.PasswordHash);
                Assert.Equal("Admin", after.Role);
                Assert.Null((await db.RefreshTokens.AsNoTracking().SingleAsync()).RevokedAtUtc);
            });
        }

        [Fact]
        public async Task NoTestUser_MigrationSucceeds()
        {
            await WithDatabaseAtPreviousMigrationAsync(async db =>
            {
                await db.Database.ExecuteSqlRawAsync("DELETE FROM Users WHERE UserName = 'testuser'");

                await db.Database.MigrateAsync();

                Assert.False(await db.Users.AnyAsync(u => u.UserName == "testuser"));
                Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            });
        }

        [Fact]
        public async Task NeutralizedTestUser_LoginReturns401()
        {
            using var factory = new SqlServerApiFactory();
            var client = factory.CreateClient();

            var response = await client.PostAsJsonAsync("api/account/login",
                new { userName = "testuser", password = "anything-at-all" });

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            var row = await factory.WithDbAsync(db => db.Users.SingleAsync(u => u.UserName == "testuser"));
            Assert.Equal(Marker, row.PasswordHash);
            Assert.Equal("User", row.Role);
        }
    }
}
