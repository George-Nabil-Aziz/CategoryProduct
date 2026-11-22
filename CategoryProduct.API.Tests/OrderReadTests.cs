using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static CategoryProduct.API.Tests.CartOrderTestHelpers;

namespace CategoryProduct.API.Tests
{
    public class OrderReadTests : IClassFixture<ApiFactory>
    {
        private readonly ApiFactory _factory;
        public OrderReadTests(ApiFactory factory) => _factory = factory;

        private Task<HttpClient> AdminAsync() => AuthHelper.LoggedInAsync(_factory, ApiFactory.UserName);

        private async Task<int> PlaceOrderAsync(HttpClient admin, HttpClient user, int qty = 1, int stock = 50)
        {
            var pid = await CreateProductAsync(admin, quantity: stock);
            await AddOkAsync(user, pid, qty);
            return await CheckoutOkAsync(user);
        }

        // ---------- list ----------
        [Fact]
        public async Task GetMine_ReturnsOnlyCallersOrders_NewestFirst()
        {
            var admin = await AdminAsync();
            var (a, _) = await RegisterAndLoginAsync(_factory);
            var (b, _) = await RegisterAndLoginAsync(_factory);
            var a1 = await PlaceOrderAsync(admin, a);
            var a2 = await PlaceOrderAsync(admin, a, 2);
            await PlaceOrderAsync(admin, b);

            var page = await JsonAsync(await a.GetAsync("api/orders"));
            Assert.Equal(2, page.GetProperty("totalCount").GetInt32());
            var items = page.GetProperty("items").EnumerateArray().ToList();
            Assert.Equal(new[] { a2, a1 }, items.Select(i => i.GetProperty("id").GetInt32()).ToArray());
            Assert.Equal(1, items[0].GetProperty("itemCount").GetInt32());
            Assert.Equal(20m, items[0].GetProperty("total").GetDecimal());
            Assert.Equal("Pending", items[0].GetProperty("status").GetString());
        }

        [Fact]
        public async Task GetMine_Paging_AndClamping()
        {
            var admin = await AdminAsync();
            var (u, _) = await RegisterAndLoginAsync(_factory);
            for (var i = 0; i < 5; i++) await PlaceOrderAsync(admin, u);

            var p2 = await JsonAsync(await u.GetAsync("api/orders?page=3&pageSize=2"));
            Assert.Equal(5, p2.GetProperty("totalCount").GetInt32());
            Assert.Equal(3, p2.GetProperty("totalPages").GetInt32());
            Assert.Equal(1, p2.GetProperty("items").GetArrayLength());

            Assert.Equal(1, (await JsonAsync(await u.GetAsync("api/orders?pageSize=0"))).GetProperty("pageSize").GetInt32());
            Assert.Equal(50, (await JsonAsync(await u.GetAsync("api/orders?pageSize=500"))).GetProperty("pageSize").GetInt32());
            Assert.Equal(1, (await JsonAsync(await u.GetAsync("api/orders?page=0"))).GetProperty("page").GetInt32());
        }

        [Fact]
        public async Task GetMine_EmptyForNewUser_And401WithoutToken()
        {
            var (u, _) = await RegisterAndLoginAsync(_factory);
            var page = await JsonAsync(await u.GetAsync("api/orders"));
            Assert.Equal(0, page.GetProperty("totalCount").GetInt32());
            Assert.Equal(0, page.GetProperty("items").GetArrayLength());
            Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("api/orders")).StatusCode);
        }

        // ---------- detail ----------
        [Fact]
        public async Task GetById_OwnerAndAdmin_Ok_OtherUser404()
        {
            var admin = await AdminAsync();
            var (a, name) = await RegisterAndLoginAsync(_factory);
            var (b, _) = await RegisterAndLoginAsync(_factory);
            var id = await PlaceOrderAsync(admin, a);

            var own = await a.GetAsync($"api/orders/{id}");
            Assert.Equal(HttpStatusCode.OK, own.StatusCode);
            var body = await JsonAsync(own);
            Assert.Equal(1, body.GetProperty("items").GetArrayLength());
            Assert.Equal(name, body.GetProperty("userName").GetString());

            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"api/orders/{id}")).StatusCode);

            var foreign = await b.GetAsync($"api/orders/{id}");
            var unknown = await b.GetAsync("api/orders/999999");
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
            Assert.Equal(unknown.StatusCode, foreign.StatusCode);
            Assert.Equal(unknown.Content.Headers.ContentLength, foreign.Content.Headers.ContentLength);
        }

        [Fact]
        public async Task GetById_UnknownNonNumericAndNoToken()
        {
            var (u, _) = await RegisterAndLoginAsync(_factory);
            Assert.Equal(HttpStatusCode.NotFound, (await u.GetAsync("api/orders/999999")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await u.GetAsync("api/orders/abc")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("api/orders/1")).StatusCode);
        }

        [Fact]
        public async Task DeletedProduct_OrderItemStillShowsSnapshot()
        {
            var admin = await AdminAsync();
            var (u, _) = await RegisterAndLoginAsync(_factory);
            var pid = await CreateProductAsync(admin, "Vanishing", 7, 5);
            await AddOkAsync(u, pid, 2);
            var id = await CheckoutOkAsync(u);
            (await admin.DeleteAsync($"api/product/{pid}")).EnsureSuccessStatusCode();

            var item = (await GetOrderAsync(u, id)).GetProperty("items")[0];
            Assert.Equal("Vanishing", item.GetProperty("productName").GetString());
            Assert.Equal(7m, item.GetProperty("unitPrice").GetDecimal());
        }

        // ---------- admin list ----------
        [Fact]
        public async Task GetAll_IncludesOtherUsersOrders_WithUserInfo()
        {
            var admin = await AdminAsync();
            var (u, name) = await RegisterAndLoginAsync(_factory);
            var id = await PlaceOrderAsync(admin, u);

            var page = await JsonAsync(await admin.GetAsync("api/orders/all?pageSize=50"));
            var row = page.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetInt32() == id);
            Assert.Equal(name, row.GetProperty("userName").GetString());
            Assert.Equal(await InventoryHelper.UserIdAsync(_factory, name), row.GetProperty("userId").GetInt32());
        }

        [Fact]
        public async Task GetAll_StatusFilter_CaseInsensitive_AndInvalid400()
        {
            var admin = await AdminAsync();
            var (u, _) = await RegisterAndLoginAsync(_factory);
            var pending = await PlaceOrderAsync(admin, u);
            var cancelled = await PlaceOrderAsync(admin, u);
            var confirmed = await PlaceOrderAsync(admin, u);
            (await CancelAsync(u, cancelled)).EnsureSuccessStatusCode();
            (await SetStatusAsync(admin, confirmed, "Confirmed")).EnsureSuccessStatusCode();

            async Task<List<int>> Ids(string status)
            {
                var page = await JsonAsync(await admin.GetAsync($"api/orders/all?status={status}&pageSize=50"));
                return page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt32()).ToList();
            }

            Assert.Contains(pending, await Ids("Pending"));
            Assert.DoesNotContain(cancelled, await Ids("pending"));
            Assert.Contains(cancelled, await Ids("Cancelled"));
            Assert.DoesNotContain(pending, await Ids("cancelled"));
            Assert.Contains(confirmed, await Ids("CONFIRMED"));

            foreach (var bad in new[] { "1", "abc" })
            {
                var r = await admin.GetAsync($"api/orders/all?status={bad}");
                Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
                Assert.Contains("status", await ErrorKeysAsync(r));
            }
        }

        [Fact]
        public async Task GetAll_PagingClamping_And403_401()
        {
            var admin = await AdminAsync();
            var (u, _) = await RegisterAndLoginAsync(_factory);
            Assert.Equal(1, (await JsonAsync(await admin.GetAsync("api/orders/all?pageSize=0"))).GetProperty("pageSize").GetInt32());
            Assert.Equal(50, (await JsonAsync(await admin.GetAsync("api/orders/all?pageSize=500"))).GetProperty("pageSize").GetInt32());
            Assert.Equal(1, (await JsonAsync(await admin.GetAsync("api/orders/all?page=0"))).GetProperty("page").GetInt32());
            Assert.Equal(HttpStatusCode.Forbidden, (await u.GetAsync("api/orders/all")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("api/orders/all")).StatusCode);
        }
    }
}
