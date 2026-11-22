using System.Net;

namespace CategoryProduct.MVC.Tests;

public class InventoryTests : MvcTestBase
{
    // Signed-in page that needs no API call but carries the layout's logout form (antiforgery token).
    private const string TokenPage = "/Account/AccessDenied";

    public InventoryTests(MvcFactory factory) : base(factory) { }

    private static void AssertAccessDenied(HttpResponseMessage resp)
    {
        var loc = resp.Headers.Location!;
        Assert.Equal("/Account/AccessDenied", loc.IsAbsoluteUri ? loc.AbsolutePath : loc.OriginalString.Split('?')[0], ignoreCase: true);
    }

    private void ScriptIndex(IEnumerable<object> items, int lowStockTotal, int page = 1, int totalPages = 1)
    {
        Fake.On(HttpMethod.Get, "api/product", FakeResponse.Json(200, ApiJson.Paged(items, page, 10, totalPages: totalPages)));
        Fake.On(HttpMethod.Get, "api/product/low-stock",
            FakeResponse.Json(200, ApiJson.Paged(Array.Empty<object>(), 1, 1, totalCount: lowStockTotal)));
    }

    private void ScriptHistory(int id = 4, int page = 1, int totalPages = 1, params object[] movements)
    {
        Fake.On(HttpMethod.Get, $"api/product/{id}", FakeResponse.Json(200, ApiJson.Product(id, "Widget", quantity: 12)));
        Fake.On(HttpMethod.Get, $"api/product/{id}/stock-movements",
            FakeResponse.Json(200, ApiJson.Paged(movements, page, 10, totalPages: totalPages)));
    }

    private void ScriptRefreshFails() => Fake.On(HttpMethod.Post, "api/account/refresh", FakeResponse.Status(401));

    private static Dictionary<string, string> Movement(string change = "5", string reason = "Restock", string note = "") =>
        new() { ["quantityChange"] = change, ["reason"] = reason, ["note"] = note, ["page"] = "1" };

    // ---------- Index ----------

    [Fact]
    public async Task Index_shows_low_stock_banner_badges_and_sends_expected_api_calls()
    {
        await SignInAdminAsync(token: "tok");
        ScriptIndex(new object[]
        {
            ApiJson.Product(1, "Bolt", quantity: 2, isLowStock: true),
            ApiJson.Product(2, "Nut", quantity: 50),
            ApiJson.Product(3, "Screw", quantity: 1, isLowStock: true)
        }, lowStockTotal: 3);

        var html = await GetHtmlAsync("/Inventory");

        Assert.Equal("3 product(s) low on stock", HtmlHelpers.Alert(html, "warning"));
        Assert.True(HtmlHelpers.HasText(html, "Bolt"));
        Assert.True(HtmlHelpers.HasText(html, "Nut"));
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(html, "Low stock</span>").Count);
        Assert.True(HtmlHelpers.HasText(html, "/Inventory/Product/1"));

        var products = Fake.Single(HttpMethod.Get, "api/product");
        Assert.StartsWith("?page=1&pageSize=10", products.Query);
        var low = Fake.Single(HttpMethod.Get, "api/product/low-stock");
        Assert.Equal("?page=1&pageSize=1", low.Query);
        Assert.Equal("Bearer tok", low.Authorization);
    }

    [Fact]
    public async Task Index_without_low_stock_has_no_banner()
    {
        await SignInAdminAsync();
        ScriptIndex(new object[] { ApiJson.Product(2, "Nut", quantity: 50) }, lowStockTotal: 0);

        var html = await GetHtmlAsync("/Inventory");

        Assert.Empty(HtmlHelpers.Alerts(html, "warning"));
        Assert.DoesNotContain("low on stock", html);
        Assert.DoesNotContain("Low stock</span>", html);
    }

    [Fact]
    public async Task Index_middle_page_shows_previous_and_next_and_requests_that_page()
    {
        await SignInAdminAsync();
        ScriptIndex(new object[] { ApiJson.Product(2, "Nut") }, lowStockTotal: 0, page: 2, totalPages: 3);

        var html = await GetHtmlAsync("/Inventory?page=2");

        Assert.Contains(">Previous</a>", html);
        Assert.Contains(">Next</a>", html);
        Assert.True(HtmlHelpers.HasText(html, "/Inventory?page=1"));
        Assert.True(HtmlHelpers.HasText(html, "/Inventory?page=3"));
        Assert.StartsWith("?page=2&pageSize=10", Fake.Single(HttpMethod.Get, "api/product").Query);
    }

    [Fact]
    public async Task Index_single_page_has_no_paging_links()
    {
        await SignInAdminAsync();
        ScriptIndex(new object[] { ApiJson.Product(2, "Nut") }, lowStockTotal: 0);

        var html = await GetHtmlAsync("/Inventory");

        Assert.DoesNotContain(">Previous</a>", html);
        Assert.DoesNotContain(">Next</a>", html);
    }

    [Fact]
    public async Task Index_as_user_is_denied_without_api_calls()
    {
        await SignInUserAsync();

        var resp = await GetAsync("/Inventory");

        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
        AssertAccessDenied(resp);
        Assert.Empty(Fake.Requests);
    }

    [Fact]
    public async Task Index_api_401_and_failed_refresh_redirects_to_login_with_return_url()
    {
        await SignInAdminAsync();
        Fake.On(HttpMethod.Get, "api/product", FakeResponse.Status(401));
        ScriptRefreshFails();

        var resp = await GetAsync("/Inventory?page=2");

        AssertLoginRedirect(resp, "/Inventory?page=2");
    }

    // ---------- History ----------

    [Fact]
    public async Task History_renders_product_movements_signs_and_dashes_for_empty_cells()
    {
        await SignInAdminAsync(token: "tok");
        ScriptHistory(4, 1, 1,
            ApiJson.StockMovement(1, 4, 5, 17, "Restock", note: "Delivery", userName: "root"),
            ApiJson.StockMovement(2, 4, -3, 14, "Damage", note: null, userName: null));

        var html = await GetHtmlAsync("/Inventory/Product/4");

        Assert.True(HtmlHelpers.HasText(html, "Stock history: Widget"));
        Assert.True(HtmlHelpers.HasText(html, "Quantity:"));
        Assert.True(HtmlHelpers.HasText(html, "<td>+5</td>"));
        Assert.True(HtmlHelpers.HasText(html, "<td>-3</td>"));
        Assert.Contains("<td>Delivery</td>", html);
        Assert.Contains("<td>root</td>", html);
        // second row: empty note and user render as "-" (also the "-3" change must not get a plus)
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(html, "<td>-</td>").Count);
        Assert.DoesNotContain("+-3", html);

        Assert.Equal("Bearer tok", Fake.Single(HttpMethod.Get, "api/product/4/stock-movements").Authorization);
        Assert.Equal("?page=1&pageSize=10", Fake.Single(HttpMethod.Get, "api/product/4/stock-movements").Query);
        Fake.Single(HttpMethod.Get, "api/product/4");
    }

    [Fact]
    public async Task History_product_404_returns_404()
    {
        await SignInAdminAsync();
        Fake.On(HttpMethod.Get, "api/product/4", FakeResponse.Status(404));

        var resp = await GetAsync("/Inventory/Product/4");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task History_movements_404_returns_404()
    {
        await SignInAdminAsync();
        Fake.On(HttpMethod.Get, "api/product/4", FakeResponse.Json(200, ApiJson.Product(4, "Widget")));
        Fake.On(HttpMethod.Get, "api/product/4/stock-movements", FakeResponse.Status(404));

        var resp = await GetAsync("/Inventory/Product/4");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task History_page_2_sends_query_and_shows_previous_link()
    {
        await SignInAdminAsync();
        ScriptHistory(4, page: 2, totalPages: 2, ApiJson.StockMovement(9, 4, 1, 13));

        var html = await GetHtmlAsync("/Inventory/Product/4?page=2");

        Assert.Equal("?page=2&pageSize=10", Fake.Single(HttpMethod.Get, "api/product/4/stock-movements").Query);
        Assert.Contains(">Previous</a>", html);
        Assert.DoesNotContain(">Next</a>", html);
        Assert.True(HtmlHelpers.HasText(html, "/Inventory/Product/4?page=1"));
    }

    [Fact]
    public async Task History_api_401_redirects_to_login()
    {
        await SignInAdminAsync();
        Fake.On(HttpMethod.Get, "api/product/4", FakeResponse.Status(401));
        ScriptRefreshFails();

        var resp = await GetAsync("/Inventory/Product/4");

        AssertLoginRedirect(resp, "/Inventory/Product/4");
    }

    // ---------- AddMovement ----------

    [Fact]
    public async Task AddMovement_ok_redirects_to_history_and_shows_success()
    {
        await SignInAdminAsync(token: "tok");
        Fake.On(HttpMethod.Post, "api/product/4/stock-movements", FakeResponse.Status(201));
        ScriptHistory();

        var resp = await PostFormAsync("/Inventory/Product/4", Movement("5", "Restock", "Delivery"), TokenPage);

        AssertRedirect(resp, "/Inventory/Product/4?page=1");
        var post = Fake.Single(HttpMethod.Post, "api/product/4/stock-movements");
        Assert.Equal("Bearer tok", post.Authorization);
        var json = post.Json();
        Assert.Equal(5, json.GetProperty("quantityChange").GetInt32());
        Assert.Equal("Restock", json.GetProperty("reason").GetString());
        Assert.Equal("Delivery", json.GetProperty("note").GetString());

        var html = await GetHtmlAsync("/Inventory/Product/4?page=1");
        Assert.Equal("Stock movement recorded.", HtmlHelpers.Alert(html, "success"));
    }

    [Fact]
    public async Task AddMovement_keeps_page_in_redirect()
    {
        await SignInAdminAsync();
        Fake.On(HttpMethod.Post, "api/product/4/stock-movements", FakeResponse.Status(201));
        var fields = Movement();
        fields["page"] = "3";

        var resp = await PostFormAsync("/Inventory/Product/4", fields, TokenPage);

        AssertRedirect(resp, "/Inventory/Product/4?page=3");
    }

    [Fact]
    public async Task AddMovement_409_shows_conflict_detail()
    {
        await SignInAdminAsync();
        Fake.On(HttpMethod.Post, "api/product/4/stock-movements",
            FakeResponse.Json(409, ApiJson.ConflictProblem("Stock cannot go below zero.")));
        ScriptHistory();

        var resp = await PostFormAsync("/Inventory/Product/4", Movement("-100", "Damage"), TokenPage);
        AssertRedirect(resp, "/Inventory/Product/4?page=1");

        var html = await GetHtmlAsync("/Inventory/Product/4?page=1");
        Assert.Equal("Stock cannot go below zero.", HtmlHelpers.Alert(html, "danger"));
    }

    [Fact]
    public async Task AddMovement_400_shows_joined_messages()
    {
        await SignInAdminAsync();
        Fake.On(HttpMethod.Post, "api/product/4/stock-movements", FakeResponse.Json(400, ApiJson.ValidationProblem(
            new Dictionary<string, string[]>
            {
                ["QuantityChange"] = new[] { "Must not be zero." },
                ["Reason"] = new[] { "Invalid reason." }
            })));
        ScriptHistory();

        await PostFormAsync("/Inventory/Product/4", Movement("0"), TokenPage);

        var html = await GetHtmlAsync("/Inventory/Product/4?page=1");
        Assert.Equal("Must not be zero. Invalid reason.", HtmlHelpers.Alert(html, "danger"));
    }

    [Theory]
    [InlineData(403, "You do not have permission to change stock.")]
    [InlineData(429, "Too many requests. Please try again later.")]
    public async Task AddMovement_403_and_429_show_fixed_messages(int status, string message)
    {
        await SignInAdminAsync();
        Fake.On(HttpMethod.Post, "api/product/4/stock-movements", FakeResponse.Status(status));
        ScriptHistory();

        await PostFormAsync("/Inventory/Product/4", Movement(), TokenPage);

        var html = await GetHtmlAsync("/Inventory/Product/4?page=1");
        Assert.Equal(message, HtmlHelpers.Alert(html, "danger"));
    }

    [Fact]
    public async Task AddMovement_404_returns_404()
    {
        await SignInAdminAsync();
        Fake.On(HttpMethod.Post, "api/product/4/stock-movements", FakeResponse.Status(404));

        var resp = await PostFormAsync("/Inventory/Product/4", Movement(), TokenPage);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task AddMovement_401_redirects_to_login()
    {
        await SignInAdminAsync();
        Fake.On(HttpMethod.Post, "api/product/4/stock-movements", FakeResponse.Status(401));
        ScriptRefreshFails();

        var resp = await PostFormAsync("/Inventory/Product/4", Movement(), TokenPage);

        AssertLoginRedirect(resp, "/Inventory/Product/4");
    }

    [Fact]
    public async Task AddMovement_as_user_is_denied_without_api_calls()
    {
        await SignInUserAsync();

        var resp = await PostFormAsync("/Inventory/Product/4", Movement(), TokenPage);

        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
        AssertAccessDenied(resp);
        Assert.Empty(Fake.Requests);
    }
}
