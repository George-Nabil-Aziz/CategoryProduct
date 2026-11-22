using System.Net;
using CategoryProduct.API.Models;
using static CategoryProduct.API.Tests.CartOrderTestHelpers;
using static CategoryProduct.API.Tests.PaymentTestHelpers;

namespace CategoryProduct.API.Tests
{
    public class PaymentIdempotencyTests : IClassFixture<ApiFactory>
    {
        private readonly ApiFactory _factory;
        public PaymentIdempotencyTests(ApiFactory factory) => _factory = factory;

        private static string Key() => "k-" + Guid.NewGuid().ToString("N");

        private async Task<(HttpClient Buyer, int OrderId)> OrderAsync(bool withEmail = false)
        {
            var admin = await LoggedInAdminAsync(_factory);
            var (buyer, _) = await RegisterAndLoginAsync(_factory);
            if (withEmail) await SetEmailAsync(buyer);
            var (orderId, _, _) = await PlaceOrderAsync(_factory, admin, buyer);
            return (buyer, orderId);
        }

        [Fact]
        public async Task SameKeyTwice_ReplaysTheSamePayment_ChargesOnce_SendsOneMail()
        {
            var (buyer, id) = await OrderAsync(withEmail: true);
            await _factory.EmailQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10));
            _factory.Emails.Clear();
            _factory.Gateway.Reset();
            var key = Key();

            var first = await PayOkAsync(buyer, id, "tok_visa", key);
            var second = await PayOkAsync(buyer, id, "tok_visa", key);

            Assert.Equal(first.GetProperty("id").GetInt32(), second.GetProperty("id").GetInt32());
            Assert.Equal(first.GetProperty("providerReference").GetString(), second.GetProperty("providerReference").GetString());
            Assert.Single(await PaymentsAsync(_factory, id));
            Assert.Equal(1, _factory.Gateway.ChargeCount);
            await _factory.EmailQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10));
            Assert.Single(_factory.Emails.Messages, m => m.Subject.StartsWith("Payment received"));
        }

        [Fact]
        public async Task DeclinedKey_ReplaysTheDecline_NewKeyStartsANewAttempt()
        {
            var (buyer, id) = await OrderAsync();
            var key = Key();

            Assert.Equal((HttpStatusCode)402, (await PayAsync(buyer, id, "tok_decline", key)).StatusCode);
            _factory.Gateway.Reset();
            var replay = await PayAsync(buyer, id, "tok_visa", key);

            Assert.Equal((HttpStatusCode)402, replay.StatusCode);
            Assert.Single(await PaymentsAsync(_factory, id));
            Assert.Equal(0, _factory.Gateway.ChargeCount);

            Assert.Equal((HttpStatusCode)402, (await PayAsync(buyer, id, "tok_decline", Key())).StatusCode);
            Assert.Equal(2, (await PaymentsAsync(_factory, id)).Count);
        }

        [Fact]
        public async Task NoKey_IsGenerated_AndNotReplayable()
        {
            var (buyer, id) = await OrderAsync();

            var first = await PayAsync(buyer, id, "tok_visa", null);
            var second = await PayAsync(buyer, id, "tok_visa", null);

            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
            Assert.Single(await PaymentsAsync(_factory, id));
        }

        [Fact]
        public async Task KeyUsedForAnotherOrder_Returns409_WithoutLeaking()
        {
            var (buyer, id) = await OrderAsync();
            var admin = await LoggedInAdminAsync(_factory);
            var (second, _, _) = await PlaceOrderAsync(_factory, admin, buyer);
            var key = Key();
            await PayOkAsync(buyer, id, "tok_visa", key);

            var r = await PayAsync(buyer, second, "tok_visa", key);

            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            var text = await r.Content.ReadAsStringAsync();
            Assert.Contains("Idempotency key already used.", text);
            Assert.DoesNotContain("fake_ch_", text);
            Assert.Empty(await PaymentsAsync(_factory, second));
        }

        [Fact]
        public async Task KeyUsedByAnotherUsersOrder_Returns409()
        {
            var (a, orderA) = await OrderAsync();
            var (b, orderB) = await OrderAsync();
            var key = Key();
            await PayOkAsync(a, orderA, "tok_visa", key);

            var r = await PayAsync(b, orderB, "tok_visa", key);

            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Contains("Idempotency key already used.", await r.Content.ReadAsStringAsync());
            Assert.Empty(await PaymentsAsync(_factory, orderB));
            Assert.Equal("Unpaid", await PaymentStatusAsync(b, orderB));
        }

        [Fact]
        public async Task PendingPayment_IsRedrivenBySameKey_AndCompletes()
        {
            var (buyer, id) = await OrderAsync();
            var key = Key();
            await SeedPaymentAsync(_factory, id, PaymentState.Pending, key);
            var seeded = Assert.Single(await PaymentsAsync(_factory, id));

            var body = await PayOkAsync(buyer, id, "tok_visa", key);

            Assert.Equal(seeded.Id, body.GetProperty("id").GetInt32());
            Assert.Equal("Succeeded", body.GetProperty("status").GetString());
            Assert.Equal("Paid", await PaymentStatusAsync(buyer, id));
            Assert.Single(await PaymentsAsync(_factory, id));
        }

        [Fact]
        public async Task PendingPayment_WithAnotherKey_Returns409InProgress()
        {
            var (buyer, id) = await OrderAsync();
            await SeedPaymentAsync(_factory, id, PaymentState.Pending, Key());

            var r = await PayAsync(buyer, id, "tok_visa", Key());

            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Equal("A payment for this order is already in progress or completed.",
                (await JsonAsync(r)).GetProperty("detail").GetString());
        }
    }
}
