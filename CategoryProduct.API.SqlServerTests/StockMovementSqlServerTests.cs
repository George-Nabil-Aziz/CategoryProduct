using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;

namespace CategoryProduct.API.SqlServerTests
{
    public class StockMovementSqlServerTests : IClassFixture<SqlServerApiFactory>
    {
        private readonly SqlServerApiFactory _f;
        public StockMovementSqlServerTests(SqlServerApiFactory f) => _f = f;

        [Fact]
        public async Task ParallelMovements_FinalQuantityEqualsInitialPlusSuccesses()
        {
            const int n = 10;
            const int initial = 5;
            var productId = await _f.SeedProductAsync(initial);
            var (_, admin) = await _f.NewUserAsync("Admin", "admin");

            var responses = await Parallel8.RunAsync(n, i => admin.PostAsJsonAsync(
                $"api/product/{productId}/stock-movements",
                new { quantityChange = i + 1, reason = "Restock", note = "parallel" }));

            var succeeded = new List<int>();
            for (var i = 0; i < n; i++)
            {
                var code = (int)responses[i].StatusCode;
                Assert.Contains(code, new[] { 201, 409 });
                if (code == 201) succeeded.Add(i + 1);
            }
            Assert.NotEmpty(succeeded);

            await _f.WithDbAsync(async db =>
            {
                var product = await db.Products.AsNoTracking().SingleAsync(p => p.Id == productId);
                Assert.Equal(initial + succeeded.Sum(), product.Quantity);

                var movements = await db.StockMovements.AsNoTracking()
                    .Where(m => m.ProductId == productId).OrderBy(m => m.Id).ToListAsync();
                Assert.Equal(succeeded.Count, movements.Count);
                Assert.Equal(succeeded.Sum(), movements.Sum(m => m.QuantityChange));

                // All changes are positive, so QuantityAfter increases along the serial order of application.
                var running = initial;
                foreach (var m in movements.OrderBy(m => m.QuantityAfter))
                {
                    running += m.QuantityChange;
                    Assert.Equal(running, m.QuantityAfter);
                }
                Assert.Equal(product.Quantity, running);
            });
        }
    }
}
