using System.Net;
using System.Net.Http.Json;
using static CategoryProduct.API.Tests.CartOrderTestHelpers;

namespace CategoryProduct.API.Tests
{
    public class OrderEmailTests : IClassFixture<ApiFactory>
    {
        private readonly ApiFactory _factory;
        public OrderEmailTests(ApiFactory factory) => _factory = factory;

        private static string UniqueEmail() => $"o{Guid.NewGuid():N}@example.com";

        private async Task<HttpClient> AdminAsync() => await AuthHelper.LoggedInAsync(_factory, ApiFactory.UserName);

        /// <summary>Waits until everything queued so far was handled, then resets the recorder.</summary>
        private async Task ResetAsync()
        {
            await _factory.EmailQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10));
            _factory.Emails.Clear();
        }

        /// <summary>The enqueue happens before the HTTP response, so idle means "all mails of the request were sent".</summary>
        private async Task<IReadOnlyList<CategoryProduct.API.Services.EmailMessage>> DrainAsync()
        {
            await _factory.EmailQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10));
            return _factory.Emails.Messages;
        }

        private async Task<(HttpClient Client, string UserName, string? Email)> BuyerAsync(bool withEmail)
        {
            var (client, name) = await RegisterAndLoginAsync(_factory);
            string? email = null;
            if (withEmail)
            {
                email = UniqueEmail();
                (await client.PutAsJsonAsync("api/account/email", new { email })).EnsureSuccessStatusCode();
            }
            return (client, name, email);
        }

        private static async Task<(int ProductId, string Name)> ProductAsync(HttpClient admin, decimal price = 10, int quantity = 10, string? name = null)
        {
            name ??= AuthHelper.Unique("item");
            return (await CreateProductAsync(admin, name, price, quantity), name);
        }

        private async Task<int> PlaceOrderAsync(HttpClient admin, HttpClient buyer, decimal price = 10, int qty = 2, string? productName = null)
        {
            var (pid, _) = await ProductAsync(admin, price, 50, productName);
            await AddOkAsync(buyer, pid, qty);
            return await CheckoutOkAsync(buyer);
        }

        // ---------- placed ----------
        [Fact]
        public async Task Checkout_WithEmail_QueuesOneMailWithOrderDetails()
        {
            await ResetAsync();
            var admin = await AdminAsync();
            var (buyer, userName, email) = await BuyerAsync(withEmail: true);
            var (pid, name) = await ProductAsync(admin, price: 12.5m, quantity: 20);
            var (pid2, name2) = await ProductAsync(admin, price: 3m, quantity: 20);
            await AddOkAsync(buyer, pid, 2);
            await AddOkAsync(buyer, pid2, 3);
            await ResetAsync();

            var orderId = await CheckoutOkAsync(buyer);

            var mail = Assert.Single(await DrainAsync());
            Assert.Equal(email, mail.To);
            Assert.Equal($"Order #{orderId} placed", mail.Subject);
            Assert.Contains($"Hello {userName},", mail.Body);
            Assert.Contains($"order #{orderId}", mail.Body);
            Assert.Contains("Status: Pending", mail.Body);
            Assert.Contains($"- {name} x2 @ 12.50 = 25.00", mail.Body);
            Assert.Contains($"- {name2} x3 @ 3.00 = 9.00", mail.Body);
            Assert.Contains("Total: 34.00", mail.Body);
            Assert.Contains(" UTC", mail.Body);
        }

        [Fact]
        public async Task Checkout_WithoutEmail_QueuesNothing_AndResponseIsUnchanged()
        {
            var admin = await AdminAsync();
            var (buyer, _, _) = await BuyerAsync(withEmail: false);
            var (pid, _) = await ProductAsync(admin);
            await AddOkAsync(buyer, pid, 1);
            await ResetAsync();

            var r = await CheckoutAsync(buyer);

            Assert.Equal(HttpStatusCode.Created, r.StatusCode);
            Assert.Empty(await DrainAsync());
        }

        [Fact]
        public async Task FailedCheckouts_QueueNothing()
        {
            var admin = await AdminAsync();
            var (buyer, _, _) = await BuyerAsync(withEmail: true);
            await ResetAsync();

            // empty cart
            Assert.Equal(HttpStatusCode.BadRequest, (await CheckoutAsync(buyer)).StatusCode);

            // insufficient stock
            var (pid, _) = await ProductAsync(admin, quantity: 1);
            await AddOkAsync(buyer, pid, 1);
            (await InventoryHelper.MoveAsync(admin, pid, -1, "Adjustment")).EnsureSuccessStatusCode();
            await ResetAsync();
            Assert.Equal(HttpStatusCode.Conflict, (await CheckoutAsync(buyer)).StatusCode);

            // anonymous
            Assert.Equal(HttpStatusCode.Unauthorized, (await CheckoutAsync(_factory.CreateClient())).StatusCode);

            Assert.Empty(await DrainAsync());
        }

        // ---------- confirm / cancel ----------
        [Fact]
        public async Task AdminConfirm_MailsTheOwnerOnly()
        {
            var admin = await AdminAsync();
            var adminEmail = UniqueEmail();
            (await admin.PutAsJsonAsync("api/account/email", new { email = adminEmail })).EnsureSuccessStatusCode();
            var (buyer, userName, email) = await BuyerAsync(withEmail: true);
            var orderId = await PlaceOrderAsync(admin, buyer);
            await ResetAsync();

            var r = await SetStatusAsync(admin, orderId, "Confirmed");

            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            var mail = Assert.Single(await DrainAsync());
            Assert.Equal(email, mail.To);
            Assert.NotEqual(adminEmail, mail.To);
            Assert.Equal($"Order #{orderId} confirmed", mail.Subject);
            Assert.Contains($"Hello {userName},", mail.Body);
            Assert.Contains("is now Confirmed", mail.Body);
            Assert.Contains("Total: 20.00", mail.Body);
            Assert.DoesNotContain("Reserved stock was released.", mail.Body);
        }

        [Fact]
        public async Task OwnerCancel_MailsTheOwner()
        {
            var admin = await AdminAsync();
            var (buyer, _, email) = await BuyerAsync(withEmail: true);
            var orderId = await PlaceOrderAsync(admin, buyer);
            await ResetAsync();

            Assert.Equal(HttpStatusCode.OK, (await CancelAsync(buyer, orderId)).StatusCode);

            var mail = Assert.Single(await DrainAsync());
            Assert.Equal(email, mail.To);
            Assert.Equal($"Order #{orderId} cancelled", mail.Subject);
            Assert.Contains("is now Cancelled", mail.Body);
            Assert.Contains("Reserved stock was released.", mail.Body);
            Assert.Contains("Items:", mail.Body);
        }

        [Fact]
        public async Task AdminCancel_ThroughCancelEndpoint_MailsTheOwner()
        {
            var admin = await AdminAsync();
            var (buyer, _, email) = await BuyerAsync(withEmail: true);
            var orderId = await PlaceOrderAsync(admin, buyer);
            await ResetAsync();

            Assert.Equal(HttpStatusCode.OK, (await CancelAsync(admin, orderId)).StatusCode);

            var mail = Assert.Single(await DrainAsync());
            Assert.Equal(email, mail.To);
            Assert.Equal($"Order #{orderId} cancelled", mail.Subject);
        }

        [Fact]
        public async Task AdminCancel_ThroughStatusEndpoint_MailsTheOwner()
        {
            var admin = await AdminAsync();
            var (buyer, _, email) = await BuyerAsync(withEmail: true);
            var orderId = await PlaceOrderAsync(admin, buyer);
            await ResetAsync();

            Assert.Equal(HttpStatusCode.OK, (await SetStatusAsync(admin, orderId, "Cancelled")).StatusCode);

            var mail = Assert.Single(await DrainAsync());
            Assert.Equal(email, mail.To);
            Assert.Equal($"Order #{orderId} cancelled", mail.Subject);
        }

        [Fact]
        public async Task RejectedTransitions_QueueNothing()
        {
            var admin = await AdminAsync();
            var (buyer, _, _) = await BuyerAsync(withEmail: true);
            var confirmed = await PlaceOrderAsync(admin, buyer);
            Assert.Equal(HttpStatusCode.OK, (await SetStatusAsync(admin, confirmed, "Confirmed")).StatusCode);
            var cancelled = await PlaceOrderAsync(admin, buyer);
            Assert.Equal(HttpStatusCode.OK, (await CancelAsync(buyer, cancelled)).StatusCode);
            var pending = await PlaceOrderAsync(admin, buyer);
            var (other, _, _) = await BuyerAsync(withEmail: true);
            await ResetAsync();

            Assert.Equal(HttpStatusCode.Conflict, (await SetStatusAsync(admin, confirmed, "Confirmed")).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await SetStatusAsync(admin, confirmed, "Cancelled")).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await SetStatusAsync(admin, cancelled, "Confirmed")).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await CancelAsync(buyer, cancelled)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await SetStatusAsync(admin, pending, "Pending")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await SetStatusAsync(admin, pending, "Shipped")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await SetStatusAsync(admin, 999_999, "Confirmed")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await CancelAsync(buyer, 999_999)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await CancelAsync(other, pending)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await SetStatusAsync(buyer, pending, "Confirmed")).StatusCode);

            Assert.Empty(await DrainAsync());
        }

        [Fact]
        public async Task OwnerWithoutEmail_GetsNoConfirmOrCancelMail()
        {
            var admin = await AdminAsync();
            var (buyer, _, _) = await BuyerAsync(withEmail: false);
            var first = await PlaceOrderAsync(admin, buyer);
            var second = await PlaceOrderAsync(admin, buyer);
            await ResetAsync();

            Assert.Equal(HttpStatusCode.OK, (await SetStatusAsync(admin, first, "Confirmed")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await CancelAsync(buyer, second)).StatusCode);

            Assert.Empty(await DrainAsync());
        }

        [Fact]
        public async Task EmailChangedAfterCheckout_ConfirmationGoesToTheNewAddress()
        {
            var admin = await AdminAsync();
            var (buyer, _, oldEmail) = await BuyerAsync(withEmail: true);
            var orderId = await PlaceOrderAsync(admin, buyer);
            var newEmail = UniqueEmail();
            (await buyer.PutAsJsonAsync("api/account/email", new { email = newEmail })).EnsureSuccessStatusCode();
            await ResetAsync();

            Assert.Equal(HttpStatusCode.OK, (await SetStatusAsync(admin, orderId, "Confirmed")).StatusCode);

            var mail = Assert.Single(await DrainAsync());
            Assert.Equal(newEmail, mail.To);
            Assert.NotEqual(oldEmail, mail.To);
        }

        // ---------- safety ----------
        [Fact]
        public async Task ProductNameWithMarkupAndNewline_StaysInBody_NotInSubject()
        {
            var admin = await AdminAsync();
            var (buyer, _, _) = await BuyerAsync(withEmail: true);
            var name = "<b>Bold</b>\r\nBcc: evil@example.com";
            var (pid, _) = await ProductAsync(admin, name: name);
            await AddOkAsync(buyer, pid, 1);
            await ResetAsync();

            var orderId = await CheckoutOkAsync(buyer);

            var mail = Assert.Single(await DrainAsync());
            Assert.Equal($"Order #{orderId} placed", mail.Subject);
            Assert.DoesNotContain("evil", mail.Subject);
            Assert.DoesNotContain('\n', mail.Subject);
            Assert.Contains("<b>Bold</b>", mail.Body);
            Assert.Contains("Bcc: evil@example.com", mail.Body);
            Assert.Equal(1, mail.Body.Split('\n').Count(l => l.StartsWith("Bcc: evil", StringComparison.Ordinal)));
        }
    }

    public class ThrowingEmailFactory : ApiFactory
    {
        public ThrowingEmailFactory() => Emails.ThrowOnSend = true;
    }

    public class OrderEmailFailureTests : IClassFixture<ThrowingEmailFactory>
    {
        private readonly ThrowingEmailFactory _factory;
        public OrderEmailFailureTests(ThrowingEmailFactory factory) => _factory = factory;

        [Fact]
        public async Task ThrowingSender_DoesNotChangeOrderResponses()
        {
            var admin = await AuthHelper.LoggedInAsync(_factory, ApiFactory.UserName);
            var (buyer, _) = await RegisterAndLoginAsync(_factory);
            var email = $"f{Guid.NewGuid():N}@example.com";
            (await buyer.PutAsJsonAsync("api/account/email", new { email })).EnsureSuccessStatusCode();
            var pid = await CreateProductAsync(admin, quantity: 20);
            await AddOkAsync(buyer, pid, 1);

            var orderId = await CheckoutOkAsync(buyer);
            await _factory.EmailQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(1, _factory.Emails.Attempts);

            Assert.Equal(HttpStatusCode.OK, (await SetStatusAsync(admin, orderId, "Confirmed")).StatusCode);
            await AddOkAsync(buyer, pid, 1);
            var second = await CheckoutOkAsync(buyer);
            Assert.Equal(HttpStatusCode.OK, (await CancelAsync(buyer, second)).StatusCode);

            await _factory.EmailQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(4, _factory.Emails.Attempts);
            Assert.Empty(_factory.Emails.Messages);
            Assert.Equal("Confirmed", await OrderStatusAsync(buyer, orderId));
            Assert.Equal("Cancelled", await OrderStatusAsync(buyer, second));
        }
    }
}
