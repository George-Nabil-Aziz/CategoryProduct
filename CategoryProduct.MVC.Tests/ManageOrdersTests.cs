using System.Net;
using System.Text.RegularExpressions;

namespace CategoryProduct.MVC.Tests;

public class ManageOrdersTests : MvcTestBase
{
    private const string TokenPage = "/Account/AccessDenied";

    public ManageOrdersTests(MvcFactory factory) : base(factory) { }

    private static void AssertAccessDenied(HttpResponseMessage resp)
    {
        var loc = resp.Headers.Location!;
        Assert.Equal("/Account/AccessDenied", loc.IsAbsoluteUri ? loc.AbsolutePath : loc.OriginalString.Split('?')[0], ignoreCase: true);
    }

    private void ScriptManage(int page = 1, int totalPages = 1, params object[] orders) =>
        Fake.On(HttpMethod.Get, "api/orders/all",
            FakeResponse.Json(200, ApiJson.Paged(orders, page, 10, totalPages: totalPages)));

    private static Dictionary<string, string> Fields(params (string, string)[] f) =>
        f.ToDictionary(x => x.Item1, x => x.Item2);

    [Fact]
    public async Task Manage_lists_all_users_orders_and_sends_bearer()
    {
        await SignInAdminAsync(token: "tok");
        ScriptManage(1, 1,
            ApiJson.OrderSummary(11, "Pending", userName: "bob"),
            ApiJson.OrderSummary(12, "Confirmed", userName: "carol"));

        var html = await GetHtmlAsync("/Orders/Manage");

        Assert.True(HtmlHelpers.HasText(html, "#11"));
        Assert.True(HtmlHelpers.HasText(html, "bob"));
        Assert.True(HtmlHelpers.HasText(html, "carol"));
        // only the pending order offers Confirm/Cancel
        Assert.True(HtmlHelpers.FormAction(html, "/Orders/11/Status"));
        Assert.True(HtmlHelpers.FormAction(html, "/Orders/11/Cancel"));
        Assert.False(HtmlHelpers.FormAction(html, "/Orders/12/Status"));
        var req = Fake.Single(HttpMethod.Get, "api/orders/all");
        Assert.Equal("?page=1&pageSize=10&status=", req.Query);
        Assert.Equal("Bearer tok", req.Authorization);
    }

    [Fact]
    public async Task Manage_lowercase_status_is_normalized_and_option_selected()
    {
        await SignInAdminAsync();
        ScriptManage(1, 1, ApiJson.OrderSummary(11, "Pending"));

        var html = await GetHtmlAsync("/Orders/Manage?status=pending");

        Assert.Equal("?page=1&pageSize=10&status=Pending", Fake.Single(HttpMethod.Get, "api/orders/all").Query);
        Assert.Matches(new Regex("<option value=\"Pending\"\\s+selected"), html);
        Assert.DoesNotMatch(new Regex("<option value=\"Confirmed\"\\s+selected"), html);
    }

    [Fact]
    public async Task Manage_unknown_status_is_sent_empty()
    {
        await SignInAdminAsync();
        ScriptManage();

        var html = await GetHtmlAsync("/Orders/Manage?status=bogus");

        Assert.Equal("?page=1&pageSize=10&status=", Fake.Single(HttpMethod.Get, "api/orders/all").Query);
        Assert.DoesNotMatch(new Regex("<option value=\"[A-Za-z]+\"\\s+selected"), html);
    }

    [Fact]
    public async Task Manage_paging_links_keep_the_filter()
    {
        await SignInAdminAsync();
        ScriptManage(2, 3, ApiJson.OrderSummary(11, "Confirmed"));

        var html = await GetHtmlAsync("/Orders/Manage?page=2&status=Confirmed");

        Assert.Equal("?page=2&pageSize=10&status=Confirmed", Fake.Single(HttpMethod.Get, "api/orders/all").Query);
        Assert.True(HtmlHelpers.HasText(html, "/Orders/Manage?page=1&status=Confirmed"));
        Assert.True(HtmlHelpers.HasText(html, "/Orders/Manage?page=3&status=Confirmed"));
    }

    [Fact]
    public async Task Manage_as_user_is_denied_without_api_calls()
    {
        await SignInUserAsync();

        var resp = await GetAsync("/Orders/Manage");

        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
        AssertAccessDenied(resp);
        Assert.Empty(Fake.Requests);
    }

    [Fact]
    public async Task Manage_anonymous_redirects_to_login()
    {
        AssertLoginRedirect(await GetAsync("/Orders/Manage"), "/Orders/Manage");
        Assert.Empty(Fake.Requests);
    }

    // ---------- SetStatus ----------

    [Fact]
    public async Task SetStatus_confirm_ok_returns_to_manage_keeping_page_and_filter()
    {
        await SignInAdminAsync(token: "tok");
        Fake.On(HttpMethod.Put, "api/orders/11/status", FakeResponse.Json(200, ApiJson.Order(11, "Confirmed")));
        ScriptManage(2, 3, ApiJson.OrderSummary(11, "Confirmed"));

        var resp = await PostFormAsync("/Orders/11/Status",
            Fields(("status", "Confirmed"), ("returnTo", "manage"), ("page", "2"), ("filter", "Pending")), TokenPage);

        AssertRedirect(resp, "/Orders/Manage?page=2&status=Pending");
        var put = Fake.Single(HttpMethod.Put, "api/orders/11/status");
        Assert.Equal("Bearer tok", put.Authorization);
        Assert.Equal("Confirmed", put.Json().GetProperty("status").GetString());

        var html = await GetHtmlAsync("/Orders/Manage?page=2&status=Pending");
        Assert.Equal("Order updated.", HtmlHelpers.Alert(html, "success"));
    }

    [Fact]
    public async Task SetStatus_409_shows_detail()
    {
        await SignInAdminAsync();
        Fake.On(HttpMethod.Put, "api/orders/11/status",
            FakeResponse.Json(409, ApiJson.ConflictProblem("Cannot move from Cancelled to Confirmed.")));
        ScriptManage();

        await PostFormAsync("/Orders/11/Status", Fields(("status", "Confirmed"), ("returnTo", "manage")), TokenPage);

        Assert.Equal("Cannot move from Cancelled to Confirmed.",
            HtmlHelpers.Alert(await GetHtmlAsync("/Orders/Manage"), "danger"));
    }

    [Fact]
    public async Task SetStatus_400_shows_joined_messages()
    {
        await SignInAdminAsync();
        Fake.On(HttpMethod.Put, "api/orders/11/status", FakeResponse.Json(400, ApiJson.ValidationProblem(
            new Dictionary<string, string[]>
            {
                ["Status"] = new[] { "Invalid status." },
                ["Other"] = new[] { "Something else." }
            })));
        ScriptManage();

        await PostFormAsync("/Orders/11/Status", Fields(("status", "Nope"), ("returnTo", "manage")), TokenPage);

        Assert.Equal("Invalid status. Something else.", HtmlHelpers.Alert(await GetHtmlAsync("/Orders/Manage"), "danger"));
    }

    [Fact]
    public async Task SetStatus_without_return_to_redirects_to_details()
    {
        await SignInAdminAsync();
        Fake.On(HttpMethod.Put, "api/orders/11/status", FakeResponse.Json(200, ApiJson.Order(11, "Confirmed")));

        var resp = await PostFormAsync("/Orders/11/Status", Fields(("status", "Confirmed")), TokenPage);

        AssertRedirect(resp, "/Orders/11");
    }

    [Fact]
    public async Task SetStatus_as_user_is_denied_without_api_calls()
    {
        await SignInUserAsync();

        var resp = await PostFormAsync("/Orders/11/Status", Fields(("status", "Confirmed")), TokenPage);

        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
        AssertAccessDenied(resp);
        Assert.Empty(Fake.Requests);
    }
}
