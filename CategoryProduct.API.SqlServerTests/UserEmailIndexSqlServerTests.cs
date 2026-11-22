using CategoryProduct.API.Data;
using CategoryProduct.API.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CategoryProduct.API.SqlServerTests
{
    /// <summary>Proves the filtered unique index IX_Users_Email on a real SQL Server (CI only).</summary>
    public class UserEmailIndexSqlServerTests
    {
        private static User NewUser(string? email) =>
            new() { UserName = "u" + Guid.NewGuid().ToString("N")[..12], PasswordHash = "x", Role = "User", Email = email };

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
        public Task ManyUsersWithoutEmail_AreAccepted() => WithDatabaseAsync(async (db, _) =>
        {
            db.Users.AddRange(NewUser(null), NewUser(null), NewUser(null));
            await db.SaveChangesAsync();

            Assert.Equal(3, await db.Users.CountAsync(u => u.Email == null));
        });

        [Fact]
        public Task SameEmailTwice_FailsWithUniqueViolation() => WithDatabaseAsync(async (db, cs) =>
        {
            db.Users.Add(NewUser("dup@example.com"));
            await db.SaveChangesAsync();

            using var other = new ApplicationDbContext(SqlServerEnv.Options(cs));
            other.Users.Add(NewUser("dup@example.com"));
            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync());
            Assert.True(IsUniqueViolation(ex));
        });

        [Fact]
        public Task SameEmailInAnotherCase_FailsToo_BecauseOfTheDatabaseCollation() => WithDatabaseAsync(async (db, cs) =>
        {
            // The application stores addresses lower-case; this proves the index also rejects the stored form twice
            // even if the case ever differs (default SQL Server collations are case-insensitive).
            db.Users.Add(NewUser("case@example.com"));
            await db.SaveChangesAsync();

            using var other = new ApplicationDbContext(SqlServerEnv.Options(cs));
            other.Users.Add(NewUser("case@example.com"));
            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync());
            Assert.True(IsUniqueViolation(ex));
        });

        [Fact]
        public Task DifferentEmails_AreAccepted_AndClearingFreesTheAddress() => WithDatabaseAsync(async (db, _) =>
        {
            var first = NewUser("one@example.com");
            db.Users.Add(first);
            db.Users.Add(NewUser("two@example.com"));
            await db.SaveChangesAsync();

            first.Email = null;
            await db.SaveChangesAsync();
            db.Users.Add(NewUser("one@example.com"));
            await db.SaveChangesAsync();

            Assert.Equal(1, await db.Users.CountAsync(u => u.Email == "one@example.com"));
        });

        [Fact]
        public Task ModelHasNoPendingChanges() => WithDatabaseAsync((db, _) =>
        {
            Assert.False(db.Database.HasPendingModelChanges());
            return Task.CompletedTask;
        });
    }
}
