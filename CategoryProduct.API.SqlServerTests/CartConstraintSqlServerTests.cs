using System.Net.Http.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CategoryProduct.API.SqlServerTests
{
    public class CartConstraintSqlServerTests : IClassFixture<SqlServerApiFactory>
    {
        private readonly SqlServerApiFactory _f;
        public CartConstraintSqlServerTests(SqlServerApiFactory f) => _f = f;

        [Fact]
        public async Task DuplicateCartRow_IsRejectedByUniqueIndex()
        {
            var productId = await _f.SeedProductAsync(10);
            var userId = await _f.CreateUserAsync(_f.UniqueName("dup"), "User");

            await _f.AddCartLineAsync(userId, productId, 1);

            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => _f.AddCartLineAsync(userId, productId, 1));
            var sql = Assert.IsType<SqlException>(ex.InnerException);
            Assert.Contains(sql.Number, new[] { 2601, 2627 });
        }

        [Fact]
        public async Task ConcurrentAddOfSamePair_EndsWithOneRow()
        {
            var productId = await _f.SeedProductAsync(10);
            var (userId, client) = await _f.NewUserAsync("User", "adder");

            var responses = await Parallel8.RunAsync(4, _ =>
                client.PostAsJsonAsync("api/cart/items", new { productId, quantity = 1 }));
            var statuses = responses.Select(r => (int)r.StatusCode).ToList();

            Assert.True(statuses.Contains(200), "Statuses: " + string.Join(",", statuses));
            var rows = await _f.WithDbAsync(db =>
                db.CartItems.CountAsync(c => c.UserId == userId && c.ProductId == productId));
            Assert.Equal(1, rows);
        }
    }
}
