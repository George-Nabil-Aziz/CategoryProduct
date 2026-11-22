using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace CategoryProduct.API.SqlServerTests
{
    public class AdminDemotionSqlServerTests : IClassFixture<SqlServerApiFactory>
    {
        private readonly SqlServerApiFactory _f;
        private readonly ITestOutputHelper _output;
        public AdminDemotionSqlServerTests(SqlServerApiFactory f, ITestOutputHelper output)
        {
            _f = f;
            _output = output;
        }

        [Fact]
        public async Task ParallelDemotionOfOnlyTwoAdmins_LeavesAtLeastOneAdmin()
        {
            // The migrations seed testuser as an Admin; remove it so exactly two admins exist.
            await _f.WithDbAsync(async db =>
            {
                var seeded = await db.Users.Where(u => u.UserName == "testuser").ToListAsync();
                db.Users.RemoveRange(seeded);
                await db.SaveChangesAsync();
            });

            var (idA, clientA) = await _f.NewUserAsync("Admin", "adminA");
            var (idB, clientB) = await _f.NewUserAsync("Admin", "adminB");
            Assert.Equal(2, await _f.WithDbAsync(db => db.Users.CountAsync(u => u.Role == "Admin")));

            var responses = await Parallel8.RunAsync(2, i => i == 0
                ? clientA.PutAsJsonAsync($"api/users/{idB}/role", new { role = "User" })
                : clientB.PutAsJsonAsync($"api/users/{idA}/role", new { role = "User" }));

            var statuses = responses.Select(r => (int)r.StatusCode).ToList();
            _output.WriteLine("Demotion statuses: " + string.Join(", ", statuses));

            Assert.True(statuses.Count(s => s == 200) <= 1, "Statuses: " + string.Join(",", statuses));
            var admins = await _f.WithDbAsync(db => db.Users.CountAsync(u => u.Role == "Admin"));
            Assert.True(admins >= 1, "At least one admin must remain.");
        }
    }
}
