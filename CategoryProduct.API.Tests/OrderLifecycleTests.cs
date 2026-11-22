using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CategoryProduct.API.Models;
using static CategoryProduct.API.Tests.CartOrderTestHelpers;

namespace CategoryProduct.API.Tests
{
    public class OrderLifecycleTests : IClassFixture<ApiFactory>
    {
        private readonly ApiFactory _factory;
        public OrderLifecycleTests(ApiFactory factory) => _factory = factory;

        private Task<HttpClient> AdminAsync() => AuthHelper.LoggedInAsync(_factory, ApiFactory.UserName);

        private static async Task<(int OrderId, int ProductA, int ProductB)> TwoLineOrderAsync(HttpClient admin, HttpClient user)
        {
            var a = await CreateProductAsync(admin, quantity: 5);
            var b = await CreateProductAsync(admin, quantity: 8);
            await AddOkAsync(user, a, 2);
            await AddOkAsync(user, b, 3);
            return (await CheckoutOkAsync(user), a, b);
        }

        // ---------- cancel ----------
        [Fact]
        public async Task OwnerCancel_RestoresStock_WritesMovements_RefreshesUpdated()
        {
            var admin = await AdminAsync();
            var (u, name) = await RegisterAndLoginAsync(_factory);
            var (id, a, b) = await TwoLineOrderAsync(admin, u);
            var before = (await GetOrderAsync(u, id)).GetProperty("updatedAtUtc").GetDateTime();
            await Task.Delay(20);

            var r = await CancelAsync(u, id);

            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            var body = await JsonAsync(r);
            Assert.Equal("Cancelled", body.GetProperty("status").GetString());
            Assert.True(body.GetProperty("updatedAtUtc").GetDateTime() > before);
            Assert.Equal(5, await GetProductQuantityAsync(admin, a));
            Assert.Equal(8, await GetProductQuantityAsync(admin, b));
            var userId = await InventoryHelper.UserIdAsync(_factory, name);
            foreach (var (pid, change, after) in new[] { (a, 2, 5), (b, 3, 8) })
            {
                var m = Assert.Single(await MovementsOfReasonAsync(admin, pid, "OrderCancelled"));
                Assert.Equal(change, m.GetProperty("quantityChange").GetInt32());
                Assert.Equal(after, m.GetProperty("quantityAfter").GetInt32());
                Assert.Equal(userId, m.GetProperty("userId").GetInt32());
                Assert.Equal($"Order #{id} cancelled", m.GetProperty("note").GetString());
                var all = await GetMovementsAsync(admin, pid);
                Assert.Equal(after, all.GetProperty("items").EnumerateArray().Sum(x => x.GetProperty("quantityChange").GetInt32()));
            }
        }

        [Fact]
        public async Task AdminCancelsOthersOrder_NonOwnerGets404()
        {
            var admin = await AdminAsync();
            var (u, _) = await RegisterAndLoginAsync(_factory);
            var (other, _) = await RegisterAndLoginAsync(_factory);
            var (id, a, _) = await TwoLineOrderAsync(admin, u);

            Assert.Equal(HttpStatusCode.NotFound, (await CancelAsync(other, id)).StatusCode);
            Assert.Equal("Pending", await OrderStatusAsync(u, id));
            Assert.Equal(3, await GetProductQuantityAsync(admin, a));

            Assert.Equal(HttpStatusCode.OK, (await CancelAsync(admin, id)).StatusCode);
            Assert.Equal(5, await GetProductQuantityAsync(admin, a));
        }

        [Fact]
        public async Task Cancel_UnknownAndNoToken()
        {
            var (u, _) = await RegisterAndLoginAsync(_factory);
            Assert.Equal(HttpStatusCode.NotFound, (await CancelAsync(u, 999999)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await CancelAsync(_factory.CreateClient(), 1)).StatusCode);
        }

        [Fact]
        public async Task Cancel_NonPending_Returns409_NoStockChange()
        {
            var admin = await AdminAsync();
            var (u, _) = await RegisterAndLoginAsync(_factory);
            var (id, a, _) = await TwoLineOrderAsync(admin, u);
            (await SetStatusAsync(admin, id, "Confirmed")).EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.Conflict, (await CancelAsync(u, id)).StatusCode);
            Assert.Equal(3, await GetProductQuantityAsync(admin, a));

            var (id2, a2, _) = await TwoLineOrderAsync(admin, u);
            (await CancelAsync(u, id2)).EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.Conflict, (await CancelAsync(u, id2)).StatusCode);
            Assert.Equal(5, await GetProductQuantityAsync(admin, a2));
            Assert.Single(await MovementsOfReasonAsync(admin, a2, "OrderCancelled"));
        }

        [Fact]
        public async Task Cancel_AddsToCurrentStock_WhenChangedAfterCheckout()
        {
            var admin = await AdminAsync();
            var (u, _) = await RegisterAndLoginAsync(_factory);
            var (id, a, _) = await TwoLineOrderAsync(admin, u);
            (await InventoryHelper.MoveAsync(admin, a, 10)).EnsureSuccessStatusCode();
            (await CancelAsync(u, id)).EnsureSuccessStatusCode();
            Assert.Equal(3 + 10 + 2, await GetProductQuantityAsync(admin, a));
        }

        [Fact]
        public async Task Cancel_DeletedProductLine_Skipped_OthersRestored()
        {
            var admin = await AdminAsync();
            var (u, _) = await RegisterAndLoginAsync(_factory);
            var (id, a, b) = await TwoLineOrderAsync(admin, u);
            (await admin.DeleteAsync($"api/product/{a}")).EnsureSuccessStatusCode();

            var r = await CancelAsync(u, id);

            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.Equal("Cancelled", (await JsonAsync(r)).GetProperty("status").GetString());
            Assert.Equal(8, await GetProductQuantityAsync(admin, b));
            Assert.Single(await MovementsOfReasonAsync(admin, b, "OrderCancelled"));
        }

        // ---------- admin status ----------
        [Fact]
        public async Task Confirm_Pending_Returns200_StockUntouched_OwnerCannotCancelAfter()
        {
            var admin = await AdminAsync();
            var (u, _) = await RegisterAndLoginAsync(_factory);
            var (id, a, _) = await TwoLineOrderAsync(admin, u);
            var before = (await GetOrderAsync(u, id)).GetProperty("updatedAtUtc").GetDateTime();
            await Task.Delay(20);

            var r = await SetStatusAsync(admin, id, "confirmed");

            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            var body = await JsonAsync(r);
            Assert.Equal("Confirmed", body.GetProperty("status").GetString());
            Assert.True(body.GetProperty("updatedAtUtc").GetDateTime() > before);
            Assert.Equal(3, await GetProductQuantityAsync(admin, a));
            Assert.Equal(HttpStatusCode.Conflict, (await CancelAsync(u, id)).StatusCode);
        }

        [Fact]
        public async Task AdminSetCancelled_RestoresLikeCancel()
        {
            var admin = await AdminAsync();
            var (u, _) = await RegisterAndLoginAsync(_factory);
            var (id, a, b) = await TwoLineOrderAsync(admin, u);
            var r = await SetStatusAsync(admin, id, "Cancelled");
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.Equal(5, await GetProductQuantityAsync(admin, a));
            Assert.Equal(8, await GetProductQuantityAsync(admin, b));
            Assert.Single(await MovementsOfReasonAsync(admin, a, "OrderCancelled"));
        }

        [Theory]
        [InlineData("Confirmed", "Confirmed")]
        [InlineData("Confirmed", "Cancelled")]
        [InlineData("Confirmed", "Pending")]
        [InlineData("Cancelled", "Confirmed")]
        [InlineData("Cancelled", "Cancelled")]
        [InlineData("Cancelled", "Pending")]
        [InlineData("Pending", "Pending")]
        public async Task InvalidTransitions_Return409(string from, string to)
        {
            var admin = await AdminAsync();
            var (u, _) = await RegisterAndLoginAsync(_factory);
            var (id, a, _) = await TwoLineOrderAsync(admin, u);
            if (from != "Pending") (await SetStatusAsync(admin, id, from)).EnsureSuccessStatusCode();
            var qty = await GetProductQuantityAsync(admin, a);

            Assert.Equal(HttpStatusCode.Conflict, (await SetStatusAsync(admin, id, to)).StatusCode);
            Assert.Equal(from, await OrderStatusAsync(u, id));
            Assert.Equal(qty, await GetProductQuantityAsync(admin, a));
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("abc")]
        [InlineData("1")]
        public async Task InvalidStatusValue_Returns400(string? status)
        {
            var admin = await AdminAsync();
            var (u, _) = await RegisterAndLoginAsync(_factory);
            var (id, _, _) = await TwoLineOrderAsync(admin, u);
            var r = await SetStatusAsync(admin, id, status);
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.Contains("Status", await ErrorKeysAsync(r));
        }

        [Fact]
        public async Task SetStatus_AuthAndUnknown()
        {
            var admin = await AdminAsync();
            var (u, _) = await RegisterAndLoginAsync(_factory);
            var (id, _, _) = await TwoLineOrderAsync(admin, u);
            Assert.Equal(HttpStatusCode.Forbidden, (await SetStatusAsync(u, id, "Confirmed")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await SetStatusAsync(_factory.CreateClient(), id, "Confirmed")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await SetStatusAsync(admin, 999999, "Confirmed")).StatusCode);
        }
    }

    public class OrderLifecycleConcurrencyTests : IClassFixture<CartRacingFactory>
    {
        private readonly CartRacingFactory _factory;
        public OrderLifecycleConcurrencyTests(CartRacingFactory factory) => _factory = factory;

        private Task<HttpClient> AdminAsync() => AuthHelper.LoggedInAsync(_factory, ApiFactory.UserName);

        private static async Task<(int OrderId, int Product)> OrderAsync(HttpClient admin, HttpClient user, int stock = 5, int qty = 2)
        {
            var a = await CreateProductAsync(admin, quantity: stock);
            await AddOkAsync(user, a, qty);
            return (await CheckoutOkAsync(user), a);
        }

        [Fact]
        public async Task Cancel_RacedByStockChange_RetriesAndRestores()
        {
            var admin = await AdminAsync();
            var (u, _) = await RegisterAndLoginAsync(_factory);
            var (id, a) = await OrderAsync(admin, u);

            _factory.Interceptor.StockProductId = a;
            _factory.Interceptor.Arm(1);
            var r = await CancelAsync(u, id);
            _factory.Interceptor.Disarm();

            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.Equal(3 + 100 + 2, await GetProductQuantityAsync(admin, a));
            // InMemory may keep the failed first save's movement (SQL Server: exactly one); the latest must match the final stock.
            var restores = await MovementsOfReasonAsync(admin, a, "OrderCancelled");
            Assert.Equal(105, restores.OrderBy(m => m.GetProperty("id").GetInt32()).Last().GetProperty("quantityAfter").GetInt32());
        }

        [Fact]
        public async Task Cancel_RaceOnEveryAttempt_Returns409_OrderStaysPending()
        {
            var admin = await AdminAsync();
            var (u, _) = await RegisterAndLoginAsync(_factory);
            var (id, a) = await OrderAsync(admin, u);

            _factory.Interceptor.StockProductId = a;
            _factory.Interceptor.StockDelta = 10;
            _factory.Interceptor.Arm(100);
            var r = await CancelAsync(u, id);
            var hits = _factory.Interceptor.Hits;
            _factory.Interceptor.Disarm();

            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Equal(3, hits);
            Assert.Equal("Pending", await OrderStatusAsync(u, id));
            Assert.Equal(3 + 30, await GetProductQuantityAsync(admin, a));
        }

        [Fact]
        public async Task Cancel_RacedStatusToConfirmed_Returns409_StockUnchanged()
        {
            var admin = await AdminAsync();
            var (u, _) = await RegisterAndLoginAsync(_factory);
            var (id, a) = await OrderAsync(admin, u);

            _factory.Interceptor.RaceStatus = OrderStatus.Confirmed;
            _factory.Interceptor.Arm(1);
            var r = await CancelAsync(u, id);
            _factory.Interceptor.Disarm();

            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Equal("Confirmed", await OrderStatusAsync(u, id));
            // "Stock unchanged" holds on SQL Server (rollback); InMemory keeps the product write of the failed save.
        }

        [Fact]
        public async Task Confirm_RacedStatusToCancelled_Returns409()
        {
            var admin = await AdminAsync();
            var (u, _) = await RegisterAndLoginAsync(_factory);
            var (id, _) = await OrderAsync(admin, u);

            _factory.Interceptor.RaceStatus = OrderStatus.Cancelled;
            _factory.Interceptor.Arm(1);
            var r = await SetStatusAsync(admin, id, "Confirmed");
            _factory.Interceptor.Disarm();

            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Equal("Cancelled", await OrderStatusAsync(u, id));
        }

        [Fact]
        public async Task ParallelCancels_SecondLoses409_OrderCancelledOnce()
        {
            var admin = await AdminAsync();
            var (u, _) = await RegisterAndLoginAsync(_factory);
            var (id, a) = await OrderAsync(admin, u);

            // A competing cancel commits completely between this request's read and its save.
            _factory.Interceptor.RacingCancelOrderId = id;
            _factory.Interceptor.Arm(1);
            var r = await CancelAsync(u, id);
            _factory.Interceptor.Disarm();

            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Equal("Cancelled", await OrderStatusAsync(u, id));
            Assert.NotEmpty(await MovementsOfReasonAsync(admin, a, "OrderCancelled"));
            // "Restored exactly once" holds on SQL Server (loser is rolled back; see SqlServerTests);
            // InMemory is non-transactional and keeps the loser's product write, so stock is not asserted.
        }

        [Fact]
        public async Task ParallelConfirmAndCancel_ExactlyOneWins()
        {
            var admin = await AdminAsync();
            var (u, _) = await RegisterAndLoginAsync(_factory);
            var (id, a) = await OrderAsync(admin, u);

            var results = await Task.WhenAll(SetStatusAsync(admin, id, "Confirmed"), CancelAsync(u, id));

            Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
            Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
            var status = await OrderStatusAsync(u, id);
            if (status == "Cancelled") Assert.Equal(5, await GetProductQuantityAsync(admin, a));
            // When confirm wins, stock stays 3 on SQL Server; InMemory may keep the loser's product write.
        }
    }
}
