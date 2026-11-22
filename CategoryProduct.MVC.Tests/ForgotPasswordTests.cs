using System.Net;
using CategoryProduct.MVC.Services;

namespace CategoryProduct.MVC.Tests;

public class ForgotPasswordTests : MvcTestBase
{
    private const string Page = "/Account/ForgotPassword";
    private const string Confirmation = "/Account/ForgotPasswordConfirmation";
    private const string ApiPath = "api/account/forgot-password";

    public ForgotPasswordTests(MvcFactory factory) : base(factory) { }

    private Task<HttpResponseMessage> Submit(string input) =>
        PostFormAsync(Page, new Dictionary<string, string> { ["UserNameOrEmail"] = input });

    [Fact]
    public async Task Get_shows_the_form_for_anonymous_users()
    {
        var html = await GetHtmlAsync(Page);

        Assert.True(HtmlHelpers.FormAction(html, Page));
        Assert.Contains("name=\"UserNameOrEmail\"", html);
        Assert.Empty(Fake.Requests);
    }

    [Fact]
    public async Task Post_api_202_redirects_to_the_confirmation_and_sends_the_input()
    {
        Fake.On(HttpMethod.Post, ApiPath, FakeResponse.Status(202));

        var resp = await Submit("  alice  ");

        AssertRedirect(resp, Confirmation);
        var call = Fake.Single(HttpMethod.Post, ApiPath);
        Assert.Equal("alice", call.Json().GetProperty("userNameOrEmail").GetString());
        Assert.Null(call.Authorization);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(204)]
    [InlineData(404)]
    public async Task Post_other_non_error_outcomes_still_redirect_to_the_uniform_confirmation(int status)
    {
        Fake.On(HttpMethod.Post, ApiPath, FakeResponse.Status(status));

        var resp = await Submit("alice");

        AssertRedirect(resp, Confirmation);
    }

    [Fact]
    public async Task Post_api_429_shows_the_rate_limit_message()
    {
        Fake.On(HttpMethod.Post, ApiPath, FakeResponse.Status(429));

        var resp = await Submit("alice");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(ApiRateLimit.Message, HtmlHelpers.FieldError(await resp.Content.ReadAsStringAsync(), ""));
    }

    [Fact]
    public async Task Post_api_400_shows_a_field_error()
    {
        Fake.On(HttpMethod.Post, ApiPath,
            FakeResponse.Json(400, ApiJson.ValidationProblem("UserNameOrEmail", "The field is too long.")));

        var resp = await Submit("alice");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("The field is too long.", HtmlHelpers.FieldError(await resp.Content.ReadAsStringAsync(), "UserNameOrEmail"));
    }

    [Fact]
    public async Task Post_empty_input_rerenders_with_an_error_and_makes_no_api_call()
    {
        var resp = await Submit("");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.NotNull(HtmlHelpers.FieldError(await resp.Content.ReadAsStringAsync(), "UserNameOrEmail"));
        Assert.Empty(Fake.Calls(HttpMethod.Post, ApiPath));
    }

    [Fact]
    public async Task Confirmation_text_is_identical_for_different_inputs()
    {
        Fake.On(HttpMethod.Post, ApiPath, FakeResponse.Status(202));

        var first = await FollowAsync(await Submit("alice"));
        var firstHtml = await first.Content.ReadAsStringAsync();
        var second = await FollowAsync(await Submit("nobody@example.com"));
        var secondHtml = await second.Content.ReadAsStringAsync();

        const string text = "If an account with that name or email exists and has an email address, a reset link was sent.";
        Assert.True(HtmlHelpers.HasText(firstHtml, text));
        Assert.True(HtmlHelpers.HasText(secondHtml, text));
        Assert.DoesNotContain("alice", firstHtml.Replace("Hello", ""), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Post_requires_the_antiforgery_token()
    {
        using var resp = await PostFormNoTokenAsync(Page, new Dictionary<string, string> { ["UserNameOrEmail"] = "alice" });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Empty(Fake.Requests);
    }
}
