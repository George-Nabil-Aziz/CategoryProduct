using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace CategoryProduct.API.Tests
{
    internal static class AuthHelper
    {
        public static async Task<HttpClient> LoggedInAsync(ApiFactory f, string user, string password = ApiFactory.Password)
        {
            var c = f.CreateClient();
            var r = await c.PostAsJsonAsync("api/account/login", new { userName = user, password });
            r.EnsureSuccessStatusCode();
            var json = await r.Content.ReadFromJsonAsync<JsonElement>();
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.GetProperty("token").GetString());
            return c;
        }

        public static string Unique(string prefix) => prefix + Guid.NewGuid().ToString("N")[..8];
    }

    public class UsersTests : IClassFixture<ApiFactory>
    {
        private readonly ApiFactory _factory;
        public UsersTests(ApiFactory factory) => _factory = factory;

        [Fact]
        public async Task Register_Succeeds_And_CanLogin()
        {
            var name = AuthHelper.Unique("reg");
            var c = _factory.CreateClient();
            var r = await c.PostAsJsonAsync("api/account/register", new { userName = name, password = "longenough1" });
            Assert.Equal(HttpStatusCode.Created, r.StatusCode);
            var json = await r.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("User", json.GetProperty("role").GetString());
            Assert.False(json.TryGetProperty("passwordHash", out _));

            var login = await c.PostAsJsonAsync("api/account/login", new { userName = name, password = "longenough1" });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        }

        [Fact]
        public async Task Register_DuplicateDifferentCase_Returns409()
        {
            var name = AuthHelper.Unique("dup");
            var c = _factory.CreateClient();
            Assert.Equal(HttpStatusCode.Created,
                (await c.PostAsJsonAsync("api/account/register", new { userName = name, password = "longenough1" })).StatusCode);
            var r = await c.PostAsJsonAsync("api/account/register", new { userName = name.ToUpper(), password = "longenough1" });
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        }

        [Fact]
        public async Task Register_ShortPassword_Returns400()
        {
            var r = await _factory.CreateClient().PostAsJsonAsync("api/account/register",
                new { userName = AuthHelper.Unique("short"), password = "short" });
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        }

        [Fact]
        public async Task Users_NonAdmin_Returns403()
        {
            var c = await AuthHelper.LoggedInAsync(_factory, ApiFactory.PlainUserName);
            Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("api/users")).StatusCode);
        }

        [Fact]
        public async Task Users_Admin_ListsWithoutPasswordHash()
        {
            var c = await AuthHelper.LoggedInAsync(_factory, ApiFactory.UserName);
            var r = await c.GetAsync("api/users");
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            var body = await r.Content.ReadAsStringAsync();
            Assert.DoesNotContain("passwordHash", body, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(ApiFactory.PlainUserName, body);
        }

        [Fact]
        public async Task Admin_CanPromoteUser()
        {
            var name = AuthHelper.Unique("promo");
            var anon = _factory.CreateClient();
            var created = await (await anon.PostAsJsonAsync("api/account/register", new { userName = name, password = "longenough1" }))
                .Content.ReadFromJsonAsync<JsonElement>();
            var id = created.GetProperty("id").GetInt32();

            var admin = await AuthHelper.LoggedInAsync(_factory, ApiFactory.UserName);
            var r = await admin.PutAsJsonAsync($"api/users/{id}/role", new { role = "Admin" });
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            var bad = await admin.PutAsJsonAsync($"api/users/{id}/role", new { role = "Root" });
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        }
    }

    public class LastAdminTests : IClassFixture<ApiFactory>
    {
        private readonly ApiFactory _factory;
        public LastAdminTests(ApiFactory factory) => _factory = factory;

        [Fact]
        public async Task DemotingLastAdmin_Returns409()
        {
            var admin = await AuthHelper.LoggedInAsync(_factory, ApiFactory.UserName);
            var list = await admin.GetFromJsonAsync<JsonElement>("api/users?search=" + ApiFactory.UserName);
            var id = list.GetProperty("items")[0].GetProperty("id").GetInt32();
            var r = await admin.PutAsJsonAsync($"api/users/{id}/role", new { role = "User" });
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        }
    }
}
