using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static CategoryProduct.API.Tests.CartOrderTestHelpers;

namespace CategoryProduct.API.Tests
{
    public class CartTests : IClassFixture<ApiFactory>
    {
        private readonly ApiFactory _factory;
        public CartTests(ApiFactory factory) => _factory = factory;

        private Task<HttpClient> AdminAsync() => AuthHelper.LoggedInAsync(_factory, ApiFactory.UserName);

        // ---------- read and add ----------
        [Fact]
        public async Task EmptyCart_ReturnsEmpty()
        {
            var (u, _) = await RegisterAndLoginAsync(_factory);
            var cart = await GetCartAsync(u);
            Assert.Equal(0, cart.GetProperty("items").GetArrayLength());
            Assert.Equal(0, cart.GetProperty("itemCount").GetInt32());
            Assert.Equal(0m, cart.GetProperty("total").GetDecimal());
        }

        [Fact]
        public async Task Add_ReturnsCartWithLineDetails()
        {
            var admin = await AdminAsync();
            var (u, _) = await RegisterAndLoginAsync(_factory);
            var pid = await CreateProductAsync(admin, "Thing", 12.5m, 7);
            var r = await AddToCartAsync(u, pid, 2);
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            var cart = await JsonAsync(r);
            var line = cart.GetProperty("items")[0];
            Assert.Equal("Thing", line.GetProperty("productName").GetString());
            Assert.Equal(12.5m, line.GetProperty("unitPrice").GetDecimal());
            Assert.Equal(25m, line.GetProperty("lineTotal").GetDecimal());
            Assert.Equal(7, line.GetProperty("availableQuantity").GetInt32());
            Assert.Equal(25m, cart.GetProperty("total").GetDecimal());
        }

        [Fact]
        public async Task Add_MergesIntoOneLine()
        {
            var admin = await AdminAsync();
            var (u, _) = await RegisterAndLoginAsync(_factory);
            var pid = await CreateProductAsync(admin);
            await AddOkAsync(u, pid, 2);
            await AddOkAsync(u, pid, 3);
            var cart = await GetCartAsync(u);
            Assert.Equal(1, cart.GetProperty("items").GetArrayLength());
            Assert.Equal(5, cart.GetProperty("items")[0].GetProperty("quantity").GetInt32());
        }

        [Fact]
        public async Task Add_MergeAbove100_Returns400_AndLineUnchanged()
        {
            var admin = await AdminAsync();
            var (u, _) = await RegisterAndLoginAsync(_factory);
            var pid = await CreateProductAsync(admin);
            await AddOkAsync(u, pid, 98);
            var r = await AddToCartAsync(u, pid, 5);
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.Contains("Quantity", await ErrorKeysAsync(r));
            Assert.Equal(98, (await GetCartAsync(u)).GetProperty("items")[0].GetProperty("quantity").GetInt32());
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(101)]
        public async Task Add_InvalidQuantity_Returns400(int q)
        {
            var admin = await AdminAsync();
            var (u, _) = await RegisterAndLoginAsync(_factory);
            var pid = await CreateProductAsync(admin);
            var r = await AddToCartAsync(u, pid, q);
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.Equal(0, (await GetCartAsync(u)).GetProperty("items").GetArrayLength());
        }

        [Fact]
        public async Task Add_ProductIdZero_Returns400()
        {
            var (u, _) = await RegisterAndLoginAsync(_factory);
            Assert.Equal(HttpStatusCode.BadRequest, (await AddToCartAsync(u, 0, 1)).StatusCode);
        }

        [Fact]
        public async Task Add_UnknownProduct_Returns404_NothingStored()
        {
            var (u, _) = await RegisterAndLoginAsync(_factory);
            Assert.Equal(HttpStatusCode.NotFound, (await AddToCartAsync(u, 999999, 1)).StatusCode);
            Assert.Equal(0, (await GetCartAsync(u)).GetProperty("items").GetArrayLength());
        }

        [Fact]
        public async Task Lines_AreOrderedByAddTime()
        {
            var admin = await AdminAsync();
            var (u, _) = await RegisterAndLoginAsync(_factory);
            var a = await CreateProductAsync(admin);
            var b = await CreateProductAsync(admin);
            var c = await CreateProductAsync(admin);
            await AddOkAsync(u, b, 1);
            await AddOkAsync(u, c, 1);
            await AddOkAsync(u, a, 1);
            await AddOkAsync(u, b, 1); // merge keeps position
            var ids = (await GetCartAsync(u)).GetProperty("items").EnumerateArray()
                .Select(i => i.GetProperty("productId").GetInt32()).ToList();
            Assert.Equal(new[] { b, c, a }, ids);
        }

        // ---------- update / remove / clear ----------
        [Fact]
        public async Task Put_SetsExactQuantity()
        {
            var admin = await AdminAsync();
            var (u, _) = await RegisterAndLoginAsync(_factory);
            var pid = await CreateProductAsync(admin);
            await AddOkAsync(u, pid, 3);
            var r = await u.PutAsJsonAsync($"api/cart/items/{pid}", new { quantity = 7 });
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.Equal(7, (await JsonAsync(r)).GetProperty("items")[0].GetProperty("quantity").GetInt32());
        }

        [Theory]
        [InlineData(0)]
        [InlineData(101)]
        public async Task Put_InvalidQuantity_Returns400(int q)
        {
            var admin = await AdminAsync();
            var (u, _) = await RegisterAndLoginAsync(_factory);
            var pid = await CreateProductAsync(admin);
            await AddOkAsync(u, pid, 3);
            var r = await u.PutAsJsonAsync($"api/cart/items/{pid}", new { quantity = q });
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.Equal(3, (await GetCartAsync(u)).GetProperty("items")[0].GetProperty("quantity").GetInt32());
        }

        [Fact]
        public async Task PutAndDelete_MissingLine_Return404()
        {
            var (u, _) = await RegisterAndLoginAsync(_factory);
            Assert.Equal(HttpStatusCode.NotFound, (await u.PutAsJsonAsync("api/cart/items/1", new { quantity = 2 })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await u.DeleteAsync("api/cart/items/1")).StatusCode);
        }

        [Fact]
        public async Task DeleteLine_ReturnsCart()
        {
            var admin = await AdminAsync();
            var (u, _) = await RegisterAndLoginAsync(_factory);
            var a = await CreateProductAsync(admin);
            var b = await CreateProductAsync(admin);
            await AddOkAsync(u, a, 1);
            await AddOkAsync(u, b, 1);
            var r = await u.DeleteAsync($"api/cart/items/{a}");
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            var items = (await JsonAsync(r)).GetProperty("items");
            Assert.Equal(1, items.GetArrayLength());
            Assert.Equal(b, items[0].GetProperty("productId").GetInt32());
        }

        [Fact]
        public async Task Clear_Returns204_AlsoWhenEmpty()
        {
            var admin = await AdminAsync();
            var (u, _) = await RegisterAndLoginAsync(_factory);
            await AddOkAsync(u, await CreateProductAsync(admin), 1);
            Assert.Equal(HttpStatusCode.NoContent, (await u.DeleteAsync("api/cart")).StatusCode);
            Assert.Equal(0, (await GetCartAsync(u)).GetProperty("items").GetArrayLength());
            Assert.Equal(HttpStatusCode.NoContent, (await u.DeleteAsync("api/cart")).StatusCode);
        }

        // ---------- isolation and auth ----------
        [Fact]
        public async Task Carts_AreIsolatedPerUser()
        {
            var admin = await AdminAsync();
            var (a, _) = await RegisterAndLoginAsync(_factory);
            var (b, _) = await RegisterAndLoginAsync(_factory);
            var pid = await CreateProductAsync(admin);
            await AddOkAsync(a, pid, 4);

            Assert.Equal(0, (await GetCartAsync(b)).GetProperty("items").GetArrayLength());
            Assert.Equal(HttpStatusCode.NotFound, (await b.PutAsJsonAsync($"api/cart/items/{pid}", new { quantity = 1 })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await b.DeleteAsync($"api/cart/items/{pid}")).StatusCode);
            await b.DeleteAsync("api/cart");
            Assert.Equal(4, (await GetCartAsync(a)).GetProperty("items")[0].GetProperty("quantity").GetInt32());
            Assert.Equal(0, (await GetCartAsync(admin)).GetProperty("items").GetArrayLength());
        }

        [Fact]
        public async Task NoToken_Returns401_OnAllRoutes()
        {
            var c = _factory.CreateClient();
            Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("api/cart")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await AddToCartAsync(c, 1, 1)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await c.PutAsJsonAsync("api/cart/items/1", new { quantity = 1 })).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await c.DeleteAsync("api/cart/items/1")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await c.DeleteAsync("api/cart")).StatusCode);
        }

        [Fact]
        public async Task PriceChange_ShowsInNextRead()
        {
            var admin = await AdminAsync();
            var (u, _) = await RegisterAndLoginAsync(_factory);
            var pid = await CreateProductAsync(admin, "Priced", 10, 10);
            await AddOkAsync(u, pid, 2);
            var put = await admin.PutAsJsonAsync($"api/product/{pid}",
                new { name = "Priced", price = 15, quantity = 10, categoryId = 3 });
            put.EnsureSuccessStatusCode();
            var cart = await GetCartAsync(u);
            Assert.Equal(15m, cart.GetProperty("items")[0].GetProperty("unitPrice").GetDecimal());
            Assert.Equal(30m, cart.GetProperty("total").GetDecimal());
        }

        [Fact]
        public async Task LineAboveStock_IsKept_WithRealAvailableQuantity()
        {
            var admin = await AdminAsync();
            var (u, _) = await RegisterAndLoginAsync(_factory);
            var pid = await CreateProductAsync(admin, quantity: 2);
            await AddOkAsync(u, pid, 5);
            var line = (await GetCartAsync(u)).GetProperty("items")[0];
            Assert.Equal(5, line.GetProperty("quantity").GetInt32());
            Assert.Equal(2, line.GetProperty("availableQuantity").GetInt32());
        }

        [Fact]
        public async Task ProductDeleted_CartStillReadable()
        {
            var admin = await AdminAsync();
            var (u, _) = await RegisterAndLoginAsync(_factory);
            var pid = await CreateProductAsync(admin);
            await AddOkAsync(u, pid, 1);
            (await admin.DeleteAsync($"api/product/{pid}")).EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.OK, (await u.GetAsync("api/cart")).StatusCode);
        }
    }
}
