using System.Net;
using System.Text.Json;
using static CategoryProduct.API.Tests.CartOrderTestHelpers;
using static CategoryProduct.API.Tests.PaymentTestHelpers;

namespace CategoryProduct.API.Tests
{
    public class PaymentReadTests : IClassFixture<ApiFactory>
    {
        private readonly ApiFactory _factory;
        public PaymentReadTests(ApiFactory factory) => _factory = factory;

        private async Task<(HttpClient Admin, HttpClient Buyer, int OrderId)> OrderAsync()
        {
            var admin = await LoggedInAdminAsync(_factory);
            var (buyer, _) = await RegisterAndLoginAsync(_factory);
            var (orderId, _, _) = await PlaceOrderAsync(_factory, admin, buyer);
            return (admin, buyer, orderId);
        }

        private static IEnumerable<int> Ids(JsonElement page) =>
            page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt32());

        [Fact]
        public async Task PaymentStatus_IsPresentInAllThreeOrderReads_ForUnpaidPaidAndRefunded()
        {
            var (admin, buyer, unpaid) = await OrderAsync();
            var (_, _, paidId) = (admin, buyer, (await PlaceOrderAsync(_factory, admin, buyer)).OrderId);
            await PayOkAsync(buyer, paidId);
            var (_, _, refundedId) = (admin, buyer, (await PlaceOrderAsync(_factory, admin, buyer)).OrderId);
            await PayOkAsync(buyer, refundedId);
            (await CancelAsync(buyer, refundedId)).EnsureSuccessStatusCode();

            var mine = await JsonAsync(await buyer.GetAsync("api/orders?pageSize=50"));
            var all = await JsonAsync(await admin.GetAsync("api/orders/all?pageSize=50"));

            foreach (var (id, expected) in new[] { (unpaid, "Unpaid"), (paidId, "Paid"), (refundedId, "Refunded") })
            {
                Assert.Equal(expected, (await GetOrderAsync(buyer, id)).GetProperty("paymentStatus").GetString());
                foreach (var page in new[] { mine, all })
                {
                    var row = page.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetInt32() == id);
                    Assert.Equal(expected, row.GetProperty("paymentStatus").GetString());
                    Assert.True(row.TryGetProperty("status", out _));
                    Assert.True(row.TryGetProperty("itemCount", out _));
                    Assert.True(row.TryGetProperty("total", out _));
                }
            }
        }

        [Fact]
        public async Task ExistingOrderFields_KeepTheirShape()
        {
            var (_, buyer, id) = await OrderAsync();
            var order = await GetOrderAsync(buyer, id);
            foreach (var name in new[] { "id", "userId", "userName", "status", "total", "createdAtUtc", "updatedAtUtc", "items" })
                Assert.True(order.TryGetProperty(name, out _), name);
            Assert.Equal("Pending", order.GetProperty("status").GetString());
        }

        [Fact]
        public async Task AdminFilter_ByPaymentStatus_AndCombinedWithStatus()
        {
            var (admin, buyer, unpaid) = await OrderAsync();
            var paid = (await PlaceOrderAsync(_factory, admin, buyer)).OrderId;
            await PayOkAsync(buyer, paid);
            var paidConfirmed = (await PlaceOrderAsync(_factory, admin, buyer)).OrderId;
            await PayOkAsync(buyer, paidConfirmed);
            (await SetStatusAsync(admin, paidConfirmed, "Confirmed")).EnsureSuccessStatusCode();

            var onlyPaid = await JsonAsync(await admin.GetAsync("api/orders/all?paymentStatus=Paid&pageSize=50"));
            var paidPending = await JsonAsync(await admin.GetAsync("api/orders/all?paymentStatus=paid&status=Pending&pageSize=50"));
            var everything = await JsonAsync(await admin.GetAsync("api/orders/all?pageSize=50"));

            Assert.All(onlyPaid.GetProperty("items").EnumerateArray(),
                i => Assert.Equal("Paid", i.GetProperty("paymentStatus").GetString()));
            Assert.Contains(paid, Ids(onlyPaid));
            Assert.Contains(paidConfirmed, Ids(onlyPaid));
            Assert.DoesNotContain(unpaid, Ids(onlyPaid));
            Assert.Contains(paid, Ids(paidPending));
            Assert.DoesNotContain(paidConfirmed, Ids(paidPending));
            Assert.Contains(unpaid, Ids(everything));
            Assert.Contains(paid, Ids(everything));
        }

        [Fact]
        public async Task AdminFilter_UnknownPaymentStatus_Returns400()
        {
            var admin = await LoggedInAdminAsync(_factory);
            var r = await admin.GetAsync("api/orders/all?paymentStatus=Bogus");
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            // Like "status" today, the key is mapped to the action parameter name (paymentStatus).
            Assert.Contains("paymentstatus", (await ErrorKeysAsync(r)).Select(k => k.ToLowerInvariant()));
        }

        [Fact]
        public async Task Payments_OwnerAndAdminSeeThemNewestFirst_AndPaged()
        {
            var (admin, buyer, id) = await OrderAsync();
            await PayAsync(buyer, id, "tok_decline", "read-1-" + Guid.NewGuid().ToString("N"));
            await PayAsync(buyer, id, "tok_insufficient_funds", "read-2-" + Guid.NewGuid().ToString("N"));
            var key3 = "read-3-" + Guid.NewGuid().ToString("N");
            await PayOkAsync(buyer, id, "tok_visa", key3);

            var ownerR = await buyer.GetAsync($"api/orders/{id}/payments");
            var adminR = await admin.GetAsync($"api/orders/{id}/payments");

            Assert.Equal(HttpStatusCode.OK, ownerR.StatusCode);
            Assert.Equal(HttpStatusCode.OK, adminR.StatusCode);
            var page = await JsonAsync(ownerR);
            var items = page.GetProperty("items").EnumerateArray().ToList();
            Assert.Equal(3, items.Count);
            Assert.Equal(new[] { "Succeeded", "Failed", "Failed" }, items.Select(i => i.GetProperty("status").GetString()));
            Assert.Equal(3, page.GetProperty("totalCount").GetInt32());
            var text = page.GetRawText();
            Assert.DoesNotContain(key3, text);
            Assert.DoesNotContain("idempotencyKey", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("token", text, StringComparison.OrdinalIgnoreCase);

            var small = await JsonAsync(await buyer.GetAsync($"api/orders/{id}/payments?page=2&pageSize=1"));
            Assert.Single(small.GetProperty("items").EnumerateArray());
            Assert.Equal(3, small.GetProperty("totalPages").GetInt32());
            Assert.Equal("Failed", small.GetProperty("items")[0].GetProperty("status").GetString());
            var clamped = await JsonAsync(await buyer.GetAsync($"api/orders/{id}/payments?pageSize=500"));
            Assert.Equal(50, clamped.GetProperty("pageSize").GetInt32());
        }

        [Fact]
        public async Task Payments_NonOwnerGets404_UnknownOrder404_AnonymousGets401()
        {
            var (_, _, id) = await OrderAsync();
            var (other, _) = await RegisterAndLoginAsync(_factory);

            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"api/orders/{id}/payments")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync("api/orders/987654/payments")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await _factory.CreateClient().GetAsync($"api/orders/{id}/payments")).StatusCode);
        }
    }
}
