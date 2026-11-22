using System.Net;
using CategoryProduct.API.Models;
using static CategoryProduct.API.Tests.CartOrderTestHelpers;
using static CategoryProduct.API.Tests.PaymentTestHelpers;

namespace CategoryProduct.API.Tests
{
    public class PaymentRefundTests : IClassFixture<CartRacingFactory>
    {
        private readonly CartRacingFactory _factory;
        public PaymentRefundTests(CartRacingFactory factory) => _factory = factory;

        private async Task<(HttpClient Admin, HttpClient Buyer, int OrderId, int ProductId, decimal Total)> PaidOrderAsync(
            string token = "tok_visa", bool pay = true)
        {
            _factory.Interceptor.Disarm();
            var admin = await LoggedInAdminAsync(_factory);
            var (buyer, _) = await RegisterAndLoginAsync(_factory);
            var (orderId, productId, total) = await PlaceOrderAsync(_factory, admin, buyer, price: 15m, qty: 2, stock: 10);
            if (pay) await PayOkAsync(buyer, orderId, token);
            _factory.Gateway.Reset();
            return (admin, buyer, orderId, productId, total);
        }

        private async Task AssertRefundedCancelAsync(HttpClient reader, HttpClient admin, int orderId, int productId, decimal total)
        {
            var order = await GetOrderAsync(reader, orderId);
            Assert.Equal("Cancelled", order.GetProperty("status").GetString());
            Assert.Equal("Refunded", order.GetProperty("paymentStatus").GetString());
            Assert.Equal(1, _factory.Gateway.RefundCount);
            Assert.Equal(total, _factory.Gateway.RefundRequests.Single().Amount);
            Assert.Equal(PaymentState.Refunded, Assert.Single(await PaymentsAsync(_factory, orderId)).Status);
            Assert.Equal(10, await GetProductQuantityAsync(admin, productId));
            Assert.Single(await MovementsOfReasonAsync(admin, productId, "OrderCancelled"));
        }

        [Fact]
        public async Task OwnerCancelsPaidOrder_Refunds_AndRestoresStockLikeAnUnpaidCancel()
        {
            var (admin, buyer, id, pid, total) = await PaidOrderAsync();

            var r = await CancelAsync(buyer, id);

            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            var body = await JsonAsync(r);
            Assert.Equal("Cancelled", body.GetProperty("status").GetString());
            Assert.Equal("Refunded", body.GetProperty("paymentStatus").GetString());
            await AssertRefundedCancelAsync(buyer, admin, id, pid, total);
        }

        [Fact]
        public async Task AdminPostCancel_OfAPaidOrder_Refunds()
        {
            var (admin, buyer, id, pid, total) = await PaidOrderAsync();

            Assert.Equal(HttpStatusCode.OK, (await CancelAsync(admin, id)).StatusCode);

            await AssertRefundedCancelAsync(buyer, admin, id, pid, total);
        }

        [Fact]
        public async Task AdminPutStatusCancelled_OfAPaidOrder_Refunds()
        {
            var (admin, buyer, id, pid, total) = await PaidOrderAsync();

            Assert.Equal(HttpStatusCode.OK, (await SetStatusAsync(admin, id, "Cancelled")).StatusCode);

            await AssertRefundedCancelAsync(buyer, admin, id, pid, total);
        }

        [Fact]
        public async Task CancellingAnUnpaidOrder_NeverCallsTheGateway()
        {
            var (_, buyer, id, _, _) = await PaidOrderAsync(pay: false);

            Assert.Equal(HttpStatusCode.OK, (await CancelAsync(buyer, id)).StatusCode);

            Assert.Equal(0, _factory.Gateway.RefundCount);
            Assert.Equal(0, _factory.Gateway.ChargeCount);
            Assert.Equal("Unpaid", await PaymentStatusAsync(buyer, id));
        }

        [Fact]
        public async Task CancellingAfterADeclinedAttempt_NeedsNoRefund()
        {
            var (_, buyer, id, _, _) = await PaidOrderAsync(pay: false);
            await PayAsync(buyer, id, "tok_decline", "decl-" + Guid.NewGuid().ToString("N"));
            _factory.Gateway.Reset();

            Assert.Equal(HttpStatusCode.OK, (await CancelAsync(buyer, id)).StatusCode);

            Assert.Equal(0, _factory.Gateway.RefundCount);
        }

        [Fact]
        public async Task RefundFailure_AbortsTheCancel_NothingChanges()
        {
            var (admin, buyer, id, pid, _) = await PaidOrderAsync("tok_refund_fail");

            var r = await CancelAsync(buyer, id);

            Assert.Equal(HttpStatusCode.BadGateway, r.StatusCode);
            Assert.Contains("Payment provider error", await r.Content.ReadAsStringAsync());
            var order = await GetOrderAsync(buyer, id);
            Assert.Equal("Pending", order.GetProperty("status").GetString());
            Assert.Equal("Paid", order.GetProperty("paymentStatus").GetString());
            Assert.Equal(8, await GetProductQuantityAsync(admin, pid));
            Assert.Empty(await MovementsOfReasonAsync(admin, pid, "OrderCancelled"));
            Assert.Equal(PaymentState.Succeeded, Assert.Single(await PaymentsAsync(_factory, id)).Status);
        }

        [Fact]
        public async Task PendingPayment_BlocksCancel_With409()
        {
            var (admin, buyer, id, pid, _) = await PaidOrderAsync(pay: false);
            await SeedPaymentAsync(_factory, id, PaymentState.Pending, "inflight-" + Guid.NewGuid().ToString("N"));

            var r = await CancelAsync(buyer, id);

            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Equal("A payment is in progress for this order.", (await JsonAsync(r)).GetProperty("detail").GetString());
            Assert.Equal("Pending", await OrderStatusAsync(buyer, id));
            Assert.Equal(8, await GetProductQuantityAsync(admin, pid));
            Assert.Equal(0, _factory.Gateway.RefundCount);
        }

        [Fact]
        public async Task RetriedCancelAfterACommitConflict_RefundsOnce_WithTheSameKey()
        {
            var (admin, buyer, id, pid, total) = await PaidOrderAsync();
            var paymentId = Assert.Single(await PaymentsAsync(_factory, id)).Id;
            // A competing stock write makes the first cancel save fail; the retry must not refund again.
            _factory.Interceptor.StockProductId = pid;
            _factory.Interceptor.StockDelta = 5;
            _factory.Interceptor.Arm(1);

            var r = await CancelAsync(buyer, id);
            _factory.Interceptor.Disarm();

            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.Equal(1, _factory.Interceptor.Hits);
            Assert.Equal(1, _factory.Gateway.RefundCount);
            Assert.Equal($"refund-{paymentId}", _factory.Gateway.RefundRequests.Single().IdempotencyKey);
            Assert.Single(_factory.Gateway.RefundReferences);
            Assert.Equal(total, _factory.Gateway.RefundRequests.Single().Amount);
            Assert.Equal("Refunded", await PaymentStatusAsync(buyer, id));
            Assert.Equal("Cancelled", await OrderStatusAsync(buyer, id));
            Assert.Equal(PaymentState.Refunded, Assert.Single(await PaymentsAsync(_factory, id)).Status);
        }

        [Fact]
        public async Task ConfirmedPaidOrder_CannotBeCancelled_AndStaysPaid()
        {
            var (admin, buyer, id, _, _) = await PaidOrderAsync();
            (await SetStatusAsync(admin, id, "Confirmed")).EnsureSuccessStatusCode();

            var r = await CancelAsync(buyer, id);

            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Equal("Paid", await PaymentStatusAsync(buyer, id));
            Assert.Equal(0, _factory.Gateway.RefundCount);
        }

        [Fact]
        public async Task SecondCancel_Returns409_WithoutGatewayCall()
        {
            var (_, buyer, id, _, _) = await PaidOrderAsync();
            (await CancelAsync(buyer, id)).EnsureSuccessStatusCode();
            _factory.Gateway.Reset();

            var r = await CancelAsync(buyer, id);

            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Equal(0, _factory.Gateway.RefundCount);
            Assert.Equal("Refunded", await PaymentStatusAsync(buyer, id));
        }
    }
}
