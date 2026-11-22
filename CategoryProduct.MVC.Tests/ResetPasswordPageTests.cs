using System.Net;
using CategoryProduct.MVC.Services;

namespace CategoryProduct.MVC.Tests;

public class ResetPasswordPageTests : MvcTestBase
{
    private const string Token = "tok_Reset-123";
    private const string Page = "/Account/ResetPassword";
    private const string PageWithToken = "/Account/ResetPassword?token=" + Token;
    private const string ApiPath = "api/account/reset-password";
    private const string InvalidMessage = "This reset link is invalid or has expired.";

    public ResetPasswordPageTests(MvcFactory factory) : base(factory) { }

    private Task<HttpResponseMessage> Submit(string password = "new-password-1", string? confirm = null, string token = Token) =>
        PostFormAsync(Page, new Dictionary<string, string>
        {
            ["Token"] = token,
            ["NewPassword"] = password,
            ["ConfirmPassword"] = confirm ?? password
        }, tokenPageUrl: PageWithToken);

    [Fact]
    public async Task Get_with_token_renders_the_hidden_token_and_safe_headers()
    {
        using var resp = await GetAsync(PageWithToken);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("no-referrer", resp.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("no-store", resp.Headers.GetValues("Cache-Control").Single());
        var html = await resp.Content.ReadAsStringAsync();
        Assert.Matches($"<input[^>]*type=\"hidden\"[^>]*name=\"Token\"[^>]*value=\"{Token}\"|<input[^>]*name=\"Token\"[^>]*type=\"hidden\"[^>]*value=\"{Token}\"", html);
        Assert.True(HtmlHelpers.FormAction(html, Page));
        Assert.Contains("autocomplete=\"new-password\"", html);
        Assert.Empty(Fake.Requests);
    }

    [Theory]
    [InlineData(Page)]
    [InlineData(Page + "?token=")]
    public async Task Get_without_token_shows_the_error_and_a_link_but_no_form(string url)
    {
        var html = await GetHtmlAsync(url);

        Assert.Equal(InvalidMessage, HtmlHelpers.Alert(html, "danger"));
        Assert.Contains("/Account/ForgotPassword", html);
        Assert.False(HtmlHelpers.FormAction(html, Page));
    }

    [Fact]
    public async Task Post_valid_sends_token_and_password_and_redirects_to_login_with_the_success_message()
    {
        Fake.On(HttpMethod.Post, ApiPath, FakeResponse.Status(204));

        var resp = await Submit();

        AssertRedirect(resp, "/Account/Login");
        var call = Fake.Single(HttpMethod.Post, ApiPath);
        Assert.Equal(Token, call.Json().GetProperty("token").GetString());
        Assert.Equal("new-password-1", call.Json().GetProperty("newPassword").GetString());
        var html = await (await FollowAsync(resp)).Content.ReadAsStringAsync();
        Assert.Equal("Your password was changed. Sign in with your new password.", HtmlHelpers.Alert(html, "success"));
    }

    [Fact]
    public async Task Post_mismatched_confirmation_rerenders_without_an_api_call()
    {
        var resp = await Submit("new-password-1", "different-password");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("Passwords do not match.", HtmlHelpers.FieldError(await resp.Content.ReadAsStringAsync(), "ConfirmPassword"));
        Assert.Empty(Fake.Calls(HttpMethod.Post, ApiPath));
    }

    [Fact]
    public async Task Post_api_400_on_token_shows_the_invalid_link_state_without_echoing_the_token()
    {
        Fake.On(HttpMethod.Post, ApiPath,
            FakeResponse.Json(400, ApiJson.ValidationProblem("Token", "The reset link is invalid or has expired.")));

        var resp = await Submit();

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var html = await resp.Content.ReadAsStringAsync();
        Assert.Equal(InvalidMessage, HtmlHelpers.Alert(html, "danger"));
        Assert.Contains("/Account/ForgotPassword", html);
        Assert.DoesNotContain(Token, html);
        Assert.False(HtmlHelpers.FormAction(html, Page));
    }

    [Fact]
    public async Task Post_api_400_on_new_password_shows_a_field_error_and_keeps_the_form()
    {
        Fake.On(HttpMethod.Post, ApiPath,
            FakeResponse.Json(400, ApiJson.ValidationProblem("NewPassword", "Too weak.")));

        var resp = await Submit();

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var html = await resp.Content.ReadAsStringAsync();
        Assert.Equal("Too weak.", HtmlHelpers.FieldError(html, "NewPassword"));
        Assert.True(HtmlHelpers.FormAction(html, Page));
    }

    [Fact]
    public async Task Post_api_429_shows_the_rate_limit_message()
    {
        Fake.On(HttpMethod.Post, ApiPath, FakeResponse.Status(429));

        var resp = await Submit();

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var html = await resp.Content.ReadAsStringAsync();
        Assert.Equal(ApiRateLimit.Message, HtmlHelpers.FieldError(html, ""));
        Assert.DoesNotContain("The reset link is invalid", html);
    }

    [Fact]
    public async Task Post_sets_the_safe_headers_too()
    {
        Fake.On(HttpMethod.Post, ApiPath, FakeResponse.Status(429));

        var resp = await Submit();

        Assert.Equal("no-referrer", resp.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("no-store", resp.Headers.GetValues("Cache-Control").Single());
    }
}
