using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CategoryProduct.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static CategoryProduct.API.Tests.CartOrderTestHelpers;
using static CategoryProduct.API.Tests.PaymentTestHelpers;

namespace CategoryProduct.API.Tests
{
    public class PaymentTests : IClassFixture<LogCapturingApiFactory>
    {
        private readonly LogCapturingApiFactory _factory;
        public PaymentTests(LogCapturingApiFactory factory) => _factory = factory;

        private Task<HttpClient> AdminAsync() => LoggedInAdminAsync(_factory);

        private async Task<(HttpClient Buyer, int OrderId, decimal Total)> OrderAsync()
        {
            var admin = await AdminAsync();
            var (buyer, _) = await RegisterAndLoginAsync(_factory);
            var (orderId, _, total) = await PlaceOrderAsync(_factory, admin, buyer);
            return (buyer, orderId, total);
        }

        // ---------- success ----------
        [Fact]
        public async Task Pay_Success_ReturnsSucceededPayment_AndMarksOrderPaid()
        {
            var (buyer, id, total) = await OrderAsync();

            var r = await PayAsync(buyer, id, "tok_visa");

            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            var body = await JsonAsync(r);
            Assert.Equal("Succeeded", body.GetProperty("status").GetString());
            Assert.Equal(id, body.GetProperty("orderId").GetInt32());
            Assert.Equal(total, body.GetProperty("amount").GetDecimal());
            Assert.Equal("USD", body.GetProperty("currency").GetString());
            Assert.StartsWith("fake_ch_", body.GetProperty("providerReference").GetString());
            Assert.Equal(JsonValueKind.Null, body.GetProperty("failureReason").ValueKind);
            Assert.False(body.TryGetProperty("idempotencyKey", out _));
            Assert.False(body.TryGetProperty("paymentToken", out _));

            var order = await GetOrderAsync(buyer, id);
            Assert.Equal("Paid", order.GetProperty("paymentStatus").GetString());
            Assert.Equal("Pending", order.GetProperty("status").GetString());
        }

        [Fact]
        public async Task Pay_IgnoresAmountAndCurrencyInTheBody()
        {
            var (buyer, id, total) = await OrderAsync();

            var r = await buyer.PostAsJsonAsync($"api/orders/{id}/pay",
                new { paymentToken = "tok_visa", amount = 0.01m, currency = "EUR", total = 1 });

            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            var body = await JsonAsync(r);
            Assert.Equal(total, body.GetProperty("amount").GetDecimal());
            Assert.Equal("USD", body.GetProperty("currency").GetString());
        }

        [Theory]
        [InlineData("tok_mastercard")]
        [InlineData("tok_some_other_token")]
        public async Task Pay_AnyOtherWellFormedToken_Approves(string token)
        {
            var (buyer, id, _) = await OrderAsync();
            Assert.Equal(HttpStatusCode.OK, (await PayAsync(buyer, id, token)).StatusCode);
        }

        [Fact]
        public async Task PriceChangeAfterCheckout_DoesNotChangeTheCharge()
        {
            var admin = await AdminAsync();
            var (buyer, _) = await RegisterAndLoginAsync(_factory);
            var (id, productId, total) = await PlaceOrderAsync(_factory, admin, buyer, price: 15m, qty: 2);
            await InDbAsync(_factory, async db =>
            {
                var p = await db.Products.SingleAsync(x => x.Id == productId);
                p.Price = 999m;
                p.ConcurrencyStamp = Guid.NewGuid();
                await db.SaveChangesAsync();
                return 0;
            });

            var body = await PayOkAsync(buyer, id);

            Assert.Equal(total, body.GetProperty("amount").GetDecimal());
        }

        // ---------- declines ----------
        [Theory]
        [InlineData("tok_decline", "card_declined")]
        [InlineData("tok_insufficient_funds", "insufficient_funds")]
        public async Task Pay_Declined_Returns402_RecordsFailedPayment_AndAllowsRetry(string token, string reason)
        {
            var (buyer, id, _) = await OrderAsync();

            var r = await PayAsync(buyer, id, token);

            Assert.Equal((HttpStatusCode)402, r.StatusCode);
            var body = await JsonAsync(r);
            Assert.Equal("Payment declined", body.GetProperty("title").GetString());
            Assert.Equal($"Payment declined: {reason}.", body.GetProperty("detail").GetString());
            Assert.Equal(reason, body.GetProperty("reason").GetString());
            var payments = await PaymentsAsync(_factory, id);
            var failed = Assert.Single(payments);
            Assert.Equal(PaymentState.Failed, failed.Status);
            Assert.Equal(reason, failed.FailureReason);
            Assert.Equal(failed.Id, body.GetProperty("paymentId").GetInt32());
            Assert.Equal("Unpaid", await PaymentStatusAsync(buyer, id));
            Assert.Equal("Pending", await OrderStatusAsync(buyer, id));

            var retry = await PayAsync(buyer, id, "tok_visa", "new-key-" + Guid.NewGuid().ToString("N"));
            Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
            Assert.Equal("Paid", await PaymentStatusAsync(buyer, id));
        }

        // ---------- validation ----------
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("visa")]
        [InlineData("tok_")]
        [InlineData("4242424242424242")]
        [InlineData("4242 4242 4242 4242")]
        public async Task Pay_InvalidToken_Returns400_NoPaymentRow(string? token)
        {
            var (buyer, id, _) = await OrderAsync();

            var r = await PayAsync(buyer, id, token);

            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.Contains("PaymentToken", await ErrorKeysAsync(r));
            Assert.Empty(await PaymentsAsync(_factory, id));
        }

        [Fact]
        public async Task Pay_CardNumber_GetsTheSpecificMessage_AndIsNeverEchoedOrLogged()
        {
            var (buyer, id, _) = await OrderAsync();

            var r = await PayAsync(buyer, id, "4242424242424242");

            var text = await r.Content.ReadAsStringAsync();
            Assert.Contains("Card numbers are not accepted", text);
            Assert.DoesNotContain("4242424242424242", text);
            Assert.DoesNotContain(_factory.Logs.Messages, m => m.Contains("4242424242424242"));
        }

        [Theory]
        [InlineData("has space")]
        [InlineData("semi;colon")]
        public async Task Pay_BadIdempotencyKey_Returns400(string key)
        {
            var (buyer, id, _) = await OrderAsync();

            var r = await PayAsync(buyer, id, "tok_visa", key);

            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.Contains("IdempotencyKey", await ErrorKeysAsync(r));
            Assert.Empty(await PaymentsAsync(_factory, id));
        }

        [Fact]
        public async Task Pay_TooLongIdempotencyKey_Returns400()
        {
            var (buyer, id, _) = await OrderAsync();
            var r = await PayAsync(buyer, id, "tok_visa", new string('k', 65));
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.Contains("IdempotencyKey", await ErrorKeysAsync(r));
        }

        // ---------- state and ownership ----------
        [Fact]
        public async Task Pay_ConfirmedOrder_Returns409()
        {
            var (buyer, id, _) = await OrderAsync();
            var admin = await AdminAsync();
            (await SetStatusAsync(admin, id, "Confirmed")).EnsureSuccessStatusCode();

            var r = await PayAsync(buyer, id);

            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Equal("Only a Pending order can be paid.", (await JsonAsync(r)).GetProperty("detail").GetString());
            Assert.Empty(await PaymentsAsync(_factory, id));
        }

        [Fact]
        public async Task Pay_CancelledOrder_Returns409()
        {
            var (buyer, id, _) = await OrderAsync();
            (await CancelAsync(buyer, id)).EnsureSuccessStatusCode();

            var r = await PayAsync(buyer, id);

            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Equal("Only a Pending order can be paid.", (await JsonAsync(r)).GetProperty("detail").GetString());
        }

        [Fact]
        public async Task Pay_AlreadyPaid_Returns409_NoSecondCharge()
        {
            var (buyer, id, _) = await OrderAsync();
            await PayOkAsync(buyer, id, key: "first-" + Guid.NewGuid().ToString("N"));
            _factory.Gateway.Reset();

            var r = await PayAsync(buyer, id, "tok_visa", "second-" + Guid.NewGuid().ToString("N"));

            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Equal("The order is already paid.", (await JsonAsync(r)).GetProperty("detail").GetString());
            Assert.Equal(0, _factory.Gateway.ChargeCount);
        }

        [Fact]
        public async Task Pay_OtherUser_Returns404()
        {
            var (_, id, _) = await OrderAsync();
            var (other, _) = await RegisterAndLoginAsync(_factory);
            Assert.Equal(HttpStatusCode.NotFound, (await PayAsync(other, id)).StatusCode);
            Assert.Empty(await PaymentsAsync(_factory, id));
        }

        [Fact]
        public async Task Pay_AdminNonOwner_Returns404()
        {
            var (_, id, _) = await OrderAsync();
            var admin = await AdminAsync();
            Assert.Equal(HttpStatusCode.NotFound, (await PayAsync(admin, id)).StatusCode);
        }

        [Fact]
        public async Task Pay_UnknownOrder_Returns404()
        {
            var (buyer, _) = await RegisterAndLoginAsync(_factory);
            Assert.Equal(HttpStatusCode.NotFound, (await PayAsync(buyer, 987654)).StatusCode);
        }

        [Fact]
        public async Task Pay_Anonymous_Returns401()
        {
            var r = await PayAsync(_factory.CreateClient(), 1);
            Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        }

        // ---------- provider error ----------
        [Fact]
        public async Task Pay_ProviderError_Returns502_WithoutExceptionText_AndRecordsFailure()
        {
            var (buyer, id, _) = await OrderAsync();

            var r = await PayAsync(buyer, id, "tok_error");

            Assert.Equal(HttpStatusCode.BadGateway, r.StatusCode);
            var text = await r.Content.ReadAsStringAsync();
            Assert.Contains("Payment provider error", text);
            Assert.DoesNotContain("Simulated", text);
            Assert.DoesNotContain("InvalidOperationException", text);
            var failed = Assert.Single(await PaymentsAsync(_factory, id));
            Assert.Equal(PaymentState.Failed, failed.Status);
            Assert.Equal("provider_error", failed.FailureReason);
            Assert.Equal("Unpaid", await PaymentStatusAsync(buyer, id));
        }

        // ---------- confirm interplay, storage, logs ----------
        [Fact]
        public async Task AdminConfirm_OfAnUnpaidOrder_LeavesItUnpaid()
        {
            var (buyer, id, _) = await OrderAsync();
            var admin = await AdminAsync();

            (await SetStatusAsync(admin, id, "Confirmed")).EnsureSuccessStatusCode();

            Assert.Equal("Unpaid", await PaymentStatusAsync(buyer, id));
            Assert.Equal("Confirmed", await OrderStatusAsync(buyer, id));
        }

        [Fact]
        public void PaymentEntity_HasNoTokenOrCardColumn()
        {
            using var scope = _factory.Services.CreateScope();
            var db = (CategoryProduct.API.Data.ApplicationDbContext)scope.ServiceProvider
                .GetService(typeof(CategoryProduct.API.Data.ApplicationDbContext))!;
            var names = db.Model.FindEntityType(typeof(Payment))!.GetProperties().Select(p => p.Name).ToList();
            Assert.DoesNotContain(names, n => n.Contains("Token", StringComparison.OrdinalIgnoreCase)
                || n.Contains("Card", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task Pay_NeverLogsTheTokenOrTheKey()
        {
            var (buyer, id, _) = await OrderAsync();
            var token = "tok_unique" + Guid.NewGuid().ToString("N")[..12];
            var key = "key-secret-" + Guid.NewGuid().ToString("N")[..12];
            var (buyer2, id2, _) = await OrderAsync();
            var errToken = "tok_error";
            var errKey = "key-error-" + Guid.NewGuid().ToString("N")[..12];

            await PayOkAsync(buyer, id, token, key);
            Assert.Equal(HttpStatusCode.BadGateway, (await PayAsync(buyer2, id2, errToken, errKey)).StatusCode);

            Assert.DoesNotContain(_factory.Logs.Messages, m => m.Contains(token) || m.Contains(key) || m.Contains(errKey));
            Assert.Contains(_factory.Logs.Messages, m => m.Contains($"Payment") && m.Contains($"order {id}"));
        }
    }
}
