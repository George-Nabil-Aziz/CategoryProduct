using System.Net;
using System.Text.RegularExpressions;

namespace CategoryProduct.MVC.Tests;

public class AccountTests : MvcTestBase
{
    public AccountTests(MvcFactory factory) : base(factory) { }

    private const string Home = "/";

    private void ScriptHome() =>
        Fake.On(HttpMethod.Get, "api/category", FakeResponse.Json(200, ApiJson.Paged(new[] { ApiJson.Category(1, "Books") })));

    private static Dictionary<string, string> Creds(string user = "alice", string pw = "P@ssw0rd!") =>
        new() { ["UserName"] = user, ["Password"] = pw };

    private static Dictionary<string, string> Reg(string user = "bob", string pw = "P@ssw0rd!", string? confirm = null) =>
        new() { ["UserName"] = user, ["Password"] = pw, ["ConfirmPassword"] = confirm ?? pw };

    private static bool HasAuthCookie(HttpResponseMessage r) =>
        r.Headers.TryGetValues("Set-Cookie", out var v) &&
        v.Any(c => c.StartsWith(".AspNetCore.Cookies=", StringComparison.Ordinal) && !c.StartsWith(".AspNetCore.Cookies=;", StringComparison.Ordinal));

    [Fact]
    public async Task Login_get_has_token_and_keeps_returnUrl_in_form_action()
    {
        var html = await GetHtmlAsync("/Account/Login?returnUrl=%2FProducts");
        Assert.False(string.IsNullOrEmpty(HtmlHelpers.ExtractAntiforgeryToken(html)));
        var decoded = HtmlHelpers.Decode(html);
        Assert.Matches("action=\"/Account/Login\\?returnUrl=(%2F|/)Products\"", decoded);
    }

    [Fact]
    public async Task Login_ok_user_sets_cookie_posts_credentials_without_authorization_and_greets()
    {
        ScriptLogin("User");
        ScriptHome();
        var resp = await PostFormAsync("/Account/Login", Creds("alice"));
        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
        Assert.True(HasAuthCookie(resp));
        AssertRedirect(resp, Home);

        var call = Fake.Single(HttpMethod.Post, "api/account/login");
        Assert.Null(call.Authorization);
        Assert.Equal("alice", call.Json().GetProperty("userName").GetString());
        Assert.Equal("P@ssw0rd!", call.Json().GetProperty("password").GetString());

        var html = await GetHtmlAsync("/Categories");
        Assert.True(HtmlHelpers.HasText(html, "Hello, alice"));
        Assert.False(HtmlHelpers.HasText(html, "Manage Orders"));
    }

    [Fact]
    public async Task Login_ok_admin_layout_shows_admin_links()
    {
        ScriptLogin("Admin");
        ScriptHome();
        var resp = await PostFormAsync("/Account/Login", Creds("root"));
        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
        var html = await GetHtmlAsync("/Categories");
        Assert.True(HtmlHelpers.HasText(html, "Hello, root"));
        Assert.Contains("/Users", html);
        Assert.Contains("/Inventory", html);
        Assert.True(HtmlHelpers.HasText(html, "Manage Orders"));
    }

    [Fact]
    public async Task Login_local_returnUrl_is_followed()
    {
        ScriptLogin();
        var resp = await PostFormAsync("/Account/Login?returnUrl=%2FProducts", Creds());
        AssertRedirect(resp, "/Products");
    }

    [Fact]
    public async Task Login_external_returnUrl_is_ignored()
    {
        ScriptLogin();
        var resp = await PostFormAsync("/Account/Login?returnUrl=https%3A%2F%2Fevil.test%2F", Creds());
        AssertRedirect(resp, Home);
    }

    [Fact]
    public async Task Login_api_401_shows_invalid_credentials_and_no_cookie()
    {
        Fake.On(HttpMethod.Post, "api/account/login", FakeResponse.Status(401));
        var resp = await PostFormAsync("/Account/Login", Creds());
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.False(HasAuthCookie(resp));
        Assert.True(HtmlHelpers.HasText(await resp.Content.ReadAsStringAsync(), "Invalid username or password."));
    }

    [Fact]
    public async Task Login_api_429_shows_too_many_attempts()
    {
        Fake.On(HttpMethod.Post, "api/account/login", FakeResponse.Status(429));
        var resp = await PostFormAsync("/Account/Login", Creds());
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.False(HasAuthCookie(resp));
        Assert.True(HtmlHelpers.HasText(await resp.Content.ReadAsStringAsync(), "Too many login attempts. Try again in a minute."));
    }

    [Fact]
    public async Task Login_empty_fields_fail_validation_without_calling_api()
    {
        var resp = await PostFormAsync("/Account/Login", Creds("", ""));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var html = await resp.Content.ReadAsStringAsync();
        Assert.NotNull(HtmlHelpers.FieldError(html, "UserName"));
        Assert.NotNull(HtmlHelpers.FieldError(html, "Password"));
        Assert.Empty(Fake.Requests);
    }

    [Fact]
    public async Task Login_without_antiforgery_token_is_400()
    {
        var resp = await PostFormNoTokenAsync("/Account/Login", Creds());
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Empty(Fake.Requests);
    }

    [Fact]
    public async Task Register_get_renders_form()
    {
        var html = await GetHtmlAsync("/Account/Register");
        Assert.True(HtmlHelpers.FormAction(html, "/Account/Register"));
        Assert.Contains("name=\"ConfirmPassword\"", html);
    }

    [Fact]
    public async Task Register_ok_registers_then_logs_in_in_order()
    {
        Fake.On(HttpMethod.Post, "api/account/register", FakeResponse.Status(201));
        ScriptLogin("User");
        var resp = await PostFormAsync("/Account/Register", Reg("bob"));
        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
        Assert.True(HasAuthCookie(resp));
        AssertRedirect(resp, Home);

        var order = Fake.Requests.Select(r => r.Path).ToList();
        Assert.Equal(new[] { "api/account/register", "api/account/login" }, order);
        var reg = Fake.Single(HttpMethod.Post, "api/account/register");
        Assert.Null(reg.Authorization);
        Assert.Equal("bob", reg.Json().GetProperty("userName").GetString());
        Assert.Equal("P@ssw0rd!", reg.Json().GetProperty("password").GetString());
        Assert.Equal("bob", Fake.Single(HttpMethod.Post, "api/account/login").Json().GetProperty("userName").GetString());
    }

    [Fact]
    public async Task Register_ok_but_login_401_redirects_to_login()
    {
        Fake.On(HttpMethod.Post, "api/account/register", FakeResponse.Status(201));
        Fake.On(HttpMethod.Post, "api/account/login", FakeResponse.Status(401));
        var resp = await PostFormAsync("/Account/Register", Reg());
        AssertRedirect(resp, "/Account/Login");
        Assert.False(HasAuthCookie(resp));
    }

    [Fact]
    public async Task Register_api_409_shows_username_taken()
    {
        Fake.On(HttpMethod.Post, "api/account/register", FakeResponse.Json(409, ApiJson.ConflictProblem("exists")));
        var resp = await PostFormAsync("/Account/Register", Reg());
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("Username already taken.", HtmlHelpers.FieldError(await resp.Content.ReadAsStringAsync(), "UserName"));
    }

    [Fact]
    public async Task Register_api_429_shows_too_many_attempts()
    {
        Fake.On(HttpMethod.Post, "api/account/register", FakeResponse.Status(429));
        var resp = await PostFormAsync("/Account/Register", Reg());
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.True(HtmlHelpers.HasText(await resp.Content.ReadAsStringAsync(), "Too many attempts. Try again in a minute."));
    }

    [Fact]
    public async Task Register_api_400_field_errors_are_bound_to_fields()
    {
        Fake.On(HttpMethod.Post, "api/account/register", FakeResponse.Json(400, ApiJson.ValidationProblem(
            new Dictionary<string, string[]>
            {
                ["UserName"] = new[] { "Name is reserved." },
                ["Password"] = new[] { "Password too weak." }
            })));
        var resp = await PostFormAsync("/Account/Register", Reg());
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var html = await resp.Content.ReadAsStringAsync();
        Assert.Equal("Name is reserved.", HtmlHelpers.FieldError(html, "UserName"));
        Assert.Equal("Password too weak.", HtmlHelpers.FieldError(html, "Password"));
    }

    [Fact]
    public async Task Register_api_400_other_key_shows_in_summary()
    {
        Fake.On(HttpMethod.Post, "api/account/register", FakeResponse.Json(400, ApiJson.ValidationProblem("Other", "Something else is wrong.")));
        var resp = await PostFormAsync("/Account/Register", Reg());
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("Something else is wrong.", HtmlHelpers.FieldError(await resp.Content.ReadAsStringAsync(), "") ?? "");
    }

    [Theory]
    [InlineData("bob", "P@ssw0rd!", "different1", "ConfirmPassword")]
    [InlineData("bob", "short", "short", "Password")]
    [InlineData("b!d name", "P@ssw0rd!", "P@ssw0rd!", "UserName")]
    public async Task Register_invalid_model_rerenders_without_calling_api(string user, string pw, string confirm, string field)
    {
        var resp = await PostFormAsync("/Account/Register", Reg(user, pw, confirm));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.NotNull(HtmlHelpers.FieldError(await resp.Content.ReadAsStringAsync(), field));
        Assert.Empty(Fake.Requests);
    }

    // ---------- optional email on register (024-email-notifications) ----------

    private static Dictionary<string, string> RegEmail(string email)
    {
        var f = Reg("bob");
        f["Email"] = email;
        return f;
    }

    [Fact]
    public async Task Register_get_has_optional_email_input()
    {
        var html = await GetHtmlAsync("/Account/Register");
        Assert.Contains("name=\"Email\"", html);
        Assert.Contains("type=\"email\"", html);
        Assert.Contains("autocomplete=\"email\"", html);
        Assert.True(HtmlHelpers.HasText(html, "Email (optional)"));
    }

    [Fact]
    public async Task Register_valid_email_is_sent_to_the_api()
    {
        Fake.On(HttpMethod.Post, "api/account/register", FakeResponse.Status(201));
        ScriptLogin("User");
        var resp = await PostFormAsync("/Account/Register", RegEmail("  bob@example.com "));
        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);

        var reg = Fake.Single(HttpMethod.Post, "api/account/register");
        Assert.Equal("bob@example.com", reg.Json().GetProperty("email").GetString());
        Assert.Equal("bob", reg.Json().GetProperty("userName").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Register_empty_email_is_omitted_or_null(string email)
    {
        Fake.On(HttpMethod.Post, "api/account/register", FakeResponse.Status(201));
        ScriptLogin("User");
        await PostFormAsync("/Account/Register", RegEmail(email));

        var json = Fake.Single(HttpMethod.Post, "api/account/register").Json();
        Assert.True(!json.TryGetProperty("email", out var value) || value.ValueKind == System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task Register_without_email_field_still_works()
    {
        Fake.On(HttpMethod.Post, "api/account/register", FakeResponse.Status(201));
        ScriptLogin("User");
        var resp = await PostFormAsync("/Account/Register", Reg("bob"));
        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
    }

    [Fact]
    public async Task Register_api_400_email_error_shows_on_the_email_field()
    {
        Fake.On(HttpMethod.Post, "api/account/register",
            FakeResponse.Json(400, ApiJson.ValidationProblem("Email", "Email is not a valid address.")));
        var resp = await PostFormAsync("/Account/Register", RegEmail("a@b"));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var html = await resp.Content.ReadAsStringAsync();
        Assert.Equal("Email is not a valid address.", HtmlHelpers.FieldError(html, "Email"));
        Assert.Null(HtmlHelpers.FieldError(html, "UserName"));
    }

    [Fact]
    public async Task Register_api_409_email_in_use_shows_on_the_email_field_not_the_username()
    {
        Fake.On(HttpMethod.Post, "api/account/register",
            FakeResponse.Json(409, ApiJson.ConflictProblem("Email already in use.")));
        var resp = await PostFormAsync("/Account/Register", RegEmail("taken@example.com"));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var html = await resp.Content.ReadAsStringAsync();
        Assert.Equal("Email already in use.", HtmlHelpers.FieldError(html, "Email"));
        Assert.Null(HtmlHelpers.FieldError(html, "UserName"));
    }

    [Fact]
    public async Task Register_invalid_email_rerenders_without_calling_api()
    {
        var resp = await PostFormAsync("/Account/Register", RegEmail("not an email"));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.NotNull(HtmlHelpers.FieldError(await resp.Content.ReadAsStringAsync(), "Email"));
        Assert.Empty(Fake.Requests);
    }

    private Task<HttpResponseMessage> LogoutAsync() =>
        PostFormAsync("/Account/Logout", new Dictionary<string, string>(), tokenPageUrl: "/Account/AccessDenied");

    private async Task AssertSignedOutAsync()
    {
        var html = await GetHtmlAsync("/Account/AccessDenied");
        Assert.False(HtmlHelpers.HasText(html, "Hello,"));
    }

    [Fact]
    public async Task Logout_clears_cookie_and_revokes_refresh_token()
    {
        await SignInUserAsync("alice", token: "tok-1", refresh: "refresh-xyz");
        Fake.On(HttpMethod.Post, "api/account/logout", FakeResponse.Status(204));
        var resp = await LogoutAsync();
        AssertRedirect(resp, Home);
        var call = Fake.Single(HttpMethod.Post, "api/account/logout");
        Assert.Equal("Bearer tok-1", call.Authorization);
        Assert.Equal("refresh-xyz", call.Json().GetProperty("refreshToken").GetString());
        await AssertSignedOutAsync();
    }

    [Fact]
    public async Task Logout_when_api_unreachable_still_signs_out()
    {
        await SignInUserAsync();
        Fake.Throw(HttpMethod.Post, "api/account/logout", new HttpRequestException("down"));
        var resp = await LogoutAsync();
        AssertRedirect(resp, Home);
        await AssertSignedOutAsync();
    }

    [Fact]
    public async Task Logout_when_api_500_still_signs_out()
    {
        await SignInUserAsync();
        Fake.On(HttpMethod.Post, "api/account/logout", FakeResponse.Status(500));
        var resp = await LogoutAsync();
        AssertRedirect(resp, Home);
        await AssertSignedOutAsync();
    }

    [Fact]
    public async Task AccessDenied_renders_for_user()
    {
        await SignInUserAsync();
        using var r = await GetAsync("/Account/AccessDenied");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Login_cookie_lifetime_follows_refresh_token_expiry()
    {
        // The cookie is a session cookie (no expires attribute); its ticket expiry is the refresh token expiry.
        // A refresh token that is already expired therefore yields a ticket the next request rejects.
        var now = DateTime.UtcNow;
        Fake.On(HttpMethod.Post, "api/account/login", FakeResponse.Json(200, new
        {
            token = "t", expiresAtUtc = now.AddMinutes(15), role = "User",
            refreshToken = "r", refreshTokenExpiresAtUtc = now.AddMinutes(-1)
        }));
        var resp = await PostFormAsync("/Account/Login", Creds());
        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
        Assert.DoesNotContain(resp.Headers.GetValues("Set-Cookie"), c => Regex.IsMatch(c, "expires=", RegexOptions.IgnoreCase));
        var next = await GetAsync("/Cart");
        AssertLoginRedirect(next, "/Cart");
    }

    [Fact]
    public async Task Logout_with_expiring_token_does_not_refresh_and_uses_old_bearer_and_refresh_token()
    {
        await SignInUserAsync("alice", expiresInSeconds: 30, token: "tok-old", refresh: "r-old");
        Fake.On(HttpMethod.Post, "api/account/refresh", FakeResponse.Json(200, ApiJson.Login("User", 900, "tok-new", "r-new")));
        Fake.On(HttpMethod.Post, "api/account/logout", FakeResponse.Status(204));
        var resp = await LogoutAsync();
        AssertRedirect(resp, Home);
        Assert.Empty(Fake.Calls(HttpMethod.Post, "api/account/refresh"));
        var call = Fake.Single(HttpMethod.Post, "api/account/logout");
        Assert.Equal("Bearer tok-old", call.Authorization);
        Assert.Equal("r-old", call.Json().GetProperty("refreshToken").GetString());
        await AssertSignedOutAsync();
    }

    [Fact]
    public async Task Logout_api_401_does_not_refresh_or_retry_and_still_signs_out()
    {
        await SignInUserAsync("alice", token: "tok-old", refresh: "r-old");
        Fake.On(HttpMethod.Post, "api/account/refresh", FakeResponse.Json(200, ApiJson.Login("User", 900, "tok-new", "r-new")));
        Fake.On(HttpMethod.Post, "api/account/logout", FakeResponse.Status(401));
        var resp = await LogoutAsync();
        AssertRedirect(resp, Home);
        Assert.Single(Fake.Calls(HttpMethod.Post, "api/account/logout"));
        Assert.Empty(Fake.Calls(HttpMethod.Post, "api/account/refresh"));
        await AssertSignedOutAsync();
    }

    [Fact]
    public async Task Logout_with_empty_refresh_token_makes_no_api_call_and_signs_out()
    {
        Fake.On(HttpMethod.Post, "api/account/login", FakeResponse.Json(200, ApiJson.Login("User", 900, "tok-1", "")));
        var login = await PostFormAsync("/Account/Login",
            new Dictionary<string, string> { ["UserName"] = "alice", ["Password"] = "P@ssw0rd!" });
        Assert.Equal(HttpStatusCode.Found, login.StatusCode);
        Fake.ClearRequests();
        Fake.On(HttpMethod.Post, "api/account/logout", FakeResponse.Status(204));
        var resp = await LogoutAsync();
        AssertRedirect(resp, Home);
        Assert.Empty(Fake.Calls(HttpMethod.Post, "api/account/logout"));
        await AssertSignedOutAsync();
    }
}
