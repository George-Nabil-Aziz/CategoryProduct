using System.Net;
using CategoryProduct.MVC.Services;

namespace CategoryProduct.MVC.Tests;

public class ChangePasswordPageTests : MvcTestBase
{
    private const string Page = "/Account/ChangePassword";
    private const string ApiPath = "api/account/change-password";

    public ChangePasswordPageTests(MvcFactory factory) : base(factory) { }

    private Task<HttpResponseMessage> Submit(string current = "old-password-1", string next = "new-password-1", string? confirm = null) =>
        PostFormAsync(Page, new Dictionary<string, string>
        {
            ["CurrentPassword"] = current,
            ["NewPassword"] = next,
            ["ConfirmNewPassword"] = confirm ?? next
        });

    [Fact]
    public async Task Anonymous_is_redirected_to_login_without_api_calls()
    {
        using var resp = await GetAsync(Page);

        AssertLoginRedirect(resp, Page);
        Assert.Empty(Fake.Requests);
    }

    [Fact]
    public async Task Get_renders_three_password_fields()
    {
        await SignInUserAsync();

        var html = await GetHtmlAsync(Page);

        Assert.True(HtmlHelpers.FormAction(html, Page));
        Assert.Contains("name=\"CurrentPassword\"", html);
        Assert.Contains("name=\"NewPassword\"", html);
        Assert.Contains("name=\"ConfirmNewPassword\"", html);
        Assert.Contains("autocomplete=\"current-password\"", html);
    }

    [Fact]
    public async Task Post_valid_sends_bearer_replaces_the_session_tokens_and_redirects_to_profile()
    {
        await SignInUserAsync("alice", token: "tok-old", refresh: "r-old");
        Fake.On(HttpMethod.Post, ApiPath, FakeResponse.Json(200, ApiJson.Login("User", token: "tok-new", refresh: "r-new")));
        Fake.On(HttpMethod.Get, "api/account/me", FakeResponse.Json(200, ApiJson.Profile(1, "alice", "User", null)));

        var resp = await Submit();

        AssertRedirect(resp, "/Account/Profile");
        var call = Fake.Single(HttpMethod.Post, ApiPath);
        Assert.Equal("Bearer tok-old", call.Authorization);
        Assert.Equal("old-password-1", call.Json().GetProperty("currentPassword").GetString());
        Assert.Equal("new-password-1", call.Json().GetProperty("newPassword").GetString());

        var html = await (await FollowAsync(resp)).Content.ReadAsStringAsync();
        Assert.Equal("Password changed.", HtmlHelpers.Alert(html, "success"));
        Assert.Equal("Bearer tok-new", Fake.Single(HttpMethod.Get, "api/account/me").Authorization);
    }

    [Fact]
    public async Task Post_api_400_shows_errors_at_their_fields()
    {
        await SignInUserAsync();
        Fake.On(HttpMethod.Post, ApiPath, FakeResponse.Json(400, ApiJson.ValidationProblem(new Dictionary<string, string[]>
        {
            ["CurrentPassword"] = ["Current password is incorrect."],
            ["NewPassword"] = ["New password must differ from the current password."]
        })));

        var resp = await Submit();

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var html = await resp.Content.ReadAsStringAsync();
        Assert.Equal("Current password is incorrect.", HtmlHelpers.FieldError(html, "CurrentPassword"));
        Assert.Equal("New password must differ from the current password.", HtmlHelpers.FieldError(html, "NewPassword"));
    }

    [Fact]
    public async Task Post_api_429_shows_the_rate_limit_message()
    {
        await SignInUserAsync();
        Fake.On(HttpMethod.Post, ApiPath, FakeResponse.Status(429));

        var resp = await Submit();

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(ApiRateLimit.Message, HtmlHelpers.FieldError(await resp.Content.ReadAsStringAsync(), ""));
    }

    [Fact]
    public async Task Post_401_with_failed_refresh_signs_out_and_redirects_to_login()
    {
        await SignInUserAsync(token: "tok-old", refresh: "r1");
        Fake.On(HttpMethod.Post, ApiPath, FakeResponse.Status(401));
        Fake.On(HttpMethod.Post, "api/account/refresh", FakeResponse.Status(401));

        var resp = await Submit();

        AssertLoginRedirect(resp, Page);
    }

    [Fact]
    public async Task Post_mismatched_confirmation_rerenders_without_an_api_call()
    {
        await SignInUserAsync();

        var resp = await Submit(confirm: "something-else-9");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("Passwords do not match.", HtmlHelpers.FieldError(await resp.Content.ReadAsStringAsync(), "ConfirmNewPassword"));
        Assert.Empty(Fake.Calls(HttpMethod.Post, ApiPath));
    }
}
