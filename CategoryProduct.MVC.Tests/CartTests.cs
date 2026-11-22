using System.Net;

namespace CategoryProduct.MVC.Tests;

public class CartTests : MvcTestBase
{
    private const string TokenPage = "/Account/AccessDenied";

    public CartTests(MvcFactory factory) : base(factory) { }

    private void ScriptCartPage(params object[] lines) =>
        Fake.On(HttpMethod.Get, "api/cart", FakeResponse.Json(200, ApiJson.Cart(lines)));

    private static Dictionary<string, string> Fields(params (string, string)[] f) =>
        f.ToDictionary(x => x.Item1, x => x.Item2);

    private async Task<string> FlashOnCartAsync()
    {
        ScriptCartPage();
        return await GetHtmlAsync("/Cart");
    }

    // ---------- Index ----------

    [Fact]
    public async Task Index_shows_lines_with_N2_amounts_and_stock_badge_and_sends_bearer()
    {
        await SignInUserAsync(token: "tok");
        ScriptCartPage(
            ApiJson.CartLine(1, "Bolt", 12.5m, 2, availableQuantity: 100),
            ApiJson.CartLine(2, "Nut", 3m, 4, availableQuantity: 1));

        var html = await GetHtmlAsync("/Cart");

        Assert.True(HtmlHelpers.HasText(html, "Bolt"));
        Assert.True(HtmlHelpers.HasText(html, "Nut"));
        Assert.True(HtmlHelpers.HasText(html, 12.5m.ToString("N2")));
        Assert.True(HtmlHelpers.HasText(html, 25m.ToString("N2")));   // Bolt line total
        Assert.True(HtmlHelpers.HasText(html, 12m.ToString("N2")));   // Nut line total
        Assert.True(HtmlHelpers.HasText(html, 37m.ToString("N2")));   // cart total
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(html, "Only 1 in stock").Count);
        Assert.False(HtmlHelpers.HasText(html, "Only 100 in stock"));
        Assert.Equal("Bearer tok", Fake.Single(HttpMethod.Get, "api/cart").Authorization);
    }

    [Fact]
    public async Task Index_empty_cart_shows_message_and_browse_link()
    {
        await SignInUserAsync();
        ScriptCartPage();

        var html = await GetHtmlAsync("/Cart");

        Assert.True(HtmlHelpers.HasText(html, "Your cart is empty."));
        Assert.Contains("Browse products", html);
        Assert.Contains("href=\"/Products\"", html);
    }

    [Fact]
    public async Task Index_api_401_redirects_to_login()
    {
        await SignInUserAsync();
        Fake.On(HttpMethod.Get, "api/cart", FakeResponse.Status(401));
        Fake.On(HttpMethod.Post, "api/account/refresh", FakeResponse.Status(401));

        AssertLoginRedirect(await GetAsync("/Cart"), "/Cart");
    }

    // ---------- Add ----------

    [Fact]
    public async Task Add_ok_posts_body_with_bearer_and_redirects_to_products()
    {
        await SignInUserAsync(token: "tok");
        Fake.On(HttpMethod.Post, "api/cart/items", FakeResponse.Json(200, ApiJson.Cart()));

        var resp = await PostFormAsync("/Cart/Add", Fields(("productId", "7"), ("quantity", "2")), TokenPage);

        AssertRedirect(resp, "/Products");
        var post = Fake.Single(HttpMethod.Post, "api/cart/items");
        Assert.Equal("Bearer tok", post.Authorization);
        var json = post.Json();
        Assert.Equal(7, json.GetProperty("productId").GetInt32());
        Assert.Equal(2, json.GetProperty("quantity").GetInt32());
    }

    [Fact]
    public async Task Add_ok_flashes_added_to_cart()
    {
        await SignInUserAsync();
        Fake.On(HttpMethod.Post, "api/cart/items", FakeResponse.Json(200, ApiJson.Cart()));

        var resp = await PostFormAsync("/Cart/Add",
            Fields(("productId", "7"), ("quantity", "1"), ("returnUrl", "/Cart")), TokenPage);

        AssertRedirect(resp, "/Cart");
        Assert.Equal("Added to cart.", HtmlHelpers.Alert(await FlashOnCartAsync(), "success"));
    }

    [Fact]
    public async Task Add_local_return_url_redirects_there()
    {
        await SignInUserAsync();
        Fake.On(HttpMethod.Post, "api/cart/items", FakeResponse.Json(200, ApiJson.Cart()));

        var resp = await PostFormAsync("/Cart/Add",
            Fields(("productId", "7"), ("returnUrl", "/Products/Details/7")), TokenPage);

        AssertRedirect(resp, "/Products/Details/7");
    }

    [Fact]
    public async Task Add_external_return_url_redirects_to_products()
    {
        await SignInUserAsync();
        Fake.On(HttpMethod.Post, "api/cart/items", FakeResponse.Json(200, ApiJson.Cart()));

        var resp = await PostFormAsync("/Cart/Add",
            Fields(("productId", "7"), ("returnUrl", "https://evil.test/")), TokenPage);

        AssertRedirect(resp, "/Products");
    }

    [Fact]
    public async Task Add_409_shows_conflict_detail()
    {
        await SignInUserAsync();
        Fake.On(HttpMethod.Post, "api/cart/items", FakeResponse.Json(409, ApiJson.ConflictProblem("Only 3 in stock.")));

        await PostFormAsync("/Cart/Add", Fields(("productId", "7"), ("returnUrl", "/Cart")), TokenPage);

        Assert.Equal("Only 3 in stock.", HtmlHelpers.Alert(await FlashOnCartAsync(), "danger"));
    }

    [Fact]
    public async Task Add_400_shows_joined_messages()
    {
        await SignInUserAsync();
        Fake.On(HttpMethod.Post, "api/cart/items",
            FakeResponse.Json(400, ApiJson.ValidationProblem("Quantity", "Quantity must be at least 1.", "Quantity too large.")));

        await PostFormAsync("/Cart/Add", Fields(("productId", "7"), ("quantity", "0"), ("returnUrl", "/Cart")), TokenPage);

        Assert.Equal("Quantity must be at least 1. Quantity too large.", HtmlHelpers.Alert(await FlashOnCartAsync(), "danger"));
    }

    [Theory]
    [InlineData(404, "Not found.")]
    [InlineData(429, "Too many requests. Please try again later.")]
    public async Task Add_404_and_429_show_fixed_messages(int status, string message)
    {
        await SignInUserAsync();
        Fake.On(HttpMethod.Post, "api/cart/items", FakeResponse.Status(status));

        await PostFormAsync("/Cart/Add", Fields(("productId", "7"), ("returnUrl", "/Cart")), TokenPage);

        Assert.Equal(message, HtmlHelpers.Alert(await FlashOnCartAsync(), "danger"));
    }

    [Fact]
    public async Task Add_403_shows_fallback_message()
    {
        await SignInUserAsync();
        Fake.On(HttpMethod.Post, "api/cart/items", FakeResponse.Status(403));

        await PostFormAsync("/Cart/Add", Fields(("productId", "7"), ("returnUrl", "/Cart")), TokenPage);

        Assert.Equal("Could not add the product to the cart.", HtmlHelpers.Alert(await FlashOnCartAsync(), "danger"));
    }

    // ---------- Update / Remove / Clear ----------

    [Fact]
    public async Task Update_ok_puts_quantity_and_flashes()
    {
        await SignInUserAsync(token: "tok");
        Fake.On(HttpMethod.Put, "api/cart/items/3", FakeResponse.Json(200, ApiJson.Cart()));

        var resp = await PostFormAsync("/Cart/Update", Fields(("productId", "3"), ("quantity", "4")), TokenPage);

        AssertRedirect(resp, "/Cart");
        var put = Fake.Single(HttpMethod.Put, "api/cart/items/3");
        Assert.Equal("Bearer tok", put.Authorization);
        Assert.Equal(4, put.Json().GetProperty("quantity").GetInt32());
        Assert.Equal("Cart updated.", HtmlHelpers.Alert(await FlashOnCartAsync(), "success"));
    }

    [Fact]
    public async Task Update_409_shows_detail()
    {
        await SignInUserAsync();
        Fake.On(HttpMethod.Put, "api/cart/items/3", FakeResponse.Json(409, ApiJson.ConflictProblem("Only 2 in stock.")));

        await PostFormAsync("/Cart/Update", Fields(("productId", "3"), ("quantity", "9")), TokenPage);

        Assert.Equal("Only 2 in stock.", HtmlHelpers.Alert(await FlashOnCartAsync(), "danger"));
    }

    [Fact]
    public async Task Remove_ok_deletes_item_and_flashes()
    {
        await SignInUserAsync(token: "tok");
        Fake.On(HttpMethod.Delete, "api/cart/items/3", FakeResponse.Json(200, ApiJson.Cart()));

        var resp = await PostFormAsync("/Cart/Remove", Fields(("productId", "3")), TokenPage);

        AssertRedirect(resp, "/Cart");
        Assert.Equal("Bearer tok", Fake.Single(HttpMethod.Delete, "api/cart/items/3").Authorization);
        Assert.Equal("Item removed.", HtmlHelpers.Alert(await FlashOnCartAsync(), "success"));
    }

    [Fact]
    public async Task Remove_404_shows_not_found()
    {
        await SignInUserAsync();
        Fake.On(HttpMethod.Delete, "api/cart/items/3", FakeResponse.Status(404));

        await PostFormAsync("/Cart/Remove", Fields(("productId", "3")), TokenPage);

        Assert.Equal("Not found.", HtmlHelpers.Alert(await FlashOnCartAsync(), "danger"));
    }

    [Fact]
    public async Task Clear_ok_deletes_cart_and_flashes()
    {
        await SignInUserAsync(token: "tok");
        Fake.On(HttpMethod.Delete, "api/cart", FakeResponse.Status(204));

        var resp = await PostFormAsync("/Cart/Clear", Fields(), TokenPage);

        AssertRedirect(resp, "/Cart");
        Assert.Equal("Bearer tok", Fake.Single(HttpMethod.Delete, "api/cart").Authorization);
        Assert.Equal("Cart cleared.", HtmlHelpers.Alert(await FlashOnCartAsync(), "success"));
    }

    // ---------- Checkout ----------

    [Fact]
    public async Task Checkout_ok_redirects_to_order_and_shows_order_placed()
    {
        await SignInUserAsync(token: "tok");
        Fake.On(HttpMethod.Post, "api/orders", FakeResponse.Json(201,
            ApiJson.Order(55, "Pending", 1, "alice", ApiJson.OrderLine(1, 1, "Bolt", 12.5m, 2))));
        Fake.On(HttpMethod.Get, "api/orders/55", FakeResponse.Json(200,
            ApiJson.Order(55, "Pending", 1, "alice", ApiJson.OrderLine(1, 1, "Bolt", 12.5m, 2))));

        var resp = await PostFormAsync("/Cart/Checkout", Fields(), TokenPage);

        AssertRedirect(resp, "/Orders/55");
        Assert.Equal("Bearer tok", Fake.Single(HttpMethod.Post, "api/orders").Authorization);
        var html = await GetHtmlAsync("/Orders/55");
        Assert.Equal("Order placed.", HtmlHelpers.Alert(html, "success"));
    }

    [Fact]
    public async Task Checkout_409_shows_detail_on_cart()
    {
        await SignInUserAsync();
        Fake.On(HttpMethod.Post, "api/orders", FakeResponse.Json(409, ApiJson.ConflictProblem("Bolt is out of stock.")));

        var resp = await PostFormAsync("/Cart/Checkout", Fields(), TokenPage);

        AssertRedirect(resp, "/Cart");
        Assert.Equal("Bolt is out of stock.", HtmlHelpers.Alert(await FlashOnCartAsync(), "danger"));
    }

    [Fact]
    public async Task Checkout_400_shows_cart_is_empty()
    {
        await SignInUserAsync();
        Fake.On(HttpMethod.Post, "api/orders", FakeResponse.Json(400, ApiJson.ValidationProblem("Cart", "Cart is empty.")));

        await PostFormAsync("/Cart/Checkout", Fields(), TokenPage);

        Assert.Equal("Your cart is empty.", HtmlHelpers.Alert(await FlashOnCartAsync(), "danger"));
    }

    [Fact]
    public async Task Checkout_401_redirects_to_login()
    {
        await SignInUserAsync();
        Fake.On(HttpMethod.Post, "api/orders", FakeResponse.Status(401));
        Fake.On(HttpMethod.Post, "api/account/refresh", FakeResponse.Status(401));

        var resp = await PostFormAsync("/Cart/Checkout", Fields(), TokenPage);

        AssertLoginRedirect(resp, "/Cart/Checkout");
    }

    // ---------- Anonymous ----------

    [Theory]
    [InlineData("/Cart/Add")]
    [InlineData("/Cart/Update")]
    [InlineData("/Cart/Remove")]
    [InlineData("/Cart/Clear")]
    [InlineData("/Cart/Checkout")]
    public async Task Anonymous_post_redirects_to_login_without_api_calls(string url)
    {
        var resp = await PostFormAsync(url, Fields(("productId", "1"), ("quantity", "1")), "/Account/Login");

        AssertLoginRedirect(resp, url);
        Assert.Empty(Fake.Requests);
    }
}
