using System.Net;
using CategoryProduct.API.Models;
using Microsoft.EntityFrameworkCore;

namespace CategoryProduct.API.SqlServerTests
{
    public class CheckoutSqlServerTests : IClassFixture<SqlServerApiFactory>
    {
        private readonly SqlServerApiFactory _f;
        public CheckoutSqlServerTests(SqlServerApiFactory f) => _f = f;

        [Fact]
        public async Task InsufficientStock_ChangesNothing()
        {
            var productId = await _f.SeedProductAsync(3);
            var (userId, client) = await _f.NewUserAsync("User", "buyer");
            await _f.AddCartLineAsync(userId, productId, 5);

            var r = await client.PostAsync("api/orders", null);
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);

            await _f.WithDbAsync(async db =>
            {
                Assert.Equal(0, await db.Orders.CountAsync(o => o.UserId == userId));
                Assert.Equal(0, await db.StockMovements.CountAsync(m => m.ProductId == productId));
                var lines = await db.CartItems.Where(c => c.UserId == userId).ToListAsync();
                Assert.Single(lines);
                Assert.Equal(5, lines[0].Quantity);
                Assert.Equal(3, (await db.Products.AsNoTracking().SingleAsync(p => p.Id == productId)).Quantity);
            });
        }

        [Fact]
        public async Task ParallelCheckoutOfSameCart_CreatesExactlyOneOrder()
        {
            const int n = 8;
            var productId = await _f.SeedProductAsync(10);
            var (userId, client) = await _f.NewUserAsync("User", "buyer");
            await _f.AddCartLineAsync(userId, productId, 2);

            var responses = await Parallel8.RunAsync(n, _ => client.PostAsync("api/orders", null));
            var statuses = responses.Select(r => (int)r.StatusCode).ToList();

            Assert.True(statuses.Count(s => s == 201) == 1, "Statuses: " + string.Join(",", statuses));
            Assert.All(statuses.Where(s => s != 201), s => Assert.Contains(s, new[] { 400, 409 }));

            await _f.WithDbAsync(async db =>
            {
                Assert.Equal(1, await db.Orders.CountAsync(o => o.UserId == userId));
                Assert.Equal(1, await db.StockMovements.CountAsync(m => m.ProductId == productId && m.Reason == StockMovementReason.Sale));
                Assert.Equal(8, (await db.Products.AsNoTracking().SingleAsync(p => p.Id == productId)).Quantity);
                Assert.Equal(0, await db.CartItems.CountAsync(c => c.UserId == userId));
            });
        }
    }
}
