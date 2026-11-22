using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CategoryProduct.API.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static CategoryProduct.API.Tests.CartOrderTestHelpers;

namespace CategoryProduct.API.Tests
{
    public class UserEmailTests : IClassFixture<ApiFactory>
    {
        private readonly ApiFactory _factory;
        public UserEmailTests(ApiFactory factory) => _factory = factory;

        private static string UniqueEmail(string prefix = "u") => $"{prefix}{Guid.NewGuid():N}@example.com";

        private Task<HttpResponseMessage> RegisterAsync(string userName, object? email, bool includeEmail = true) =>
            includeEmail
                ? _factory.CreateClient().PostAsJsonAsync("api/account/register",
                    new { userName, password = UserPassword, email })
                : _factory.CreateClient().PostAsJsonAsync("api/account/register",
                    new { userName, password = UserPassword });

        private async Task<string?> StoredEmailAsync(string userName)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            return await db.Users.Where(u => u.UserName == userName).Select(u => u.Email).SingleAsync();
        }

        private async Task<bool> UserExistsAsync(string userName)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            return await db.Users.AnyAsync(u => u.UserName == userName);
        }

        private async Task<(HttpClient Client, string UserName, string? Email)> NewUserAsync(bool withEmail)
        {
            var name = AuthHelper.Unique("em");
            var email = withEmail ? UniqueEmail() : null;
            var r = withEmail ? await RegisterAsync(name, email) : await RegisterAsync(name, null, includeEmail: false);
            Assert.Equal(HttpStatusCode.Created, r.StatusCode);
            return (await AuthHelper.LoggedInAsync(_factory, name, UserPassword), name, email);
        }

        private static Task<HttpResponseMessage> PutEmailAsync(HttpClient c, object? email) =>
            c.PutAsJsonAsync("api/account/email", new { email });

        // ---------- register ----------
        [Fact]
        public async Task Register_ValidEmail_IsNormalizedStoredAndReturned()
        {
            var name = AuthHelper.Unique("reg");
            var local = Guid.NewGuid().ToString("N");
            var r = await RegisterAsync(name, $"  {local.ToUpperInvariant()}@Example.COM  ");

            Assert.Equal(HttpStatusCode.Created, r.StatusCode);
            var json = await JsonAsync(r);
            Assert.Equal($"{local}@example.com", json.GetProperty("email").GetString());
            Assert.Equal($"{local}@example.com", await StoredEmailAsync(name));
        }

        [Fact]
        public async Task Register_WithoutEmail_Works_AndStoresNull()
        {
            var name = AuthHelper.Unique("noem");
            var r = await RegisterAsync(name, null, includeEmail: false);

            Assert.Equal(HttpStatusCode.Created, r.StatusCode);
            Assert.Equal(JsonValueKind.Null, (await JsonAsync(r)).GetProperty("email").ValueKind);
            Assert.Null(await StoredEmailAsync(name));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task Register_NullEmptyOrWhitespaceEmail_StoresNull(string? email)
        {
            var name = AuthHelper.Unique("none");
            var r = await RegisterAsync(name, email);

            Assert.Equal(HttpStatusCode.Created, r.StatusCode);
            Assert.Null(await StoredEmailAsync(name));
        }

        [Theory]
        [InlineData("plainaddress")]
        [InlineData("a@b")]
        [InlineData("@example.com")]
        [InlineData("A <a@example.com>")]
        [InlineData("a b@example.com")]
        [InlineData("a\r\nb@example.com")]
        [InlineData("a\nBcc: x@example.com")]
        public async Task Register_InvalidEmail_Returns400_AndCreatesNoUser(string email)
        {
            var name = AuthHelper.Unique("bad");
            var r = await RegisterAsync(name, email);

            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.Contains("Email", await ErrorKeysAsync(r));
            Assert.False(await UserExistsAsync(name));
        }

        [Fact]
        public async Task Register_TooLongEmail_Returns400()
        {
            var name = AuthHelper.Unique("long");
            var r = await RegisterAsync(name, new string('a', 250) + "@b.co");

            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.Contains("Email", await ErrorKeysAsync(r));
            Assert.False(await UserExistsAsync(name));
        }

        [Fact]
        public async Task Register_DuplicateEmail_DifferentCase_Returns409_AndCreatesNoUser()
        {
            var email = UniqueEmail();
            Assert.Equal(HttpStatusCode.Created, (await RegisterAsync(AuthHelper.Unique("first"), email)).StatusCode);

            var second = AuthHelper.Unique("second");
            var r = await RegisterAsync(second, email.ToUpperInvariant());

            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Equal("Email already in use.", (await JsonAsync(r)).GetProperty("detail").GetString());
            Assert.False(await UserExistsAsync(second));
        }

        [Fact]
        public async Task Register_TakenUserName_StillReturnsUsernameConflict()
        {
            var name = AuthHelper.Unique("taken");
            Assert.Equal(HttpStatusCode.Created, (await RegisterAsync(name, null, includeEmail: false)).StatusCode);

            var r = await RegisterAsync(name, UniqueEmail());

            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Equal("Username already taken.", (await JsonAsync(r)).GetProperty("detail").GetString());
        }

        // ---------- me ----------
        [Fact]
        public async Task Me_ReturnsOwnData_OnlyForTheCaller()
        {
            var (a, nameA, emailA) = await NewUserAsync(withEmail: true);
            var (b, nameB, _) = await NewUserAsync(withEmail: false);

            var jsonA = await JsonAsync(await a.GetAsync("api/account/me"));
            Assert.Equal(nameA, jsonA.GetProperty("userName").GetString());
            Assert.Equal("User", jsonA.GetProperty("role").GetString());
            Assert.Equal(emailA, jsonA.GetProperty("email").GetString());
            Assert.True(jsonA.GetProperty("id").GetInt32() > 0);

            var jsonB = await JsonAsync(await b.GetAsync("api/account/me"));
            Assert.Equal(nameB, jsonB.GetProperty("userName").GetString());
            Assert.Equal(JsonValueKind.Null, jsonB.GetProperty("email").ValueKind);
        }

        [Fact]
        public async Task Me_Anonymous_Returns401() =>
            Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("api/account/me")).StatusCode);

        // ---------- put email ----------
        [Fact]
        public async Task PutEmail_SetsChangesAndClears()
        {
            var (c, name, _) = await NewUserAsync(withEmail: false);

            var first = UniqueEmail("first");
            var r1 = await PutEmailAsync(c, first.ToUpperInvariant());
            Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
            Assert.Equal(first, (await JsonAsync(r1)).GetProperty("email").GetString());
            Assert.Equal(first, await StoredEmailAsync(name));

            var second = UniqueEmail("second");
            var r2 = await PutEmailAsync(c, second);
            Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
            Assert.Equal(second, await StoredEmailAsync(name));

            var r3 = await PutEmailAsync(c, "");
            Assert.Equal(HttpStatusCode.OK, r3.StatusCode);
            Assert.Equal(JsonValueKind.Null, (await JsonAsync(r3)).GetProperty("email").ValueKind);
            Assert.Null(await StoredEmailAsync(name));

            Assert.Equal(HttpStatusCode.OK, (await PutEmailAsync(c, second)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await PutEmailAsync(c, null)).StatusCode);
            Assert.Null(await StoredEmailAsync(name));
        }

        [Fact]
        public async Task PutEmail_SameValue_Returns200_NoConflictWithItself()
        {
            var (c, _, email) = await NewUserAsync(withEmail: true);

            var r = await PutEmailAsync(c, email!.ToUpperInvariant());

            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.Equal(email, (await JsonAsync(r)).GetProperty("email").GetString());
        }

        [Theory]
        [InlineData("not-an-email")]
        [InlineData("a@b")]
        [InlineData("A <a@example.com>")]
        [InlineData("a b@example.com")]
        public async Task PutEmail_Invalid_Returns400_AndKeepsTheOldValue(string bad)
        {
            var (c, name, email) = await NewUserAsync(withEmail: true);

            var r = await PutEmailAsync(c, bad);

            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.Contains("Email", await ErrorKeysAsync(r));
            Assert.Equal(email, await StoredEmailAsync(name));
        }

        [Fact]
        public async Task PutEmail_UsedByAnotherUser_Returns409_AnyCase()
        {
            var (_, _, taken) = await NewUserAsync(withEmail: true);
            var (c, name, _) = await NewUserAsync(withEmail: false);

            var r = await PutEmailAsync(c, taken!.ToUpperInvariant());

            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Equal("Email already in use.", (await JsonAsync(r)).GetProperty("detail").GetString());
            Assert.Null(await StoredEmailAsync(name));
        }

        [Fact]
        public async Task PutEmail_Anonymous_Returns401() =>
            Assert.Equal(HttpStatusCode.Unauthorized, (await PutEmailAsync(_factory.CreateClient(), UniqueEmail())).StatusCode);

        [Fact]
        public async Task PutEmail_AlwaysChangesTheCallersOwnRow()
        {
            var (a, nameA, emailA) = await NewUserAsync(withEmail: true);
            var (b, nameB, _) = await NewUserAsync(withEmail: false);

            var newB = UniqueEmail("b");
            Assert.Equal(HttpStatusCode.OK, (await PutEmailAsync(b, newB)).StatusCode);

            Assert.Equal(emailA, await StoredEmailAsync(nameA));
            Assert.Equal(newB, await StoredEmailAsync(nameB));
        }

        // ---------- visibility ----------
        [Fact]
        public async Task AdminList_IncludesEmail_PlainUserGets403()
        {
            var (_, name, email) = await NewUserAsync(withEmail: true);
            var (_, nameNone, _) = await NewUserAsync(withEmail: false);
            var admin = await AuthHelper.LoggedInAsync(_factory, ApiFactory.UserName);

            var json = await JsonAsync(await admin.GetAsync("api/users?pageSize=50&search=em"));
            var items = json.GetProperty("items").EnumerateArray().ToList();
            var mine = items.Single(i => i.GetProperty("userName").GetString() == name);
            Assert.Equal(email, mine.GetProperty("email").GetString());
            var none = items.Single(i => i.GetProperty("userName").GetString() == nameNone);
            Assert.Equal(JsonValueKind.Null, none.GetProperty("email").ValueKind);

            var plain = await AuthHelper.LoggedInAsync(_factory, ApiFactory.PlainUserName);
            Assert.Equal(HttpStatusCode.Forbidden, (await plain.GetAsync("api/users")).StatusCode);
        }

        [Fact]
        public async Task RoleUpdate_Response_CarriesEmail()
        {
            var (_, name, email) = await NewUserAsync(withEmail: true);
            var admin = await AuthHelper.LoggedInAsync(_factory, ApiFactory.UserName);
            int id;
            using (var scope = _factory.Services.CreateScope())
                id = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
                    .Users.Where(u => u.UserName == name).Select(u => u.Id).SingleAsync();

            var r = await admin.PutAsJsonAsync($"api/users/{id}/role", new { role = "User" });

            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.Equal(email, (await JsonAsync(r)).GetProperty("email").GetString());
        }

        [Fact]
        public async Task OrdersMovementsAndLogin_NeverExposeEmail()
        {
            var (buyer, name, email) = await NewUserAsync(withEmail: true);
            var admin = await AuthHelper.LoggedInAsync(_factory, ApiFactory.UserName);
            var product = await CreateProductAsync(admin, quantity: 10);
            await AddOkAsync(buyer, product, 2);
            var orderId = await CheckoutOkAsync(buyer);

            var raw = new List<string>
            {
                await (await buyer.GetAsync($"api/orders/{orderId}")).Content.ReadAsStringAsync(),
                await (await buyer.GetAsync("api/orders")).Content.ReadAsStringAsync(),
                await (await admin.GetAsync("api/orders/all")).Content.ReadAsStringAsync(),
                await (await admin.GetAsync($"api/orders/{orderId}")).Content.ReadAsStringAsync(),
                await (await admin.GetAsync($"api/product/{product}/stock-movements?pageSize=50")).Content.ReadAsStringAsync(),
                await (await _factory.CreateClient().PostAsJsonAsync("api/account/login",
                    new { userName = name, password = UserPassword })).Content.ReadAsStringAsync()
            };

            Assert.All(raw, body =>
            {
                Assert.DoesNotContain("\"email\"", body, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(email!, body, StringComparison.OrdinalIgnoreCase);
            });
        }
    }
}
