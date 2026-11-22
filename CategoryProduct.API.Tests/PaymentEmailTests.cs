using System.Net;
using CategoryProduct.API.Services;
using static CategoryProduct.API.Tests.CartOrderTestHelpers;
using static CategoryProduct.API.Tests.PaymentTestHelpers;

namespace CategoryProduct.API.Tests
{
    public class PaymentEmailTests : IClassFixture<ApiFactory>, IClassFixture<PaymentEmailTests.EmailDisabledFactory>
    {
        public sealed class EmailDisabledFactory : ApiFactory
        {
            protected override bool EmailEnabled => false;
        }

        private readonly ApiFactory _factory;
        private readonly EmailDisabledFactory _disabled;

        public PaymentEmailTests(ApiFactory factory, EmailDisabledFactory disabled)
        {
            _factory = factory;
            _disabled = disabled;
        }

        private static async Task<IReadOnlyList<EmailMessage>> DrainAsync(ApiFactory f)
        {
            await f.EmailQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10));
            return f.Emails.Messages;
        }

        private static async Task ResetAsync(ApiFactory f)
        {
            await f.EmailQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10));
            f.Emails.Clear();
            f.Gateway.Reset();
        }

        private static async Task<(HttpClient Admin, HttpClient Buyer, string UserName, string? Email, int OrderId, decimal Total)>
            OrderAsync(ApiFactory f, bool withEmail)
        {
            var admin = await LoggedInAdminAsync(f);
            var (buyer, name) = await RegisterAndLoginAsync(f);
            string? email = null;
            if (withEmail)
            {
                email = $"pay{Guid.NewGuid():N}@example.com";
                await SetEmailAsync(buyer, email);
            }
            var (orderId, _, total) = await PlaceOrderAsync(f, admin, buyer, price: 12.5m, qty: 2);
            return (admin, buyer, name, email, orderId, total);
        }

        [Fact]
        public async Task Receipt_GoesToTheUser_WithDetails_AndNoSecrets()
        {
            var (_, buyer, name, email, id, _) = await OrderAsync(_factory, withEmail: true);
            await ResetAsync(_factory);
            var token = "tok_mail" + Guid.NewGuid().ToString("N")[..10];
            var key = "mail-key-" + Guid.NewGuid().ToString("N")[..10];

            var body = await PayOkAsync(buyer, id, token, key);

            var mail = Assert.Single(await DrainAsync(_factory));
            Assert.Equal(email, mail.To);
            Assert.Equal($"Payment received for order #{id}", mail.Subject);
            Assert.Contains($"Hello {name},", mail.Body);
            Assert.Contains($"order #{id}", mail.Body);
            Assert.Contains("Amount: 25.00 USD", mail.Body);
            Assert.Contains("Reference: " + body.GetProperty("providerReference").GetString(), mail.Body);
            Assert.Contains("Total: 25.00", mail.Body);
            Assert.Contains(" UTC", mail.Body);
            Assert.DoesNotContain(token, mail.Body + mail.Subject);
            Assert.DoesNotContain(key, mail.Body + mail.Subject);
        }

        [Fact]
        public async Task NoMail_ForAUserWithoutEmail()
        {
            var (_, buyer, _, _, id, _) = await OrderAsync(_factory, withEmail: false);
            await ResetAsync(_factory);

            await PayOkAsync(buyer, id);

            Assert.Empty(await DrainAsync(_factory));
        }

        [Fact]
        public async Task NoMail_WhenEmailIsDisabled_ButThePaymentSucceeds()
        {
            var (_, buyer, _, _, id, _) = await OrderAsync(_disabled, withEmail: true);
            await ResetAsync(_disabled);

            await PayOkAsync(buyer, id);

            Assert.Empty(await DrainAsync(_disabled));
        }

        [Fact]
        public async Task NoMail_OnDecline_AndNoneOnIdempotentReplay()
        {
            var (_, buyer, _, _, id, _) = await OrderAsync(_factory, withEmail: true);
            await ResetAsync(_factory);

            Assert.Equal((HttpStatusCode)402, (await PayAsync(buyer, id, "tok_decline", "d-" + Guid.NewGuid().ToString("N"))).StatusCode);
            Assert.Empty(await DrainAsync(_factory));

            var key = "ok-" + Guid.NewGuid().ToString("N");
            await PayOkAsync(buyer, id, "tok_visa", key);
            Assert.Single(await DrainAsync(_factory));
            await PayOkAsync(buyer, id, "tok_visa", key);
            Assert.Single(await DrainAsync(_factory));
        }

        [Fact]
        public async Task CancellingAPaidOrder_SendsTheRefundMail_AndTheUnchangedCancellationMail()
        {
            var (_, buyer, name, email, id, _) = await OrderAsync(_factory, withEmail: true);
            await PayOkAsync(buyer, id, "tok_visa");
            await ResetAsync(_factory);

            (await CancelAsync(buyer, id)).EnsureSuccessStatusCode();

            var mails = await DrainAsync(_factory);
            Assert.Equal(2, mails.Count);
            Assert.All(mails, m => Assert.Equal(email, m.To));
            var cancelled = Assert.Single(mails, m => m.Subject == $"Order #{id} cancelled");
            Assert.Contains("Reserved stock was released.", cancelled.Body);
            var refunded = Assert.Single(mails, m => m.Subject == $"Payment refunded for order #{id}");
            Assert.Contains($"Hello {name},", refunded.Body);
            Assert.Contains("Your payment of 25.00 USD", refunded.Body);
            Assert.Contains($"order #{id}", refunded.Body);
            Assert.Contains("Reference: fake_ch_", refunded.Body);
        }

        [Fact]
        public async Task CancellingAnUnpaidOrder_SendsOnlyTheCancellationMail()
        {
            var (_, buyer, _, _, id, _) = await OrderAsync(_factory, withEmail: true);
            await ResetAsync(_factory);

            (await CancelAsync(buyer, id)).EnsureSuccessStatusCode();

            var mail = Assert.Single(await DrainAsync(_factory));
            Assert.Equal($"Order #{id} cancelled", mail.Subject);
        }

        [Fact]
        public async Task AFailingSender_DoesNotFailThePayment()
        {
            var (_, buyer, _, _, id, _) = await OrderAsync(_factory, withEmail: true);
            await ResetAsync(_factory);
            _factory.Emails.ThrowOnSend = true;
            try
            {
                var r = await PayAsync(buyer, id);
                Assert.Equal(HttpStatusCode.OK, r.StatusCode);
                await DrainAsync(_factory);
            }
            finally
            {
                _factory.Emails.ThrowOnSend = false;
            }
            Assert.Equal("Paid", await PaymentStatusAsync(buyer, id));
        }
    }
}
