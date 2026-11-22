using System.Net;
using CategoryProduct.API.Models;
using Microsoft.EntityFrameworkCore;

namespace CategoryProduct.API.SqlServerTests
{
    public class CancelSqlServerTests : IClassFixture<SqlServerApiFactory>
    {
        private readonly SqlServerApiFactory _f;
        public CancelSqlServerTests(SqlServerApiFactory f) => _f = f;

        [Fact]
        public async Task ParallelCancelsOfSameOrder_RestoreStockOnce()
        {
            const int n = 8;
            var productId = await _f.SeedProductAsync(10);
            var (userId, client) = await _f.NewUserAsync("User", "buyer");
            await _f.AddCartLineAsync(userId, productId, 2);
            var checkout = await client.PostAsync("api/orders", null);
            Assert.Equal(HttpStatusCode.Created, checkout.StatusCode);
            var orderId = await _f.WithDbAsync(db => db.Orders.AsNoTracking()
                .Where(o => o.UserId == userId).Select(o => o.Id).SingleAsync());

            var responses = await Parallel8.RunAsync(n, _ => client.PostAsync($"api/orders/{orderId}/cancel", null));
            var statuses = responses.Select(r => (int)r.StatusCode).ToList();

            Assert.True(statuses.Count(s => s == 200) == 1, "Statuses: " + string.Join(",", statuses));
            Assert.All(statuses.Where(s => s != 200), s => Assert.Contains(s, new[] { 400, 409 }));

            await _f.WithDbAsync(async db =>
            {
                Assert.Equal(OrderStatus.Cancelled, (await db.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId)).Status);
                Assert.Equal(10, (await db.Products.AsNoTracking().SingleAsync(p => p.Id == productId)).Quantity);
                Assert.Equal(1, await db.StockMovements.CountAsync(m => m.ProductId == productId && m.Reason == StockMovementReason.OrderCancelled));
            });
        }
    }
}
