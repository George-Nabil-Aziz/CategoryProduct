using System.Net;

namespace CategoryProduct.MVC.Tests;

public class OrdersTests : MvcTestBase
{
    private const string TokenPage = "/Account/AccessDenied";

    public OrdersTests(MvcFactory factory) : base(factory) { }

    private static object SampleOrder(int id = 1, string status = "Pending", string userName = "bob", int? productId = 5) =>
        ApiJson.Order(id, status, 2, userName, ApiJson.OrderLine(1, productId, "Bolt", 12.5m, 2));

    private void ScriptOrder(int id = 1, string status = "Pending", string userName = "bob", int? productId = 5) =>
        Fake.On(HttpMethod.Get, $"api/orders/{id}", FakeResponse.Json(200, SampleOrder(id, status, userName, productId)));

    private static Dictionary<string, string> Fields(params (string, string)[] f) =>
        f.ToDictionary(x => x.Item1, x => x.Item2);

    // ---------- Index ----------

    [Fact]
    public async Task Index_lists_orders_with_badge_and_details_links()
    {
        await SignInUserAsync(token: "tok");
        Fake.On(HttpMethod.Get, "api/orders", FakeResponse.Json(200, ApiJson.Paged(new[]
        {
            ApiJson.OrderSummary(11, "Pending", total: 25m, itemCount: 2),
            ApiJson.OrderSummary(12, "Confirmed", total: 40m, itemCount: 3),
            ApiJson.OrderSummary(13, "Cancelled", total: 5m, itemCount: 1)
        }, pageSize: 10)));

        var html = await GetHtmlAsync("/Orders");

        Assert.True(HtmlHelpers.HasText(html, "#11"));
        Assert.True(HtmlHelpers.HasText(html, "#12"));
        Assert.True(HtmlHelpers.HasText(html, "#13"));
        Assert.Contains("badge bg-warning text-dark\">Pending", html);
        Assert.Contains("badge bg-success\">Confirmed", html);
        Assert.Contains("badge bg-secondary\">Cancelled", html);
        Assert.True(HtmlHelpers.HasText(html, "/Orders/11"));
        Assert.True(HtmlHelpers.HasText(html, 25m.ToString("N2")));
        Assert.DoesNotContain(">Next</a>", html);
        var req = Fake.Single(HttpMethod.Get, "api/orders");
        Assert.Equal("?page=1&pageSize=10", req.Query);
        Assert.Equal("Bearer tok", req.Authorization);
    }

    [Fact]
    public async Task Index_page_2_sends_query_and_shows_paging_links()
    {
        await SignInUserAsync();
        Fake.On(HttpMethod.Get, "api/orders", FakeResponse.Json(200,
            ApiJson.Paged(new[] { ApiJson.OrderSummary(11) }, page: 2, pageSize: 10, totalPages: 3)));

        var html = await GetHtmlAsync("/Orders?page=2");

        Assert.Equal("?page=2&pageSize=10", Fake.Single(HttpMethod.Get, "api/orders").Query);
        Assert.Contains(">Previous</a>", html);
        Assert.Contains(">Next</a>", html);
        Assert.True(HtmlHelpers.HasText(html, "/Orders?page=1"));
        Assert.True(HtmlHelpers.HasText(html, "/Orders?page=3"));
    }

    [Fact]
    public async Task Index_api_401_redirects_to_login()
    {
        await SignInUserAsync();
        Fake.On(HttpMethod.Get, "api/orders", FakeResponse.Status(401));
        Fake.On(HttpMethod.Post, "api/account/refresh", FakeResponse.Status(401));

        AssertLoginRedirect(await GetAsync("/Orders"), "/Orders");
    }

    // ---------- Details ----------

    [Fact]
    public async Task Details_pending_shows_items_total_and_cancel_form()
    {
        await SignInUserAsync(token: "tok");
        ScriptOrder(1, "Pending");

        var html = await GetHtmlAsync("/Orders/1");

        Assert.True(HtmlHelpers.HasText(html, "Order #1"));
        Assert.True(HtmlHelpers.HasText(html, "Bolt"));
        Assert.True(HtmlHelpers.HasText(html, 25m.ToString("N2")));
        Assert.True(HtmlHelpers.FormAction(html, "/Orders/1/Cancel"));
        Assert.False(HtmlHelpers.FormAction(html, "/Orders/1/Status"));
        Assert.False(HtmlHelpers.HasText(html, "(product removed)"));
        Assert.False(HtmlHelpers.HasText(html, "bob"));   // order owner only shown to admins
        Assert.Equal("Bearer tok", Fake.Single(HttpMethod.Get, "api/orders/1").Authorization);
    }

    [Theory]
    [InlineData("Confirmed")]
    [InlineData("Cancelled")]
    public async Task Details_non_pending_has_no_cancel_form(string status)
    {
        await SignInUserAsync();
        ScriptOrder(1, status);

        var html = await GetHtmlAsync("/Orders/1");

        Assert.True(HtmlHelpers.HasText(html, status));
        Assert.False(HtmlHelpers.FormAction(html, "/Orders/1/Cancel"));
    }

    [Fact]
    public async Task Details_removed_product_is_marked()
    {
        await SignInUserAsync();
        ScriptOrder(1, "Pending", productId: null);

        var html = await GetHtmlAsync("/Orders/1");

        Assert.True(HtmlHelpers.HasText(html, "(product removed)"));
    }

    [Fact]
    public async Task Details_as_admin_shows_user_name_and_confirm_for_pending()
    {
        await SignInAdminAsync();
        ScriptOrder(1, "Pending", userName: "bob");

        var html = await GetHtmlAsync("/Orders/1");

        Assert.True(HtmlHelpers.HasText(html, "bob"));
        Assert.True(HtmlHelpers.FormAction(html, "/Orders/1/Status"));
        Assert.Contains("Confirm order", html);
        Assert.True(HtmlHelpers.FormAction(html, "/Orders/1/Cancel"));
    }

    [Fact]
    public async Task Details_as_admin_confirmed_has_no_confirm_button()
    {
        await SignInAdminAsync();
        ScriptOrder(1, "Confirmed", userName: "bob");

        var html = await GetHtmlAsync("/Orders/1");

        Assert.False(HtmlHelpers.FormAction(html, "/Orders/1/Status"));
    }

    [Fact]
    public async Task Details_404_returns_404()
    {
        await SignInUserAsync();
        Fake.On(HttpMethod.Get, "api/orders/9", FakeResponse.Status(404));

        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync("/Orders/9")).StatusCode);
    }

    // ---------- Cancel ----------

    [Fact]
    public async Task Cancel_ok_redirects_to_details_and_flashes()
    {
        await SignInUserAsync(token: "tok");
        Fake.On(HttpMethod.Post, "api/orders/1/cancel", FakeResponse.Json(200, SampleOrder(1, "Cancelled")));
        ScriptOrder(1, "Cancelled");

        var resp = await PostFormAsync("/Orders/1/Cancel", Fields(), TokenPage);

        AssertRedirect(resp, "/Orders/1");
        Assert.Equal("Bearer tok", Fake.Single(HttpMethod.Post, "api/orders/1/cancel").Authorization);
        Assert.Equal("Order cancelled.", HtmlHelpers.Alert(await GetHtmlAsync("/Orders/1"), "success"));
    }

    [Fact]
    public async Task Cancel_409_shows_detail()
    {
        await SignInUserAsync();
        Fake.On(HttpMethod.Post, "api/orders/1/cancel", FakeResponse.Json(409, ApiJson.ConflictProblem("Order is already confirmed.")));
        ScriptOrder(1, "Confirmed");

        await PostFormAsync("/Orders/1/Cancel", Fields(), TokenPage);

        Assert.Equal("Order is already confirmed.", HtmlHelpers.Alert(await GetHtmlAsync("/Orders/1"), "danger"));
    }

    [Fact]
    public async Task Cancel_404_shows_order_not_found()
    {
        await SignInUserAsync();
        Fake.On(HttpMethod.Post, "api/orders/1/cancel", FakeResponse.Status(404));
        ScriptOrder(1);

        await PostFormAsync("/Orders/1/Cancel", Fields(), TokenPage);

        Assert.Equal("Order not found.", HtmlHelpers.Alert(await GetHtmlAsync("/Orders/1"), "danger"));
    }

    [Fact]
    public async Task Cancel_admin_with_return_to_manage_goes_back_to_manage_with_page_and_filter()
    {
        await SignInAdminAsync();
        Fake.On(HttpMethod.Post, "api/orders/1/cancel", FakeResponse.Json(200, SampleOrder(1, "Cancelled")));

        var resp = await PostFormAsync("/Orders/1/Cancel",
            Fields(("returnTo", "manage"), ("page", "2"), ("filter", "Pending")), TokenPage);

        AssertRedirect(resp, "/Orders/Manage?page=2&status=Pending");
    }

    [Fact]
    public async Task Cancel_user_with_return_to_manage_goes_to_details()
    {
        await SignInUserAsync();
        Fake.On(HttpMethod.Post, "api/orders/1/cancel", FakeResponse.Json(200, SampleOrder(1, "Cancelled")));

        var resp = await PostFormAsync("/Orders/1/Cancel",
            Fields(("returnTo", "manage"), ("page", "2"), ("filter", "Pending")), TokenPage);

        AssertRedirect(resp, "/Orders/1");
    }
}
