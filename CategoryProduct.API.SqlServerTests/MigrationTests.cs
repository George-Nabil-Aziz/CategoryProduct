using CategoryProduct.API.Data;
using Microsoft.EntityFrameworkCore;

namespace CategoryProduct.API.SqlServerTests
{
    public class MigrationTests
    {
        [Fact]
        public async Task Factory_StartsAndServesHealth()
        {
            using var factory = new SqlServerApiFactory();
            var response = await factory.CreateClient().GetAsync("/health");
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task EmptyDatabase_MigratesFully_AndModelHasNoPendingChanges()
        {
            var cs = SqlServerEnv.BuildConnectionString(SqlServerEnv.NewDatabaseName());
            using var db = new ApplicationDbContext(SqlServerEnv.Options(cs));
            try
            {
                await db.Database.MigrateAsync();
                Assert.Empty(await db.Database.GetPendingMigrationsAsync());
                var applied = await db.Database.GetAppliedMigrationsAsync();
                Assert.Equal(db.Database.GetMigrations().Count(), applied.Count());
                Assert.False(db.Database.HasPendingModelChanges());
            }
            finally
            {
                await db.Database.EnsureDeletedAsync();
            }
        }
    }
}
