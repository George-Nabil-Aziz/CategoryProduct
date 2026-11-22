using CategoryProduct.API.Data;
using CategoryProduct.API.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CategoryProduct.API.SqlServerTests
{
    /// <summary>Proves the PasswordResetTokens unique index, the cascade and the migration snapshot on a real SQL Server.</summary>
    public class PasswordResetTokenIndexSqlServerTests
    {
        private static User NewUser() =>
            new() { UserName = "u" + Guid.NewGuid().ToString("N")[..12], PasswordHash = "x", Role = "User" };

        private static PasswordResetToken NewToken(User user, string hash) => new()
        {
            User = user,
            TokenHash = hash,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30)
        };

        private static bool IsUniqueViolation(Exception ex)
        {
            for (; ex != null; ex = ex.InnerException!)
                if (ex is SqlException { Number: 2601 or 2627 }) return true;
            return false;
        }

        private static async Task WithDatabaseAsync(Func<ApplicationDbContext, string, Task> test)
        {
            var cs = SqlServerEnv.BuildConnectionString(SqlServerEnv.NewDatabaseName());
            using var db = new ApplicationDbContext(SqlServerEnv.Options(cs));
            try
            {
                await db.Database.MigrateAsync();
                await test(db, cs);
            }
            finally
            {
                await db.Database.EnsureDeletedAsync();
            }
        }

        [Fact]
        public Task DuplicateTokenHash_IsRejectedByTheUniqueIndex() => WithDatabaseAsync(async (db, cs) =>
        {
            var user = NewUser();
            db.PasswordResetTokens.Add(NewToken(user, "A1B2C3"));
            await db.SaveChangesAsync();

            using var other = new ApplicationDbContext(SqlServerEnv.Options(cs));
            other.PasswordResetTokens.Add(new PasswordResetToken
            {
                UserId = user.Id, TokenHash = "A1B2C3",
                CreatedAtUtc = DateTime.UtcNow, ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30)
            });
            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync());
            Assert.True(IsUniqueViolation(ex));
        });

        [Fact]
        public Task DifferentHashes_AreAccepted() => WithDatabaseAsync(async (db, _) =>
        {
            var user = NewUser();
            db.PasswordResetTokens.AddRange(NewToken(user, "HASH-ONE"), NewToken(user, "HASH-TWO"));
            await db.SaveChangesAsync();

            Assert.Equal(2, await db.PasswordResetTokens.CountAsync(t => t.UserId == user.Id));
        });

        [Fact]
        public Task DeletingTheUser_CascadesItsTokens() => WithDatabaseAsync(async (db, _) =>
        {
            var user = NewUser();
            db.PasswordResetTokens.AddRange(NewToken(user, "CASCADE-1"), NewToken(user, "CASCADE-2"));
            await db.SaveChangesAsync();
            var id = user.Id;

            db.Users.Remove(user);
            await db.SaveChangesAsync();

            Assert.Equal(0, await db.PasswordResetTokens.CountAsync(t => t.UserId == id));
        });

        [Fact]
        public Task ModelHasNoPendingChanges() => WithDatabaseAsync((db, _) =>
        {
            Assert.False(db.Database.HasPendingModelChanges());
            return Task.CompletedTask;
        });
    }
}
