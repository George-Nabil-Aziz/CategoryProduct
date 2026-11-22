using System.Net;
using CategoryProduct.API.Models;
using static CategoryProduct.API.Tests.CartOrderTestHelpers;
using static CategoryProduct.API.Tests.PaymentTestHelpers;

namespace CategoryProduct.API.Tests
{
    /// <summary>Deterministic races: a gateway hook (between claim and finalize) or the save interceptor plays the competing request.</summary>
    public class PaymentConcurrencyTests : IClassFixture<CartRacingFactory>
    {
        private readonly CartRacingFactory _factory;
        public PaymentConcurrencyTests(CartRacingFactory factory) => _factory = factory;

        private async Task<(HttpClient Buyer, int OrderId)> OrderAsync()
        {
            var admin = await LoggedInAdminAsync(_factory);
            var (buyer, _) = await RegisterAndLoginAsync(_factory);
            var (orderId, _, _) = await PlaceOrderAsync(_factory, admin, buyer);
            return (buyer, orderId);
        }

        private void Reset()
        {
            _factory.Interceptor.Disarm();
            _factory.Gateway.Reset();
        }

        [Fact]
        public async Task TwoPaysWithDifferentKeys_OnlyOneIsChargedAndSucceeds()
        {
            Reset();
            var (buyer, id) = await OrderAsync();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _factory.Gateway.BeforeCharge = async req =>
            {
                if (req.IdempotencyKey != "first-key") return;
                entered.SetResult();
                await release.Task;
            };

            // The first request is claimed and parked inside the charge; the second one runs completely meanwhile.
            var first = PayAsync(buyer, id, "tok_visa", "first-key");
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var second = await PayAsync(buyer, id, "tok_visa", "second-key");
            release.SetResult();
            var firstResponse = await first;

            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
            Assert.Equal("A payment for this order is already in progress or completed.",
                (await JsonAsync(second)).GetProperty("detail").GetString());
            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
            var payments = await PaymentsAsync(_factory, id);
            var only = Assert.Single(payments);
            Assert.Equal(PaymentState.Succeeded, only.Status);
            Assert.Equal(1, _factory.Gateway.ChargeCount);
            Assert.Equal("Paid", await PaymentStatusAsync(buyer, id));
        }

        [Fact]
        public async Task CompetingClaimLandingFirst_GivesConflict_AndNoCharge()
        {
            Reset();
            var (buyer, id) = await OrderAsync();
            _factory.Interceptor.RacingPayOrderId = id;
            _factory.Interceptor.Arm(1);

            var r = await PayAsync(buyer, id, "tok_visa", "loser-key");
            _factory.Interceptor.Disarm();

            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Equal(1, _factory.Interceptor.Hits);
            Assert.Equal(0, _factory.Gateway.ChargeCount);
            var payments = await PaymentsAsync(_factory, id);
            var active = Assert.Single(payments);
            Assert.Equal(PaymentState.Pending, active.Status);
            Assert.StartsWith("race-", active.IdempotencyKey);
            Assert.Equal("Unpaid", await PaymentStatusAsync(buyer, id));
        }

        [Fact]
        public async Task OrderCancelledBetweenClaimAndFinalize_RefundsTheCharge_AndReturns409()
        {
            Reset();
            var (buyer, id) = await OrderAsync();
            _factory.Gateway.BeforeCharge = _ => ForceOrderStatusAsync(_factory, id, OrderStatus.Cancelled);

            var r = await PayAsync(buyer, id, "tok_visa", "cancel-race");

            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Equal("The order was cancelled; the payment was refunded.",
                (await JsonAsync(r)).GetProperty("detail").GetString());
            Assert.Equal(1, _factory.Gateway.ChargeCount);
            Assert.Equal(1, _factory.Gateway.RefundCount);
            var payment = Assert.Single(await PaymentsAsync(_factory, id));
            Assert.Equal(PaymentState.Refunded, payment.Status);
            var order = await GetOrderAsync(buyer, id);
            Assert.Equal("Cancelled", order.GetProperty("status").GetString());
            Assert.Equal("Refunded", order.GetProperty("paymentStatus").GetString());
        }

        [Fact]
        public async Task AdminConfirmBetweenClaimAndFinalize_StillEndsPaid()
        {
            Reset();
            var (buyer, id) = await OrderAsync();
            _factory.Gateway.BeforeCharge = _ => ForceOrderStatusAsync(_factory, id, OrderStatus.Confirmed);

            var r = await PayAsync(buyer, id, "tok_visa", "confirm-race");

            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            var order = await GetOrderAsync(buyer, id);
            Assert.Equal("Confirmed", order.GetProperty("status").GetString());
            Assert.Equal("Paid", order.GetProperty("paymentStatus").GetString());
            Assert.Equal(0, _factory.Gateway.RefundCount);
            Assert.Equal(PaymentState.Succeeded, Assert.Single(await PaymentsAsync(_factory, id)).Status);
        }
    }
}
