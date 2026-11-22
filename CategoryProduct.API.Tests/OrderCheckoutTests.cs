using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static CategoryProduct.API.Tests.CartOrderTestHelpers;

namespace CategoryProduct.API.Tests
{
    public class OrderCheckoutTests : IClassFixture<ApiFactory>
    {
        private readonly ApiFactory _factory;
        public OrderCheckoutTests(ApiFactory factory) => _factory = factory;

        private Task<HttpClient> AdminAsync() => AuthHelper.LoggedInAsync(_factory, ApiFactory.UserName);

        // ---------- success ----------
        [Fact]
        public async Task Checkout_Returns201_WithSnapshotsAndTotal()
        {
            var admin = await AdminAsync();
            var (u, _) = await RegisterAndLoginAsync(_factory);
            var a = await CreateProductAsync(admin, "Alpha", 30, 5);
            var b = await CreateProductAsync(admin, "Beta", 12.5m, 5);
            await AddOkAsync(u, a, 2);
            await AddOkAsync(u, b, 1);

            var r = await CheckoutAsync(u);

            Assert.Equal(HttpStatusCode.Created, r.StatusCode);
            var body = await JsonAsync(r);
            var id = body.GetProperty("id").GetInt32();
            Assert.EndsWith($"api/orders/{id}", r.Headers.Location!.AbsolutePath, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("Pending", body.GetProperty("status").GetString());
            Assert.Equal(72.5m, body.GetProperty("total").GetDecimal());
            var items = body.GetProperty("items").EnumerateArray().ToList();
            Assert.Equal(2, items.Count);
            Assert.Equal("Alpha", items[0].GetProperty("productName").GetString());
            Assert.Equal(30m, items[0].GetProperty("unitPrice").GetDecimal());
            Assert.Equal(60m, items[0].GetProperty("lineTotal").GetDecimal());
            Assert.Equal(a, items[0].GetProperty("productId").GetInt32());
        }

        [Fact]
        public async Task Checkout_DeductsStock_WritesSaleMovements_ClearsCart()
        {
            var admin = await AdminAsync();
            var (u, name) = await RegisterAndLoginAsync(_factory);
            var a = await CreateProductAsync(admin, quantity: 5);
            var b = await CreateProductAsync(admin, quantity: 8);
            await AddOkAsync(u, a, 2);
            await AddOkAsync(u, b, 3);

            await CheckoutOkAsync(u);

            Assert.Equal(3, await GetProductQuantityAsync(admin, a));
            Assert.Equal(5, await GetProductQuantityAsync(admin, b));
            var userId = await InventoryHelper.UserIdAsync(_factory, name);
            foreach (var (pid, change, after) in new[] { (a, -2, 3), (b, -3, 5) })
            {
                var sales = await MovementsOfReasonAsync(admin, pid, "Sale");
                var m = Assert.Single(sales);
                Assert.Equal(change, m.GetProperty("quantityChange").GetInt32());
                Assert.Equal(after, m.GetProperty("quantityAfter").GetInt32());
                Assert.Equal(userId, m.GetProperty("userId").GetInt32());
                Assert.Equal("Checkout", m.GetProperty("note").GetString());
            }
            Assert.Equal(0, (await GetCartAsync(u)).GetProperty("items").GetArrayLength());
        }

        [Fact]
        public async Task Checkout_ExactFullStock_Succeeds()
        {
            var admin = await AdminAsync();
            var (u, _) = await RegisterAndLoginAsync(_factory);
            var a = await CreateProductAsync(admin, quantity: 4);
            await AddOkAsync(u, a, 4);
            await CheckoutOkAsync(u);
            Assert.Equal(0, await GetProductQuantityAsync(admin, a));
        }

        [Fact]
        public async Task LaterPriceChange_DoesNotAlterOrder()
        {
            var admin = await AdminAsync();
            var (u, _) = await RegisterAndLoginAsync(_factory);
            var a = await CreateProductAsync(admin, "Steady", 10, 5);
            await AddOkAsync(u, a, 2);
            var id = await CheckoutOkAsync(u);
            (await admin.PutAsJsonAsync($"api/product/{a}", new { name = "Renamed", price = 99, quantity = 3, categoryId = 3 }))
                .EnsureSuccessStatusCode();
            var order = await GetOrderAsync(u, id);
            Assert.Equal(20m, order.GetProperty("total").GetDecimal());
            var item = order.GetProperty("items")[0];
            Assert.Equal("Steady", item.GetProperty("productName").GetString());
            Assert.Equal(10m, item.GetProperty("unitPrice").GetDecimal());
        }

        // ---------- rejection ----------
        [Fact]
        public async Task EmptyCart_Returns400_NoOrder()
        {
            var (u, _) = await RegisterAndLoginAsync(_factory);
            var r = await CheckoutAsync(u);
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.Contains("Cart", await ErrorKeysAsync(r));
            Assert.Equal(0, (await JsonAsync(await u.GetAsync("api/orders"))).GetProperty("totalCount").GetInt32());
        }

        [Fact]
        public async Task InsufficientStock_Returns409_ListsAllShort_ChangesNothing()
        {
            var admin = await AdminAsync();
            var (u, _) = await RegisterAndLoginAsync(_factory);
            var a = await CreateProductAsync(admin, "ShortA", quantity: 1);
            var b = await CreateProductAsync(admin, "ShortB", quantity: 2);
            var ok = await CreateProductAsync(admin, "Fine", quantity: 10);
            await AddOkAsync(u, a, 3);
            await AddOkAsync(u, b, 5);
            await AddOkAsync(u, ok, 2);

            var r = await CheckoutAsync(u);

            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            var body = await JsonAsync(r);
            Assert.Equal("Insufficient stock", body.GetProperty("title").GetString());
            var detail = body.GetProperty("detail").GetString()!;
            Assert.Contains("ShortA (requested 3, available 1)", detail);
            Assert.Contains("ShortB (requested 5, available 2)", detail);
            var items = body.GetProperty("items").EnumerateArray().ToList();
            Assert.Equal(2, items.Count);
            Assert.Equal(new[] { a, b }, items.Select(i => i.GetProperty("productId").GetInt32()).ToArray());
            Assert.Equal(3, items[0].GetProperty("requested").GetInt32());
            Assert.Equal(1, items[0].GetProperty("available").GetInt32());

            Assert.Equal(1, await GetProductQuantityAsync(admin, a));
            Assert.Equal(10, await GetProductQuantityAsync(admin, ok));
            Assert.Empty(await MovementsOfReasonAsync(admin, ok, "Sale"));
            Assert.Equal(0, (await JsonAsync(await u.GetAsync("api/orders"))).GetProperty("totalCount").GetInt32());
            Assert.Equal(3, (await GetCartAsync(u)).GetProperty("items").GetArrayLength());
        }

        [Fact]
        public async Task ZeroStockProduct_BlocksCheckout()
        {
            var admin = await AdminAsync();
            var (u, _) = await RegisterAndLoginAsync(_factory);
            var a = await CreateProductAsync(admin, quantity: 0);
            await AddOkAsync(u, a, 1);
            Assert.Equal(HttpStatusCode.Conflict, (await CheckoutAsync(u)).StatusCode);
        }

        [Fact]
        public async Task NoToken_Returns401()
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await CheckoutAsync(_factory.CreateClient())).StatusCode);
        }

        // ---------- parallel ----------
        [Fact]
        public async Task TwoUsersBuyingLastUnit_OneWins_StockNeverNegative()
        {
            var admin = await AdminAsync();
            var (u1, _) = await RegisterAndLoginAsync(_factory);
            var (u2, _) = await RegisterAndLoginAsync(_factory);
            var a = await CreateProductAsync(admin, quantity: 1);
            await AddOkAsync(u1, a, 1);
            await AddOkAsync(u2, a, 1);

            var results = await Task.WhenAll(CheckoutAsync(u1), CheckoutAsync(u2));

            Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Created));
            Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
            Assert.Equal(0, await GetProductQuantityAsync(admin, a));
        }
    }

    public class OrderCheckoutConcurrencyTests : IClassFixture<CartRacingFactory>
    {
        private readonly CartRacingFactory _factory;
        public OrderCheckoutConcurrencyTests(CartRacingFactory factory) => _factory = factory;

        private Task<HttpClient> AdminAsync() => AuthHelper.LoggedInAsync(_factory, ApiFactory.UserName);

        [Fact]
        public async Task StockChangedDuringSave_RetriesAndSucceeds()
        {
            var admin = await AdminAsync();
            var (u, _) = await RegisterAndLoginAsync(_factory);
            var a = await CreateProductAsync(admin, quantity: 5);
            await AddOkAsync(u, a, 2);

            _factory.Interceptor.StockProductId = a;
            _factory.Interceptor.StockDelta = 100;
            _factory.Interceptor.Arm(1);
            var r = await CheckoutAsync(u);
            var hits = _factory.Interceptor.Hits;
            _factory.Interceptor.Disarm();

            Assert.Equal(HttpStatusCode.Created, r.StatusCode);
            Assert.Equal(1, hits);
            Assert.Equal(5 + 100 - 2, await GetProductQuantityAsync(admin, a));
            // InMemory is not transactional: the failed first save may leave a Sale movement behind
            // (SQL Server rolls it back, so there it is exactly one). The latest one must match the final stock.
            var sales = await MovementsOfReasonAsync(admin, a, "Sale");
            Assert.Equal(103, sales.OrderBy(m => m.GetProperty("id").GetInt32()).Last().GetProperty("quantityAfter").GetInt32());
        }

        [Fact]
        public async Task ParallelCheckoutOfSameCart_CreatesOneOrder()
        {
            // Deterministic stand-in for two parallel checkouts of one cart: a competitor commits a complete
            // checkout between our cart read and our save. Real thread timing is unusable on InMemory, whose
            // saves are not atomic (a half-applied save is visible to concurrent readers), unlike SQL Server.
            var admin = await AdminAsync();
            var (u, userName) = await RegisterAndLoginAsync(_factory);
            var a = await CreateProductAsync(admin, quantity: 10);
            await AddOkAsync(u, a, 3);

            _factory.Interceptor.RacingCheckoutUserName = userName;
            _factory.Interceptor.Arm(1);
            var r = await CheckoutAsync(u);
            var hits = _factory.Interceptor.Hits;
            _factory.Interceptor.Disarm();

            Assert.Equal(1, hits);
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode); // retry finds the cart already emptied
            // The loser must not report success (no Created) and must not retry past the emptied cart.
            // That the loser's failed save leaves no order/stock change is a SQL Server guarantee (rolled-back
            // transaction); InMemory keeps the half-applied writes of a failed save, so it is not asserted here.
        }

        [Fact]
        public async Task RacedStockBelowNeed_Ends409_NothingCreated()
        {
            var admin = await AdminAsync();
            var (u, _) = await RegisterAndLoginAsync(_factory);
            var a = await CreateProductAsync(admin, quantity: 5);
            await AddOkAsync(u, a, 5);

            _factory.Interceptor.StockProductId = a;
            _factory.Interceptor.StockDelta = -3;
            _factory.Interceptor.Arm(1);
            var r = await CheckoutAsync(u);
            _factory.Interceptor.Disarm();

            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Equal("Insufficient stock", (await JsonAsync(r)).GetProperty("title").GetString());
            Assert.Equal(2, await GetProductQuantityAsync(admin, a));
            // Atomicity (no order, no Sale movement, cart intact) is verified on SQL Server only: InMemory keeps writes of a failed save.
        }

        [Fact]
        public async Task RaceOnEveryAttempt_Returns409_After3Attempts()
        {
            var admin = await AdminAsync();
            var (u, _) = await RegisterAndLoginAsync(_factory);
            var a = await CreateProductAsync(admin, quantity: 5);
            await AddOkAsync(u, a, 2);

            _factory.Interceptor.StockProductId = a;
            _factory.Interceptor.StockDelta = 10;
            _factory.Interceptor.Arm(100);
            var r = await CheckoutAsync(u);
            var hits = _factory.Interceptor.Hits;
            _factory.Interceptor.Disarm();

            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Equal("Conflict", (await JsonAsync(r)).GetProperty("title").GetString());
            Assert.Equal(3, hits);
            // Atomicity (no order, no Sale movement, cart intact) is verified on SQL Server only: InMemory keeps writes of a failed save.
            // InMemory is not transactional: the raced stock changes (3 x 10) are the only quantity change.
            Assert.Equal(5 + 30, await GetProductQuantityAsync(admin, a));
        }
    }
}
