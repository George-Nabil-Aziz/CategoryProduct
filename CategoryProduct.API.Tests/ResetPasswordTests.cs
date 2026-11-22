using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static CategoryProduct.API.Tests.PasswordTestSupport;

namespace CategoryProduct.API.Tests
{
    public class ResetPasswordTests : IClassFixture<ApiFactory>
    {
        private readonly ApiFactory _f;
        public ResetPasswordTests(ApiFactory f) => _f = f;

        private static async Task AssertInvalidTokenAsync(HttpResponseMessage r)
        {
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            var errors = await ErrorsAsync(r);
            Assert.Equal(InvalidTokenMessage, errors.GetProperty("Token")[0].GetString());
        }

        [Fact]
        public async Task Success_Returns204_NewPasswordWorks_OldFails_TokenMarkedUsed()
        {
            var (name, email) = await RegisterAsync(_f);
            var token = await IssueTokenAsync(_f, name, email);

            var r = await ResetAsync(_f, token, NewPassword);

            Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
            Assert.Equal(string.Empty, await r.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.OK, (await LoginAsync(_f, name, NewPassword)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(_f, name, OldPassword)).StatusCode);
            Assert.NotNull(Assert.Single(await TokensOfAsync(_f, name)).UsedAtUtc);
        }

        [Fact]
        public async Task SecondUse_Returns400_PasswordStaysAsFirstCallSetIt()
        {
            var (name, email) = await RegisterAsync(_f);
            var token = await IssueTokenAsync(_f, name, email);
            Assert.Equal(HttpStatusCode.NoContent, (await ResetAsync(_f, token, NewPassword)).StatusCode);

            await AssertInvalidTokenAsync(await ResetAsync(_f, token, "another-password-3"));

            Assert.Equal(HttpStatusCode.OK, (await LoginAsync(_f, name, NewPassword)).StatusCode);
        }

        [Fact]
        public async Task Expiry_IsExactlyThirtyMinutes()
        {
            var (name, email) = await RegisterAsync(_f);
            var token = await IssueTokenAsync(_f, name, email);
            var (name2, email2) = await RegisterAsync(_f);
            var token2 = await IssueTokenAsync(_f, name2, email2);

            _f.Clock.Advance(TimeSpan.FromMinutes(29) + TimeSpan.FromSeconds(59));
            Assert.Equal(HttpStatusCode.NoContent, (await ResetAsync(_f, token2, NewPassword)).StatusCode);

            _f.Clock.Advance(TimeSpan.FromSeconds(1));
            await AssertInvalidTokenAsync(await ResetAsync(_f, token, NewPassword));
            Assert.Equal(HttpStatusCode.OK, (await LoginAsync(_f, name, OldPassword)).StatusCode);
        }

        [Fact]
        public async Task UnknownAndEmptyToken_GiveTheSame400Message()
        {
            await AssertInvalidTokenAsync(await ResetAsync(_f, "made-up-token", NewPassword));
            await AssertInvalidTokenAsync(await ResetAsync(_f, "", NewPassword));
            await AssertInvalidTokenAsync(await ResetAsync(_f, null, NewPassword));
        }

        [Fact]
        public async Task NewerToken_InvalidatesTheOlder()
        {
            var (name, email) = await RegisterAsync(_f);
            var first = await IssueTokenAsync(_f, name, email);
            _f.Clock.Advance(TimeSpan.FromSeconds(61));
            var second = await IssueTokenAsync(_f, name, email);
            Assert.NotEqual(first, second);

            await AssertInvalidTokenAsync(await ResetAsync(_f, first, NewPassword));
            Assert.Equal(HttpStatusCode.NoContent, (await ResetAsync(_f, second, NewPassword)).StatusCode);
        }

        [Fact]
        public async Task WeakPassword_Returns400OnNewPassword_TokenStillWorksAfterwards()
        {
            var (name, email) = await RegisterAsync(_f);
            var token = await IssueTokenAsync(_f, name, email);

            var r = await ResetAsync(_f, token, "short");

            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.True((await ErrorsAsync(r)).TryGetProperty("NewPassword", out _));
            Assert.Null(Assert.Single(await TokensOfAsync(_f, name)).UsedAtUtc);
            Assert.Equal(HttpStatusCode.NoContent, (await ResetAsync(_f, token, NewPassword)).StatusCode);
        }

        [Fact]
        public async Task RefreshTokens_AreRevoked_AndNoTokensAreReturned()
        {
            var (name, email) = await RegisterAsync(_f);
            var olds = new List<string>();
            for (var i = 0; i < 2; i++)
                olds.Add((await LoginOkAsync(_f, name, OldPassword)).GetProperty("refreshToken").GetString()!);
            var token = await IssueTokenAsync(_f, name, email);

            var r = await ResetAsync(_f, token, NewPassword);

            Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
            Assert.Equal(string.Empty, await r.Content.ReadAsStringAsync());
            foreach (var old in olds)
                Assert.Equal(HttpStatusCode.Unauthorized,
                    (await _f.CreateClient().PostAsJsonAsync("api/account/refresh", new { refreshToken = old })).StatusCode);
            Assert.All(await RefreshTokensOfAsync(_f, name), t => Assert.NotNull(t.RevokedAtUtc));
        }

        [Fact]
        public async Task ChangedMail_IsQueued_WithoutTokenOrPassword()
        {
            var (name, email) = await RegisterAsync(_f);
            var token = await IssueTokenAsync(_f, name, email);

            Assert.Equal(HttpStatusCode.NoContent, (await ResetAsync(_f, token, NewPassword)).StatusCode);

            var mail = Assert.Single(await MailsToAsync(_f, email), m => m.Subject == "Your password was changed");
            var text = mail.Subject + mail.Body;
            Assert.DoesNotContain(token, text);
            Assert.DoesNotContain(NewPassword, text);
        }

        [Fact]
        public async Task OtherUnusedTokens_AreInvalidatedOnSuccess()
        {
            var (name, email) = await RegisterAsync(_f);
            var token = await IssueTokenAsync(_f, name, email);
            // A second unused token for the same user (inserted directly: the interval would suppress a second mail).
            await DbAsync(_f, async db =>
            {
                var userId = db.Users.Single(u => u.UserName == name).Id;
                var now = _f.Clock.GetUtcNow().UtcDateTime;
                db.PasswordResetTokens.Add(new Models.PasswordResetToken
                {
                    UserId = userId, TokenHash = "other-" + Guid.NewGuid().ToString("N"),
                    CreatedAtUtc = now, ExpiresAtUtc = now.AddMinutes(30)
                });
                await db.SaveChangesAsync();
                return 0;
            });

            Assert.Equal(HttpStatusCode.NoContent, (await ResetAsync(_f, token, NewPassword)).StatusCode);

            Assert.All(await TokensOfAsync(_f, name), t => Assert.NotNull(t.UsedAtUtc));
        }

        [Fact]
        public async Task TwoParallelResets_WithTheSameToken_ExactlyOneWins()
        {
            var (name, email) = await RegisterAsync(_f);
            var token = await IssueTokenAsync(_f, name, email);

            var results = await Task.WhenAll(
                ResetAsync(_f, token, "parallel-pass-aaa"),
                ResetAsync(_f, token, "parallel-pass-bbb"));

            var statuses = results.Select(r => r.StatusCode).OrderBy(s => (int)s).ToArray();
            Assert.Equal(new[] { HttpStatusCode.NoContent, HttpStatusCode.BadRequest }, statuses);
        }
    }
}
