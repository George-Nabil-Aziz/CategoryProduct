using System.Net;
using CategoryProduct.API.Services;
using static CategoryProduct.API.Tests.PasswordTestSupport;

namespace CategoryProduct.API.Tests
{
    public class ForgotPasswordTests : IClassFixture<ApiFactory>
    {
        private readonly ApiFactory _f;
        public ForgotPasswordTests(ApiFactory f) => _f = f;

        [Fact]
        public async Task ByUserName_CreatesHashOnlyToken_AndMailsTheLink()
        {
            var (name, email) = await RegisterAsync(_f);
            var now = _f.Clock.GetUtcNow().UtcDateTime;

            var r = await ForgotAsync(_f, name);

            Assert.Equal(HttpStatusCode.Accepted, r.StatusCode);
            var rows = await TokensOfAsync(_f, name);
            var row = Assert.Single(rows);
            var mail = Assert.Single(await MailsToAsync(_f, email));
            Assert.Equal("Reset your password", mail.Subject);
            Assert.Contains(LinkPrefix, mail.Body);
            var token = TokenFrom(mail);
            Assert.Equal(TokenService.Hash(token), row.TokenHash);
            Assert.NotEqual(token, row.TokenHash);
            Assert.Equal(now.AddMinutes(30), row.ExpiresAtUtc);
            Assert.Null(row.UsedAtUtc);
        }

        [Fact]
        public async Task ByEmailInAnotherCase_Works()
        {
            var (name, email) = await RegisterAsync(_f);

            var r = await ForgotAsync(_f, "  " + email.ToUpperInvariant() + " ");

            Assert.Equal(HttpStatusCode.Accepted, r.StatusCode);
            Assert.Single(await TokensOfAsync(_f, name));
            Assert.Single(await MailsToAsync(_f, email));
        }

        [Fact]
        public async Task UnknownNameAndEmail_GiveTheIdenticalResponse_NoRowNoMail()
        {
            var (name, email) = await RegisterAsync(_f);
            await IdleAsync(_f);
            var rowsBefore = await DbAsync(_f, db => Task.FromResult(db.PasswordResetTokens.Count()));
            var mailsBefore = _f.Emails.Messages.Count;

            var known = await ForgotAsync(_f, name);
            var unknownName = await ForgotAsync(_f, "nobody" + Guid.NewGuid().ToString("N")[..8]);
            var unknownEmail = await ForgotAsync(_f, $"nobody{Guid.NewGuid():N}@example.com");

            foreach (var r in new[] { unknownName, unknownEmail })
            {
                Assert.Equal(known.StatusCode, r.StatusCode);
                Assert.Equal(known.Content.Headers.ContentType?.ToString(), r.Content.Headers.ContentType?.ToString());
                Assert.Equal(await known.Content.ReadAsStringAsync(), await r.Content.ReadAsStringAsync());
            }
            await IdleAsync(_f);
            Assert.Equal(rowsBefore + 1, await DbAsync(_f, db => Task.FromResult(db.PasswordResetTokens.Count())));
            Assert.Equal(mailsBefore + 1, _f.Emails.Messages.Count);
            Assert.Single(await MailsToAsync(_f, email));
        }

        [Fact]
        public async Task UserWithoutEmail_Gets202_NoRow_NoMail()
        {
            var (name, _) = await RegisterAsync(_f, withEmail: false);
            await IdleAsync(_f);
            var mailsBefore = _f.Emails.Messages.Count;

            var r = await ForgotAsync(_f, name);

            Assert.Equal(HttpStatusCode.Accepted, r.StatusCode);
            Assert.Empty(await TokensOfAsync(_f, name));
            await IdleAsync(_f);
            Assert.Equal(mailsBefore, _f.Emails.Messages.Count);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public async Task EmptyWhitespaceOrMissingInput_Returns400(string? input)
        {
            var r = await ForgotAsync(_f, input);
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.True((await ErrorsAsync(r)).TryGetProperty("UserNameOrEmail", out _));
        }

        [Fact]
        public async Task TooLongInput_Returns400()
        {
            var r = await ForgotAsync(_f, new string('a', 255));
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.True((await ErrorsAsync(r)).TryGetProperty("UserNameOrEmail", out _));
        }

        [Fact]
        public async Task SecondRequestWithinInterval_IsSilentlyIgnored_AfterItANewTokenSupersedesTheFirst()
        {
            var (name, email) = await RegisterAsync(_f);
            Assert.Equal(HttpStatusCode.Accepted, (await ForgotAsync(_f, name)).StatusCode);
            await IdleAsync(_f);

            Assert.Equal(HttpStatusCode.Accepted, (await ForgotAsync(_f, name)).StatusCode);
            await IdleAsync(_f);
            Assert.Single(await TokensOfAsync(_f, name));
            Assert.Single(await MailsToAsync(_f, email));

            _f.Clock.Advance(TimeSpan.FromSeconds(61));
            Assert.Equal(HttpStatusCode.Accepted, (await ForgotAsync(_f, name)).StatusCode);

            var rows = await TokensOfAsync(_f, name);
            Assert.Equal(2, rows.Count);
            Assert.NotNull(rows[0].UsedAtUtc);
            Assert.Null(rows[1].UsedAtUtc);
            Assert.Equal(2, (await MailsToAsync(_f, email)).Count);
        }

        [Fact]
        public async Task SendFailure_StillReturns202()
        {
            var (name, email) = await RegisterAsync(_f);
            _f.Emails.ThrowOnSend = true;
            try
            {
                var r = await ForgotAsync(_f, name);
                await IdleAsync(_f);
                Assert.Equal(HttpStatusCode.Accepted, r.StatusCode);
                Assert.Single(await TokensOfAsync(_f, name));
            }
            finally { _f.Emails.ThrowOnSend = false; }
            Assert.Empty(await MailsToAsync(_f, email));
        }

        [Fact]
        public async Task EmailDisabled_Returns202_NoRow()
        {
            using var off = new DisabledFactory();
            var (name, _) = await RegisterAsync(off);

            var r = await ForgotAsync(off, name);

            Assert.Equal(HttpStatusCode.Accepted, r.StatusCode);
            Assert.Empty(await TokensOfAsync(off, name));
        }

        [Fact]
        public async Task InvalidPublicBaseUrl_FallsBackToLocalhostDefault()
        {
            using var bad = new BadBaseUrlFactory();
            var (name, email) = await RegisterAsync(bad);

            Assert.Equal(HttpStatusCode.Accepted, (await ForgotAsync(bad, name)).StatusCode);

            var mail = Assert.Single(await MailsToAsync(bad, email));
            Assert.Contains("http://localhost:5256/Account/ResetPassword?token=", mail.Body);
        }

        private sealed class DisabledFactory : ApiFactory
        {
            protected override bool EmailEnabled => false;
        }

        private sealed class BadBaseUrlFactory : ApiFactory
        {
            protected override string PublicMvcBaseUrl => "not a url";
        }
    }
}
