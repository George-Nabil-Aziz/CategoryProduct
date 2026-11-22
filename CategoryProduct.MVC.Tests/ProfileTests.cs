using System.Net;
using CategoryProduct.MVC.Services;

namespace CategoryProduct.MVC.Tests;

public class ProfileTests : MvcTestBase
{
    private const string Page = "/Account/Profile";

    public ProfileTests(MvcFactory factory) : base(factory) { }

    private void ScriptMe(string? email = "alice@example.com", string name = "alice") =>
        Fake.On(HttpMethod.Get, "api/account/me", FakeResponse.Json(200, ApiJson.Profile(1, name, "User", email)));

    private Task<HttpResponseMessage> Save(string? email) =>
        PostFormAsync(Page, new Dictionary<string, string> { ["Email"] = email ?? "" }, tokenPageUrl: Page);

    [Fact]
    public async Task Anonymous_is_redirected_to_login_without_api_calls()
    {
        using var resp = await GetAsync(Page);
        AssertLoginRedirect(resp, Page);
        Assert.Empty(Fake.Requests);
    }

    [Fact]
    public async Task Get_shows_current_email_and_calls_me_with_bearer()
    {
        await SignInUserAsync("alice", token: "tok-a");
        ScriptMe("alice@example.com");

        var html = await GetHtmlAsync(Page);

        var call = Fake.Single(HttpMethod.Get, "api/account/me");
        Assert.Equal("Bearer tok-a", call.Authorization);
        Assert.Contains("value=\"alice@example.com\"", html);
        Assert.True(HtmlHelpers.FormAction(html, "/Account/Profile"));
        Assert.True(HtmlHelpers.HasText(html, "alice"));
        Assert.True(HtmlHelpers.HasText(html, "User"));
    }

    [Fact]
    public async Task Get_without_email_shows_an_empty_field()
    {
        await SignInUserAsync();
        ScriptMe(email: null);

        var html = await GetHtmlAsync(Page);

        Assert.DoesNotContain("value=\"alice@example.com\"", html);
        Assert.Contains("name=\"Email\"", html);
    }

    [Fact]
    public async Task Layout_shows_a_profile_link_for_signed_in_users_only()
    {
        Fake.On(HttpMethod.Get, "api/category", FakeResponse.Json(200, ApiJson.Paged(new[] { ApiJson.Category(1, "Books") })));
        var anonymous = await GetHtmlAsync("/Categories");
        Assert.DoesNotContain("/Account/Profile", anonymous);

        await SignInUserAsync();
        var signedIn = await GetHtmlAsync("/Categories");
        Assert.Contains("/Account/Profile", signedIn);
    }

    [Fact]
    public async Task Post_success_sends_json_redirects_and_shows_saved_message()
    {
        await SignInUserAsync(token: "tok-a");
        ScriptMe();
        Fake.On(HttpMethod.Put, "api/account/email", FakeResponse.Json(200, ApiJson.Profile(1, "alice", "User", "new@example.com")));

        var resp = await Save("  new@example.com ");

        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
        AssertRedirect(resp, Page);
        var put = Fake.Single(HttpMethod.Put, "api/account/email");
        Assert.Equal("Bearer tok-a", put.Authorization);
        Assert.Equal("new@example.com", put.Json().GetProperty("email").GetString());

        var html = await (await FollowAsync(resp)).Content.ReadAsStringAsync();
        Assert.Equal("Email saved.", HtmlHelpers.Alert(html, "success"));
    }

    [Fact]
    public async Task Post_empty_clears_the_email()
    {
        await SignInUserAsync();
        ScriptMe();
        Fake.On(HttpMethod.Put, "api/account/email", FakeResponse.Json(200, ApiJson.Profile(1, "alice", "User", null)));

        var resp = await Save("");

        AssertRedirect(resp, Page);
        Assert.Equal(System.Text.Json.JsonValueKind.Null,
            Fake.Single(HttpMethod.Put, "api/account/email").Json().GetProperty("email").ValueKind);
        var html = await (await FollowAsync(resp)).Content.ReadAsStringAsync();
        Assert.Equal("Email removed.", HtmlHelpers.Alert(html, "success"));
    }

    [Fact]
    public async Task Post_api_400_rerenders_with_field_error()
    {
        await SignInUserAsync();
        ScriptMe();
        Fake.On(HttpMethod.Put, "api/account/email",
            FakeResponse.Json(400, ApiJson.ValidationProblem("Email", "Email is not a valid address.")));

        var resp = await Save("a@b");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var html = await resp.Content.ReadAsStringAsync();
        Assert.Equal("Email is not a valid address.", HtmlHelpers.FieldError(html, "Email"));
        Assert.Contains("value=\"a@b\"", html);
    }

    [Fact]
    public async Task Post_api_409_rerenders_with_field_error()
    {
        await SignInUserAsync();
        ScriptMe();
        Fake.On(HttpMethod.Put, "api/account/email", FakeResponse.Json(409, ApiJson.ConflictProblem("Email already in use.")));

        var resp = await Save("taken@example.com");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("Email already in use.", HtmlHelpers.FieldError(await resp.Content.ReadAsStringAsync(), "Email"));
    }

    [Fact]
    public async Task Post_client_side_invalid_value_rerenders_without_calling_the_api()
    {
        await SignInUserAsync();
        ScriptMe();

        var resp = await Save("definitely not an email");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.NotNull(HtmlHelpers.FieldError(await resp.Content.ReadAsStringAsync(), "Email"));
        Assert.Empty(Fake.Calls(HttpMethod.Put, "api/account/email"));
    }

    [Fact]
    public async Task Post_api_429_shows_the_rate_limit_message()
    {
        await SignInUserAsync();
        ScriptMe();
        Fake.On(HttpMethod.Put, "api/account/email", FakeResponse.Status(429));

        var resp = await Save("a@example.com");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(ApiRateLimit.Message, HtmlHelpers.FieldError(await resp.Content.ReadAsStringAsync(), ""));
    }

    [Fact]
    public async Task Get_api_429_shows_the_too_many_requests_page()
    {
        await SignInUserAsync();
        Fake.On(HttpMethod.Get, "api/account/me", FakeResponse.Status(429));

        using var resp = await GetAsync(Page);

        Assert.Equal(HttpStatusCode.TooManyRequests, resp.StatusCode);
        Assert.True(HtmlHelpers.HasText(await resp.Content.ReadAsStringAsync(), ApiRateLimit.Message));
    }

    [Fact]
    public async Task Get_401_with_failed_refresh_signs_out_and_redirects_to_login()
    {
        await SignInUserAsync(token: "tok-old", refresh: "r1");
        Fake.On(HttpMethod.Get, "api/account/me", FakeResponse.Status(401));
        Fake.On(HttpMethod.Post, "api/account/refresh", FakeResponse.Status(401));

        using var resp = await GetAsync(Page);

        AssertLoginRedirect(resp, Page);
        Fake.ClearRequests();
        AssertLoginRedirect(await GetAsync(Page), Page); // cookie cleared: anonymous now
        Assert.Empty(Fake.Requests);
    }

    [Fact]
    public async Task Post_401_with_failed_refresh_signs_out_and_redirects_to_login()
    {
        await SignInUserAsync(token: "tok-old", refresh: "r1");
        ScriptMe();
        Fake.On(HttpMethod.Put, "api/account/email", FakeResponse.Status(401));
        Fake.On(HttpMethod.Post, "api/account/refresh", FakeResponse.Status(401));

        var resp = await Save("a@example.com");

        AssertLoginRedirect(resp, Page);
    }

    [Fact]
    public async Task Post_requires_the_antiforgery_token()
    {
        await SignInUserAsync();
        ScriptMe();

        using var resp = await PostFormNoTokenAsync(Page, new Dictionary<string, string> { ["Email"] = "a@example.com" });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Empty(Fake.Calls(HttpMethod.Put, "api/account/email"));
    }
}
