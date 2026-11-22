using System.Net;
using System.Net.Http.Json;
using static CategoryProduct.API.Tests.PasswordTestSupport;

namespace CategoryProduct.API.Tests
{
    public class PasswordRateLimitTests
    {
        public class LimitedFactory : ApiFactory
        {
            protected override int ForgotPasswordPermitLimit => 2;
            protected override int PasswordPermitLimit => 2;
        }

        [Fact]
        public async Task ForgotPassword_ThirdRequest_Returns429()
        {
            using var f = new LimitedFactory();
            for (var i = 0; i < 2; i++)
                Assert.Equal(HttpStatusCode.Accepted, (await ForgotAsync(f, "nobody")).StatusCode);
            Assert.Equal(HttpStatusCode.TooManyRequests, (await ForgotAsync(f, "nobody")).StatusCode);
        }

        [Fact]
        public async Task ResetPassword_ThirdRequest_Returns429()
        {
            using var f = new LimitedFactory();
            for (var i = 0; i < 2; i++)
                Assert.Equal(HttpStatusCode.BadRequest, (await ResetAsync(f, "bad", NewPassword)).StatusCode);
            Assert.Equal(HttpStatusCode.TooManyRequests, (await ResetAsync(f, "bad", NewPassword)).StatusCode);
        }

        [Fact]
        public async Task ChangePassword_ThirdRequest_Returns429_AndSharesTheCounterWithReset()
        {
            using var f = new LimitedFactory();
            var c = await AuthHelper.LoggedInAsync(f, ApiFactory.UserName);

            Assert.Equal(HttpStatusCode.BadRequest, (await ResetAsync(f, "bad", NewPassword)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,
                (await c.PostAsJsonAsync("api/account/change-password",
                    new { currentPassword = "wrong", newPassword = NewPassword })).StatusCode);
            Assert.Equal(HttpStatusCode.TooManyRequests,
                (await c.PostAsJsonAsync("api/account/change-password",
                    new { currentPassword = "wrong", newPassword = NewPassword })).StatusCode);
        }
    }
}
