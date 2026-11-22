using System.Net;

namespace CategoryProduct.MVC.Tests;

/// <summary>401 handling end to end: failed refresh signs the user out; successful refresh retries transparently.</summary>
public class SessionExpiryTests : MvcTestBase
{
    private const string TokenPage = "/Account/AccessDenied";
    private const string OldToken = "tok-old";
    private const string NewToken = "tok-new";

    public SessionExpiryTests(MvcFactory factory) : base(factory) { }

    private sealed record Route(HttpMethod Method, string Path, object? Body, int OkStatus = 200);

    private sealed record Scenario(string Name, HttpMethod Method, string Url, string PageRoutePath,
        Dictionary<string, string>? Form, params Route[] Routes)
    {
        public HttpMethod PageRouteMethod => Routes.First(r => r.Path == PageRoutePath).Method;
        public override string ToString() => Name;
    }

    private static readonly object Product = ApiJson.Product(1, "Widget");
    private static readonly object Order = ApiJson.Order(1, "Pending", 1, "alice", ApiJson.OrderLine(1, 1, "Bolt", 10m, 1));

    private static Scenario[] Scenarios() =>
    [
        new("Categories POST Create", HttpMethod.Post, "/Categories/Create", "api/category",
            new() { ["Name"] = "Gadgets" },
            new Route(HttpMethod.Post, "api/category", null, 201)),
        new("Products POST Edit", HttpMethod.Post, "/Products/Edit/1", "api/product/1",
            new() { ["Id"] = "1", ["Name"] = "Widget", ["Price"] = "5", ["Quantity"] = "2", ["CategoryId"] = "1" },
            new Route(HttpMethod.Put, "api/product/1", null, 204)),
        new("Inventory GET Index", HttpMethod.Get, "/Inventory?page=2", "api/product/low-stock", null,
            new Route(HttpMethod.Get, "api/product", ApiJson.Paged(new[] { Product }, 2, 10, totalPages: 2)),
            new Route(HttpMethod.Get, "api/product/low-stock", ApiJson.Paged(Array.Empty<object>(), 1, 1, totalCount: 0))),
        new("Inventory GET History", HttpMethod.Get, "/Inventory/Product/1?page=2", "api/product/1/stock-movements", null,
            new Route(HttpMethod.Get, "api/product/1", Product),
            new Route(HttpMethod.Get, "api/product/1/stock-movements", ApiJson.Paged(Array.Empty<object>(), 2, 10, totalPages: 2))),
        new("Inventory POST AddMovement", HttpMethod.Post, "/Inventory/Product/1", "api/product/1/stock-movements",
            new() { ["quantityChange"] = "5", ["reason"] = "Restock", ["note"] = "", ["page"] = "1" },
            new Route(HttpMethod.Post, "api/product/1/stock-movements", null, 201)),
        new("Users GET Index", HttpMethod.Get, "/Users?page=2", "api/users", null,
            new Route(HttpMethod.Get, "api/users", ApiJson.Paged(new[] { ApiJson.User(1, "alice") }, 2, 10, totalPages: 2))),
        new("Cart GET Index", HttpMethod.Get, "/Cart", "api/cart", null,
            new Route(HttpMethod.Get, "api/cart", ApiJson.Cart())),
        new("Cart POST Add", HttpMethod.Post, "/Cart/Add", "api/cart/items",
            new() { ["productId"] = "1", ["quantity"] = "1" },
            new Route(HttpMethod.Post, "api/cart/items", ApiJson.Cart())),
        new("Orders GET Index", HttpMethod.Get, "/Orders?page=2", "api/orders", null,
            new Route(HttpMethod.Get, "api/orders", ApiJson.Paged(new[] { ApiJson.OrderSummary(1) }, 2, 10, totalPages: 2))),
        new("Orders GET Details", HttpMethod.Get, "/Orders/1", "api/orders/1", null,
            new Route(HttpMethod.Get, "api/orders/1", Order)),
        new("Orders GET Manage", HttpMethod.Get, "/Orders/Manage?status=Pending", "api/orders/all", null,
            new Route(HttpMethod.Get, "api/orders/all", ApiJson.Paged(new[] { ApiJson.OrderSummary(1) }))),
        new("Orders POST Cancel", HttpMethod.Post, "/Orders/1/Cancel", "api/orders/1/cancel",
            new(),
            new Route(HttpMethod.Post, "api/orders/1/cancel", Order)),
    ];

    public static IEnumerable<object[]> ScenarioNames() => Scenarios().Select(s => new object[] { s.Name });

    private static Scenario Get(string name) => Scenarios().Single(s => s.Name == name);

    /// <summary>Every route answers 401 to the old bearer and succeeds for anything else.</summary>
    private void ScriptRoutes(Scenario s)
    {
        foreach (var r in s.Routes)
        {
            var ok = r.Body is null
                ? FakeResponse.Status(r.OkStatus)
                : FakeResponse.Json(r.OkStatus, r.Body);
            Fake.On(r.Method, r.Path,
                req => req.Authorization == "Bearer " + OldToken ? FakeResponse.Status(401) : ok);
        }
    }

    private Task<HttpResponseMessage> Perform(Scenario s) =>
        s.Method == HttpMethod.Get ? GetAsync(s.Url) : PostFormAsync(s.Url, s.Form!, TokenPage);

    [Theory]
    [MemberData(nameof(ScenarioNames))]
    public async Task Failed_refresh_redirects_to_login_clears_cookie_and_refreshes_once(string name)
    {
        var s = Get(name);
        await SignInAdminAsync(token: OldToken, refresh: "r1");
        ScriptRoutes(s);
        Fake.On(HttpMethod.Post, "api/account/refresh", FakeResponse.Status(401));

        var resp = await Perform(s);

        AssertLoginRedirect(resp, s.Url);
        Assert.Single(Fake.Calls(HttpMethod.Post, "api/account/refresh"));
        Assert.Contains("\"refreshToken\":\"r1\"", Fake.Single(HttpMethod.Post, "api/account/refresh").BodyText);

        // the cookie was cleared: the same client is anonymous now
        Fake.ClearRequests();
        AssertLoginRedirect(await GetAsync("/Cart"), "/Cart");
        Assert.Empty(Fake.Requests);
    }

    [Theory]
    [MemberData(nameof(ScenarioNames))]
    public async Task Successful_refresh_retries_with_new_bearer_and_issues_new_cookie(string name)
    {
        var s = Get(name);
        await SignInAdminAsync(token: OldToken, refresh: "r1");
        ScriptRoutes(s);
        Fake.On(HttpMethod.Post, "api/account/refresh", FakeResponse.Json(200, ApiJson.Login("Admin", 900, NewToken, "r2")));

        var resp = await Perform(s);

        if (s.Method == HttpMethod.Get)
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        else
            Assert.DoesNotContain("/Account/Login", resp.Headers.Location?.OriginalString ?? "");
        Assert.Equal((int)resp.StatusCode is >= 300 and < 400 ? 302 : 200, (int)resp.StatusCode);

        Assert.Single(Fake.Calls(HttpMethod.Post, "api/account/refresh"));
        var calls = Fake.Calls(s.PageRouteMethod, s.PageRoutePath);
        Assert.Equal(2, calls.Count);
        Assert.Equal("Bearer " + OldToken, calls[0].Authorization);
        Assert.Equal("Bearer " + NewToken, calls[1].Authorization);
        Assert.Contains(resp.Headers.GetValues("Set-Cookie"),
            c => c.StartsWith(".AspNetCore.Cookies=", StringComparison.Ordinal) && !c.Contains("expires=Thu, 01 Jan 1970"));
    }

    [Fact]
    public async Task Expiring_token_is_refreshed_before_the_first_page_call()
    {
        await SignInUserAsync(expiresInSeconds: 30, token: OldToken, refresh: "r1");
        Fake.On(HttpMethod.Post, "api/account/refresh", FakeResponse.Json(200, ApiJson.Login("User", 900, NewToken, "r2")));
        Fake.On(HttpMethod.Get, "api/cart", FakeResponse.Json(200, ApiJson.Cart()));

        await GetHtmlAsync("/Cart");

        Assert.Equal(2, Fake.Requests.Count);
        Assert.Equal("api/account/refresh", Fake.Requests[0].Path);
        Assert.Equal(HttpMethod.Post, Fake.Requests[0].Method);
        Assert.Null(Fake.Requests[0].Authorization);
        var page = Fake.Single(HttpMethod.Get, "api/cart");
        Assert.Equal(1, Fake.Requests.ToList().IndexOf(page));
        Assert.Equal("Bearer " + NewToken, page.Authorization);
    }

    [Fact]
    public async Task Token_valid_for_15_minutes_triggers_no_refresh()
    {
        await SignInUserAsync(expiresInSeconds: 900, token: OldToken, refresh: "r1");
        Fake.On(HttpMethod.Get, "api/cart", FakeResponse.Json(200, ApiJson.Cart()));

        await GetHtmlAsync("/Cart");

        Assert.Empty(Fake.Calls(HttpMethod.Post, "api/account/refresh"));
        Assert.Equal("Bearer " + OldToken, Fake.Single(HttpMethod.Get, "api/cart").Authorization);
    }
}
