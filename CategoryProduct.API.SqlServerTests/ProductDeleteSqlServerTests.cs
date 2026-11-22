using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;

namespace CategoryProduct.API.SqlServerTests
{
    public class ProductDeleteSqlServerTests : IClassFixture<SqlServerApiFactory>
    {
        private readonly SqlServerApiFactory _f;
        public ProductDeleteSqlServerTests(SqlServerApiFactory f) => _f = f;

        [Fact]
        public async Task DeleteProduct_KeepsOrderHistory_AndCascadesCartLinesAndMovements()
        {
            var productId = await _f.SeedProductAsync(10);
            var (_, admin) = await _f.NewUserAsync("Admin", "admin");
            var (buyerId, buyer) = await _f.NewUserAsync("User", "buyer");
            var (otherId, _) = await _f.NewUserAsync("User", "other");

            // Order that references the product (also creates a Sale stock movement).
            var add = await buyer.PostAsJsonAsync("api/cart/items", new { productId, quantity = 2 });
            add.EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.Created, (await buyer.PostAsync("api/orders", null)).StatusCode);

            // A restock movement and another user's cart line.
            var restock = await admin.PostAsJsonAsync($"api/product/{productId}/stock-movements",
                new { quantityChange = 5, reason = "Restock" });
            Assert.Equal(HttpStatusCode.Created, restock.StatusCode);
            await _f.AddCartLineAsync(otherId, productId, 1);

            await _f.WithDbAsync(async db =>
            {
                Assert.True(await db.StockMovements.CountAsync(m => m.ProductId == productId) >= 2);
                Assert.Equal(1, await db.CartItems.CountAsync(c => c.ProductId == productId));
            });

            var delete = await admin.DeleteAsync($"api/product/{productId}");
            Assert.Equal(HttpStatusCode.OK, delete.StatusCode);

            await _f.WithDbAsync(async db =>
            {
                var order = await db.Orders.Include(o => o.Items).SingleAsync(o => o.UserId == buyerId);
                var item = Assert.Single(order.Items);
                Assert.Null(item.ProductId);
                Assert.Equal(0, await db.CartItems.CountAsync(c => c.ProductId == productId));
                Assert.Equal(0, await db.StockMovements.CountAsync(m => m.ProductId == productId));
                Assert.False(await db.Products.AnyAsync(p => p.Id == productId));
            });
        }
    }
}
