using System.Net;

namespace CategoryProduct.MVC.Tests;

public class UsersTests : MvcTestBase
{
    public UsersTests(MvcFactory factory) : base(factory) { }

    private void ScriptUsers(int page = 1, int totalPages = 1) =>
        Fake.On(HttpMethod.Get, "api/users", FakeResponse.Json(200,
            ApiJson.Paged(new[] { ApiJson.User(1, "alice"), ApiJson.User(2, "root", "Admin") }, page, 10, totalPages: totalPages)));

    private Task<HttpResponseMessage> ChangeRole(string id = "1", string role = "Admin", string page = "2", string search = "al") =>
        PostFormAsync("/Users/ChangeRole",
            new Dictionary<string, string> { ["id"] = id, ["role"] = role, ["page"] = page, ["search"] = search },
            tokenPageUrl: "/Users");

    [Fact]
    public async Task Index_admin_lists_users_with_role_selects_and_sends_bearer()
    {
        await SignInAdminAsync(token: "tok-a");
        ScriptUsers();
        var html = await GetHtmlAsync("/Users");
        Assert.True(HtmlHelpers.HasText(html, "alice"));
        Assert.True(HtmlHelpers.HasText(html, "root"));
        Assert.Contains("<select name=\"role\"", html);
        Assert.Contains("<option value=\"Admin\" selected=\"selected\">", html);
        var call = Fake.Single(HttpMethod.Get, "api/users");
        Assert.Equal("?page=1&pageSize=10&search=", call.Query);
        Assert.Equal("Bearer tok-a", call.Authorization);
    }

    [Fact]
    public async Task Index_search_and_page_reach_api_and_paging_links_keep_search()
    {
        await SignInAdminAsync();
        ScriptUsers(2, 3);
        var html = await GetHtmlAsync("/Users?page=2&search=al");
        Assert.Equal("?page=2&pageSize=10&search=al", Fake.Single(HttpMethod.Get, "api/users").Query);
        var decoded = HtmlHelpers.Decode(html);
        Assert.Contains("page=1&search=al", decoded);
        Assert.Contains("page=3&search=al", decoded);
    }

    [Fact]
    public async Task Index_paging_links_only_when_applicable()
    {
        await SignInAdminAsync();

        ScriptUsers(1, 1);
        var single = await GetHtmlAsync("/Users");
        Assert.DoesNotContain("Previous", single);
        Assert.DoesNotContain("Next", single);

        ScriptUsers(1, 3);
        var first = await GetHtmlAsync("/Users");
        Assert.DoesNotContain("Previous", first);
        Assert.Contains("Next", first);

        ScriptUsers(3, 3);
        var last = await GetHtmlAsync("/Users?page=3");
        Assert.Contains("Previous", last);
        Assert.DoesNotContain("Next", last);
    }

    [Fact]
    public async Task Index_user_is_access_denied_and_anonymous_goes_to_login_without_api_calls()
    {
        using (var anon = await GetAsync("/Users")) AssertLoginRedirect(anon, "/Users");
        Assert.Empty(Fake.Requests);

        await SignInUserAsync();
        using var denied = await GetAsync("/Users");
        Assert.True((int)denied.StatusCode is >= 300 and < 400);
        Assert.Contains("/Account/AccessDenied", denied.Headers.Location!.ToString());
        Assert.Empty(Fake.Requests);
    }

    [Fact]
    public async Task ChangeRole_ok_redirects_keeping_page_and_search_then_shows_success()
    {
        await SignInAdminAsync(token: "tok-a");
        ScriptUsers();
        Fake.On(HttpMethod.Put, "api/users/1/role", FakeResponse.Status(204));
        var resp = await ChangeRole();
        AssertRedirect(resp, "/Users?page=2&search=al");
        var call = Fake.Single(HttpMethod.Put, "api/users/1/role");
        Assert.Equal("Bearer tok-a", call.Authorization);
        Assert.Equal("Admin", call.Json().GetProperty("role").GetString());

        var html = await GetHtmlAsync("/Users?page=2&search=al");
        Assert.Equal("Role updated.", HtmlHelpers.Alert(html, "success"));
    }

    private async Task<string> ChangeRoleThenFollowAsync()
    {
        var resp = await ChangeRole();
        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
        return await GetHtmlAsync("/Users?page=2&search=al");
    }

    [Fact]
    public async Task ChangeRole_409_shows_api_detail()
    {
        await SignInAdminAsync();
        ScriptUsers();
        Fake.On(HttpMethod.Put, "api/users/1/role", FakeResponse.Json(409, ApiJson.ConflictProblem("Cannot remove the last admin.")));
        var html = await ChangeRoleThenFollowAsync();
        Assert.Equal("Cannot remove the last admin.", HtmlHelpers.Alert(html, "danger"));
    }

    [Fact]
    public async Task ChangeRole_400_shows_joined_messages()
    {
        await SignInAdminAsync();
        ScriptUsers();
        Fake.On(HttpMethod.Put, "api/users/1/role", FakeResponse.Json(400, ApiJson.ValidationProblem(
            new Dictionary<string, string[]> { ["role"] = new[] { "Role is invalid.", "Try again." } })));
        var html = await ChangeRoleThenFollowAsync();
        Assert.Equal("Role is invalid. Try again.", HtmlHelpers.Alert(html, "danger"));
    }

    [Fact]
    public async Task ChangeRole_404_shows_user_not_found()
    {
        await SignInAdminAsync();
        ScriptUsers();
        Fake.On(HttpMethod.Put, "api/users/1/role", FakeResponse.Status(404));
        var html = await ChangeRoleThenFollowAsync();
        Assert.Equal("User not found.", HtmlHelpers.Alert(html, "danger"));
    }

    [Fact]
    public async Task ChangeRole_403_shows_generic_failure()
    {
        await SignInAdminAsync();
        ScriptUsers();
        Fake.On(HttpMethod.Put, "api/users/1/role", FakeResponse.Status(403));
        var html = await ChangeRoleThenFollowAsync();
        Assert.Equal("Could not change role.", HtmlHelpers.Alert(html, "danger"));
    }
}
