using CategoryProduct.API.Models;
using CategoryProduct.API.Services;
using Microsoft.Extensions.DependencyInjection;
using static CategoryProduct.API.Tests.PasswordTestSupport;

namespace CategoryProduct.API.Tests
{
    /// <summary>Message format of the two password mails, through the real NotificationService, queue and recording sender.</summary>
    public class PasswordEmailFormatTests : IClassFixture<ApiFactory>
    {
        private readonly ApiFactory _f;
        public PasswordEmailFormatTests(ApiFactory f) => _f = f;

        private static User U(string? email, string name = "alice") =>
            new() { Id = 1, UserName = name, PasswordHash = "x", Role = "User", Email = email };

        private static string NewAddress() => $"fmt{Guid.NewGuid():N}@example.com";

        private async Task<List<EmailMessage>> RunAsync(string address, Func<NotificationService, Task> act)
        {
            using var scope = _f.Services.CreateScope();
            await act(scope.ServiceProvider.GetRequiredService<NotificationService>());
            return await MailsToAsync(_f, address);
        }

        [Fact]
        public async Task ResetMail_HasSubjectBodyAndLinkOnItsOwnLine_AndValidates()
        {
            var to = NewAddress();
            const string link = "https://mvc.test/Account/ResetPassword?token=SECRETTOKEN123";

            var mails = await RunAsync(to, s => s.PasswordResetRequestedAsync(U(to), link, 30));

            var m = Assert.Single(mails);
            Assert.Equal("Reset your password", m.Subject);
            Assert.DoesNotContain('\r', m.Subject);
            Assert.DoesNotContain('\n', m.Subject);
            Assert.Null(m.Validate());
            Assert.Contains("Hello alice,", m.Body);
            Assert.Contains("valid for 30 minutes, one use", m.Body);
            Assert.Contains(m.Body.Split('\n'), l => l == link);
        }

        [Fact]
        public async Task ChangedMail_HasNoPasswordTokenOrLink_AndValidates()
        {
            var to = NewAddress();

            var mails = await RunAsync(to, s => s.PasswordChangedAsync(U(to)));

            var m = Assert.Single(mails);
            Assert.Equal("Your password was changed", m.Subject);
            Assert.Null(m.Validate());
            Assert.Contains("UTC", m.Body);
            Assert.DoesNotContain("http", m.Body);
            Assert.DoesNotContain("token", m.Body, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task UserNameLineBreaks_AreStrippedFromBody()
        {
            var to = NewAddress();

            var mails = await RunAsync(to, s => s.PasswordChangedAsync(U(to, "ali\r\nBcc: x")));

            Assert.Contains("Hello aliBcc: x,", Assert.Single(mails).Body);
        }

        [Fact]
        public async Task NoEmail_QueuesNothing_AndDoesNotThrow()
        {
            await IdleAsync(_f);
            var before = _f.Emails.Messages.Count;

            using var scope = _f.Services.CreateScope();
            var s = scope.ServiceProvider.GetRequiredService<NotificationService>();
            await s.PasswordChangedAsync(U(null));
            await s.PasswordResetRequestedAsync(U(""), "https://x/y", 30);
            await IdleAsync(_f);

            Assert.Equal(before, _f.Emails.Messages.Count);
        }

        [Fact]
        public async Task EmailDisabled_QueuesNothing()
        {
            using var off = new DisabledFactory();
            using var scope = off.Services.CreateScope();
            var s = scope.ServiceProvider.GetRequiredService<NotificationService>();
            await s.PasswordChangedAsync(U("a@example.com"));
            await s.PasswordResetRequestedAsync(U("a@example.com"), "https://x/y", 30);
            await IdleAsync(off);

            Assert.Empty(off.Emails.Messages);
        }

        private sealed class DisabledFactory : ApiFactory
        {
            protected override bool EmailEnabled => false;
        }
    }
}
