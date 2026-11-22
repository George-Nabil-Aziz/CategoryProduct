using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using static CategoryProduct.API.Tests.PasswordTestSupport;

namespace CategoryProduct.API.Tests
{
    public class ChangePasswordTests : IClassFixture<ApiFactory>
    {
        private readonly ApiFactory _f;
        public ChangePasswordTests(ApiFactory f) => _f = f;

        private static Task<HttpResponseMessage> ChangeAsync(HttpClient c, string? current, string? next) =>
            c.PostAsJsonAsync("api/account/change-password", new { currentPassword = current, newPassword = next });

        [Fact]
        public async Task Change_Ok_ReturnsFreshLogin_NewPasswordWorks_OldFails_AccessTokenWorks()
        {
            var (name, _) = await RegisterAsync(_f);
            var c = await AuthHelper.LoggedInAsync(_f, name, OldPassword);

            var r = await ChangeAsync(c, OldPassword, NewPassword);

            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            var json = await r.Content.ReadFromJsonAsync<JsonElement>();
            Assert.False(string.IsNullOrEmpty(json.GetProperty("token").GetString()));
            Assert.False(string.IsNullOrEmpty(json.GetProperty("refreshToken").GetString()));
            Assert.Equal(HttpStatusCode.OK, (await LoginAsync(_f, name, NewPassword)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(_f, name, OldPassword)).StatusCode);

            var me = _f.CreateClient();
            me.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.GetProperty("token").GetString());
            Assert.Equal(HttpStatusCode.OK, (await me.GetAsync("api/account/me")).StatusCode);
        }

        [Fact]
        public async Task WrongCurrent_Returns400_NothingChanged_NoMail_NoRevocation()
        {
            var (name, email) = await RegisterAsync(_f);
            var c = await AuthHelper.LoggedInAsync(_f, name, OldPassword);
            var before = await PasswordHashOfAsync(_f, name);

            var r = await ChangeAsync(c, "wrong-password", NewPassword);

            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.True((await ErrorsAsync(r)).TryGetProperty("CurrentPassword", out _));
            Assert.Equal(before, await PasswordHashOfAsync(_f, name));
            Assert.Empty(await MailsToAsync(_f, email));
            Assert.All(await RefreshTokensOfAsync(_f, name), t => Assert.Null(t.RevokedAtUtc));
        }

        [Theory]
        [InlineData("short")]
        [InlineData("")]
        [InlineData(null)]
        public async Task WeakOrMissingNewPassword_Returns400(string? weak)
        {
            var (name, _) = await RegisterAsync(_f);
            var c = await AuthHelper.LoggedInAsync(_f, name, OldPassword);

            var r = await ChangeAsync(c, OldPassword, weak);

            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.True((await ErrorsAsync(r)).TryGetProperty("NewPassword", out _));
            Assert.Equal(HttpStatusCode.OK, (await LoginAsync(_f, name, OldPassword)).StatusCode);
        }

        [Fact]
        public async Task NewEqualsCurrent_Returns400()
        {
            var (name, _) = await RegisterAsync(_f);
            var c = await AuthHelper.LoggedInAsync(_f, name, OldPassword);

            var r = await ChangeAsync(c, OldPassword, OldPassword);

            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.True((await ErrorsAsync(r)).TryGetProperty("NewPassword", out _));
        }

        [Fact]
        public async Task Anonymous_Returns401()
        {
            var r = await ChangeAsync(_f.CreateClient(), OldPassword, NewPassword);
            Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        }

        [Fact]
        public async Task AllOldRefreshTokensRevoked_OnlyTheReturnedOneWorks()
        {
            var (name, _) = await RegisterAsync(_f);
            var olds = new List<string>();
            for (var i = 0; i < 3; i++)
                olds.Add((await LoginOkAsync(_f, name, OldPassword)).GetProperty("refreshToken").GetString()!);
            var c = await AuthHelper.LoggedInAsync(_f, name, OldPassword);

            var r = await ChangeAsync(c, OldPassword, NewPassword);
            var fresh = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("refreshToken").GetString();

            foreach (var old in olds)
                Assert.Equal(HttpStatusCode.Unauthorized,
                    (await _f.CreateClient().PostAsJsonAsync("api/account/refresh", new { refreshToken = old })).StatusCode);
            Assert.Single(await RefreshTokensOfAsync(_f, name), t => t.RevokedAtUtc == null);
            Assert.Equal(HttpStatusCode.OK,
                (await _f.CreateClient().PostAsJsonAsync("api/account/refresh", new { refreshToken = fresh })).StatusCode);
        }

        [Fact]
        public async Task Mail_IsQueuedForUserWithEmail_WithoutPassword_AndNotForUserWithout()
        {
            var (name, email) = await RegisterAsync(_f);
            var c = await AuthHelper.LoggedInAsync(_f, name, OldPassword);
            Assert.Equal(HttpStatusCode.OK, (await ChangeAsync(c, OldPassword, NewPassword)).StatusCode);

            var mails = await MailsToAsync(_f, email);
            var mail = Assert.Single(mails, m => m.Subject == "Your password was changed");
            Assert.DoesNotContain(OldPassword, mail.Subject + mail.Body);
            Assert.DoesNotContain(NewPassword, mail.Subject + mail.Body);

            var (noMail, _) = await RegisterAsync(_f, withEmail: false);
            var c2 = await AuthHelper.LoggedInAsync(_f, noMail, OldPassword);
            await IdleAsync(_f);
            var before = _f.Emails.Messages.Count;
            Assert.Equal(HttpStatusCode.OK, (await ChangeAsync(c2, OldPassword, NewPassword)).StatusCode);
            await IdleAsync(_f);
            Assert.Equal(before, _f.Emails.Messages.Count);
        }
    }
}
