using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CategoryProduct.API.Data;
using CategoryProduct.API.Models;
using CategoryProduct.API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CategoryProduct.API.Tests
{
    internal static class PasswordTestSupport
    {
        public const string OldPassword = "longenough1";
        public const string NewPassword = "brandnew-pass-2";
        public const string LinkPrefix = "https://mvc.test/Account/ResetPassword?token=";
        public const string InvalidTokenMessage = "The reset link is invalid or has expired.";

        public static async Task<(string UserName, string Email)> RegisterAsync(ApiFactory f, bool withEmail = true)
        {
            var name = AuthHelper.Unique("pw");
            var email = withEmail ? $"{name}@example.com" : null;
            var r = await f.CreateClient().PostAsJsonAsync("api/account/register",
                new { userName = name, password = OldPassword, email });
            Assert.Equal(HttpStatusCode.Created, r.StatusCode);
            return (name, email!);
        }

        public static Task<HttpResponseMessage> LoginAsync(ApiFactory f, string user, string password) =>
            f.CreateClient().PostAsJsonAsync("api/account/login", new { userName = user, password });

        public static async Task<JsonElement> LoginOkAsync(ApiFactory f, string user, string password)
        {
            var r = await LoginAsync(f, user, password);
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            return await r.Content.ReadFromJsonAsync<JsonElement>();
        }

        public static Task<HttpResponseMessage> ForgotAsync(ApiFactory f, string? input) =>
            f.CreateClient().PostAsJsonAsync("api/account/forgot-password", new { userNameOrEmail = input });

        public static Task<HttpResponseMessage> ResetAsync(ApiFactory f, string? token, string? newPassword) =>
            f.CreateClient().PostAsJsonAsync("api/account/reset-password", new { token, newPassword });

        public static Task IdleAsync(ApiFactory f) => f.EmailQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10));

        public static async Task<List<EmailMessage>> MailsToAsync(ApiFactory f, string email)
        {
            await IdleAsync(f);
            return f.Emails.Messages.Where(m => m.To == email).ToList();
        }

        public static string TokenFrom(EmailMessage mail)
        {
            var line = mail.Body.Split('\n').Single(l => l.StartsWith(LinkPrefix, StringComparison.Ordinal));
            return Uri.UnescapeDataString(line[LinkPrefix.Length..].Trim());
        }

        /// <summary>Requests a reset for the user and returns the raw token parsed from the newest reset mail.</summary>
        public static async Task<string> IssueTokenAsync(ApiFactory f, string userName, string email)
        {
            var r = await ForgotAsync(f, userName);
            Assert.Equal(HttpStatusCode.Accepted, r.StatusCode);
            var mails = await MailsToAsync(f, email);
            return TokenFrom(mails.Last(m => m.Subject == "Reset your password"));
        }

        public static async Task<T> DbAsync<T>(ApiFactory f, Func<ApplicationDbContext, Task<T>> action)
        {
            using var scope = f.Services.CreateScope();
            return await action(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
        }

        public static Task<List<PasswordResetToken>> TokensOfAsync(ApiFactory f, string userName) =>
            DbAsync(f, db => db.PasswordResetTokens.AsNoTracking()
                .Where(t => t.User!.UserName == userName).OrderBy(t => t.Id).ToListAsync());

        public static Task<List<RefreshToken>> RefreshTokensOfAsync(ApiFactory f, string userName) =>
            DbAsync(f, db => db.RefreshTokens.AsNoTracking()
                .Where(t => t.User!.UserName == userName).OrderBy(t => t.Id).ToListAsync());

        public static Task<string> PasswordHashOfAsync(ApiFactory f, string userName) =>
            DbAsync(f, db => db.Users.Where(u => u.UserName == userName).Select(u => u.PasswordHash).SingleAsync());

        public static async Task<JsonElement> ErrorsAsync(HttpResponseMessage r) =>
            (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
    }
}
