using System.Net;
using System.Net.Http.Json;
using CategoryProduct.API.Data;
using CategoryProduct.API.Models;
using CategoryProduct.API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static CategoryProduct.API.Tests.CartOrderTestHelpers;

namespace CategoryProduct.API.Tests
{
    public class LowStockEmailTests : IClassFixture<ApiFactory>
    {
        private readonly ApiFactory _factory;
        public LowStockEmailTests(ApiFactory factory) => _factory = factory;

        private async Task<HttpClient> AdminAsync() => await AuthHelper.LoggedInAsync(_factory, ApiFactory.UserName);

        /// <summary>Gives exactly the requested Admins an email (everybody else: none) so counts are exact.</summary>
        private async Task<List<string>> SetUpAdminsAsync(int withEmail, int withoutEmail = 1)
        {
            await _factory.EmailQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10));
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            foreach (var u in await db.Users.Where(u => u.Email != null).ToListAsync())
                u.Email = null;
            var addresses = new List<string>();
            for (var i = 0; i < withEmail; i++)
            {
                var address = $"admin{Guid.NewGuid():N}@example.com";
                addresses.Add(address);
                db.Users.Add(new User { UserName = AuthHelper.Unique("adm"), PasswordHash = "x", Role = "Admin", Email = address });
            }
            for (var i = 0; i < withoutEmail; i++)
                db.Users.Add(new User { UserName = AuthHelper.Unique("admn"), PasswordHash = "x", Role = "Admin" });
            await db.SaveChangesAsync();
            _factory.Emails.Clear();
            return addresses;
        }

        private async Task<IReadOnlyList<EmailMessage>> DrainAsync()
        {
            await _factory.EmailQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10));
            return _factory.Emails.Messages;
        }

        private static Task<HttpResponseMessage> MoveAsync(HttpClient admin, int productId, int change) =>
            InventoryHelper.MoveAsync(admin, productId, change, change > 0 ? "Restock" : "Adjustment");

        // ---------- scenarios ----------
        [Fact]
        public async Task Crossing_SendsOneMailPerAdminWithEmail_Separately()
        {
            var admins = await SetUpAdminsAsync(withEmail: 2);
            var admin = await AdminAsync();
            var name = AuthHelper.Unique("crossing");
            var pid = await CreateProductAsync(admin, name, 5, quantity: 6);

            Assert.Equal(HttpStatusCode.Created, (await MoveAsync(admin, pid, -2)).StatusCode);

            var mails = await DrainAsync();
            Assert.Equal(2, mails.Count);
            Assert.Equal(admins.OrderBy(a => a), mails.Select(m => m.To).OrderBy(a => a));
            Assert.All(mails, m =>
            {
                Assert.Equal($"Low stock: {name}", m.Subject);
                Assert.Contains($"#{pid} {name}: 4 left (was 6)", m.Body);
                Assert.Contains("threshold (5)", m.Body);
                Assert.DoesNotContain("@", m.Body);
            });
        }

        [Fact]
        public async Task AlreadyBelow_SendsNothing()
        {
            await SetUpAdminsAsync(withEmail: 1);
            var admin = await AdminAsync();
            var pid = await CreateProductAsync(admin, quantity: 4);

            Assert.Equal(HttpStatusCode.Created, (await MoveAsync(admin, pid, -1)).StatusCode);

            Assert.Empty(await DrainAsync());
        }

        [Fact]
        public async Task ExactlyAtThreshold_SendsNothing()
        {
            await SetUpAdminsAsync(withEmail: 1);
            var admin = await AdminAsync();
            var pid = await CreateProductAsync(admin, quantity: 7);

            Assert.Equal(HttpStatusCode.Created, (await MoveAsync(admin, pid, -2)).StatusCode);
            Assert.Equal(5, await InventoryHelper.QuantityAsync(admin, pid));

            Assert.Empty(await DrainAsync());
        }

        [Fact]
        public async Task IncreaseOrDecreaseStayingAboveThreshold_SendsNothing()
        {
            await SetUpAdminsAsync(withEmail: 1);
            var admin = await AdminAsync();
            var pid = await CreateProductAsync(admin, quantity: 4);
            var high = await CreateProductAsync(admin, quantity: 20);

            Assert.Equal(HttpStatusCode.Created, (await MoveAsync(admin, pid, 10)).StatusCode);
            Assert.Equal(HttpStatusCode.Created, (await MoveAsync(admin, high, -5)).StatusCode);

            Assert.Empty(await DrainAsync());
        }

        [Fact]
        public async Task CheckoutCrossingTwoProducts_SendsOneDigestPerAdmin()
        {
            var admins = await SetUpAdminsAsync(withEmail: 2);
            var admin = await AdminAsync();
            var (buyer, _) = await RegisterAndLoginAsync(_factory);
            var nameA = AuthHelper.Unique("digestA");
            var nameB = AuthHelper.Unique("digestB");
            var a = await CreateProductAsync(admin, nameA, 5, quantity: 6);
            var b = await CreateProductAsync(admin, nameB, 5, quantity: 5);
            var calm = await CreateProductAsync(admin, null, 5, quantity: 30);
            await AddOkAsync(buyer, a, 3);
            await AddOkAsync(buyer, b, 1);
            await AddOkAsync(buyer, calm, 2);
            _factory.Emails.Clear();

            await CheckoutOkAsync(buyer);

            var mails = await DrainAsync();
            Assert.Equal(2, mails.Count);
            Assert.Equal(admins.OrderBy(x => x), mails.Select(m => m.To).OrderBy(x => x));
            Assert.All(mails, m =>
            {
                Assert.Equal("Low stock: 2 products", m.Subject);
                Assert.Contains($"#{a} {nameA}: 3 left (was 6)", m.Body);
                Assert.Contains($"#{b} {nameB}: 4 left (was 5)", m.Body);
                Assert.DoesNotContain($"#{calm}", m.Body);
            });
        }

        [Fact]
        public async Task Restocked_ThenCrossingAgain_SendsANewMail()
        {
            await SetUpAdminsAsync(withEmail: 1);
            var admin = await AdminAsync();
            var pid = await CreateProductAsync(admin, quantity: 6);

            Assert.Equal(HttpStatusCode.Created, (await MoveAsync(admin, pid, -2)).StatusCode);
            Assert.Equal(HttpStatusCode.Created, (await MoveAsync(admin, pid, -1)).StatusCode);
            Assert.Single(await DrainAsync());

            Assert.Equal(HttpStatusCode.Created, (await MoveAsync(admin, pid, 10)).StatusCode);
            Assert.Equal(HttpStatusCode.Created, (await MoveAsync(admin, pid, -10)).StatusCode);

            var mails = await DrainAsync();
            Assert.Equal(2, mails.Count);
            Assert.Contains("3 left (was 13)", mails[1].Body);
        }

        [Fact]
        public async Task NoAdminWithEmail_SendsNothing_AndNothingFails()
        {
            await SetUpAdminsAsync(withEmail: 0, withoutEmail: 2);
            var admin = await AdminAsync();
            var pid = await CreateProductAsync(admin, quantity: 6);

            Assert.Equal(HttpStatusCode.Created, (await MoveAsync(admin, pid, -3)).StatusCode);

            Assert.Empty(await DrainAsync());
        }

        [Fact]
        public async Task DemotedAdmin_StopsReceiving()
        {
            var admins = await SetUpAdminsAsync(withEmail: 2);
            var admin = await AdminAsync();
            int demotedId;
            using (var scope = _factory.Services.CreateScope())
                demotedId = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
                    .Users.Where(u => u.Email == admins[0]).Select(u => u.Id).SingleAsync();
            Assert.Equal(HttpStatusCode.OK,
                (await admin.PutAsJsonAsync($"api/users/{demotedId}/role", new { role = "User" })).StatusCode);
            var pid = await CreateProductAsync(admin, quantity: 6);

            Assert.Equal(HttpStatusCode.Created, (await MoveAsync(admin, pid, -3)).StatusCode);

            Assert.Equal(admins[1], Assert.Single(await DrainAsync()).To);
        }

        [Fact]
        public async Task OrderCancelRestock_SendsNothing()
        {
            await SetUpAdminsAsync(withEmail: 1);
            var admin = await AdminAsync();
            var (buyer, _) = await RegisterAndLoginAsync(_factory);
            var pid = await CreateProductAsync(admin, quantity: 6);
            await AddOkAsync(buyer, pid, 2);
            var orderId = await CheckoutOkAsync(buyer);
            Assert.Single(await DrainAsync()); // the checkout itself crossed 6 -> 4
            _factory.Emails.Clear();

            Assert.Equal(HttpStatusCode.OK, (await CancelAsync(buyer, orderId)).StatusCode);

            Assert.Empty(await DrainAsync());
        }
    }

    public class LowStockDisabledFactory : ApiFactory
    {
        protected override int LowStockThreshold => 0;
    }

    public class LowStockDisabledEmailTests : IClassFixture<LowStockDisabledFactory>
    {
        private readonly LowStockDisabledFactory _factory;
        public LowStockDisabledEmailTests(LowStockDisabledFactory factory) => _factory = factory;

        [Fact]
        public async Task ThresholdZero_NeverSendsLowStockMail()
        {
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                db.Users.Add(new User { UserName = AuthHelper.Unique("adm"), PasswordHash = "x", Role = "Admin", Email = $"z{Guid.NewGuid():N}@example.com" });
                await db.SaveChangesAsync();
            }
            var admin = await AuthHelper.LoggedInAsync(_factory, ApiFactory.UserName);
            var pid = await CreateProductAsync(admin, quantity: 3);

            Assert.Equal(HttpStatusCode.Created, (await InventoryHelper.MoveAsync(admin, pid, -3, "Adjustment")).StatusCode);

            await _factory.EmailQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10));
            Assert.Empty(_factory.Emails.Messages);
        }
    }
}
