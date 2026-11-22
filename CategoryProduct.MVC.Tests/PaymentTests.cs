using System.Net;
using System.Text.RegularExpressions;

namespace CategoryProduct.MVC.Tests;

public class PaymentTests : MvcTestBase
{
    private const string TokenPage = "/Account/AccessDenied";
    private static readonly string[] TestTokens =
        { "tok_visa", "tok_mastercard", "tok_decline", "tok_insufficient_funds", "tok_refund_fail", "tok_error" };

    public PaymentTests(MvcFactory factory) : base(factory) { }

    private static object Order(int id = 1, string status = "Pending", string payment = "Unpaid", string userName = "alice") =>
        ApiJson.OrderWithPayment(id, payment, status, 2, userName, ApiJson.OrderLine(1, 5, "Bolt", 12.5m, 2));

    private void ScriptOrder(int id = 1, string status = "Pending", string payment = "Unpaid", string userName = "alice") =>
        Fake.On(HttpMethod.Get, $"api/orders/{id}", FakeResponse.Json(200, Order(id, status, payment, userName)));

    private static Dictionary<string, string> PayFields(string token = "tok_visa", string key = "key-1") =>
        new() { ["paymentToken"] = token, ["idempotencyKey"] = key };

    private static string KeyIn(string html) =>
        Regex.Match(html, "name=\"idempotencyKey\"[^>]*value=\"([^\"]+)\"|value=\"([^\"]+)\"[^>]*name=\"idempotencyKey\"")
            is { Success: true } m ? (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value) : "";

    // ---------- Details: form and badges ----------

    [Fact]
    public async Task Pending_unpaid_order_of_the_viewer_shows_the_pay_now_form()
    {
        await SignInUserAsync("alice");
        ScriptOrder();

        var html = await GetHtmlAsync("/Orders/1");

        Assert.True(HtmlHelpers.FormAction(html, "/Orders/1/Pay"));
        Assert.Contains("__RequestVerificationToken", html);
        Assert.Contains("name=\"paymentToken\"", html);
        foreach (var t in TestTokens) Assert.Contains($"<option value=\"{t}\">", html);
        Assert.True(HtmlHelpers.HasText(html, "Test payments only. Do not enter card numbers."));
        Assert.Matches("^[0-9a-f]{32}$", KeyIn(html));
        Assert.True(HtmlHelpers.HasText(html, "Unpaid"));
        // No free-text card input: the only text-like control is the token <select>.
        Assert.DoesNotContain("type=\"text\"", html);
        Assert.DoesNotContain("cardNumber", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("autocomplete=\"cc-number\"", html);
    }

    [Fact]
    public async Task The_idempotency_key_differs_between_two_renders()
    {
        await SignInUserAsync("alice");
        ScriptOrder();

        var first = KeyIn(await GetHtmlAsync("/Orders/1"));
        var second = KeyIn(await GetHtmlAsync("/Orders/1"));

        Assert.NotEqual("", first);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task Paid_order_shows_a_Paid_badge_and_no_form()
    {
        await SignInUserAsync("alice");
        ScriptOrder(payment: "Paid");

        var html = await GetHtmlAsync("/Orders/1");

        Assert.Contains("data-payment-status=\"Paid\"", html);
        Assert.False(HtmlHelpers.FormAction(html, "/Orders/1/Pay"));
    }

    [Fact]
    public async Task Refunded_order_shows_a_Refunded_badge_and_no_form()
    {
        await SignInUserAsync("alice");
        ScriptOrder(status: "Cancelled", payment: "Refunded");

        var html = await GetHtmlAsync("/Orders/1");

        Assert.Contains("data-payment-status=\"Refunded\"", html);
        Assert.False(HtmlHelpers.FormAction(html, "/Orders/1/Pay"));
    }

    [Theory]
    [InlineData("Confirmed")]
    [InlineData("Cancelled")]
    public async Task Unpaid_order_that_is_not_pending_has_no_form(string status)
    {
        await SignInUserAsync("alice");
        ScriptOrder(status: status);

        var html = await GetHtmlAsync("/Orders/1");

        Assert.False(HtmlHelpers.FormAction(html, "/Orders/1/Pay"));
    }

    [Fact]
    public async Task An_admin_viewing_another_users_order_gets_no_pay_form()
    {
        await SignInAdminAsync("root");
        ScriptOrder(userName: "bob");

        var html = await GetHtmlAsync("/Orders/1");

        Assert.False(HtmlHelpers.FormAction(html, "/Orders/1/Pay"));
        Assert.True(HtmlHelpers.FormAction(html, "/Orders/1/Status"));
    }

    [Fact]
    public async Task Json_without_paymentStatus_renders_as_Unpaid()
    {
        await SignInUserAsync("alice");
        Fake.On(HttpMethod.Get, "api/orders/1", FakeResponse.Json(200,
            ApiJson.Order(1, "Pending", 2, "alice", ApiJson.OrderLine(1, 5, "Bolt", 12.5m, 2))));

        var html = await GetHtmlAsync("/Orders/1");

        Assert.Contains("data-payment-status=\"Unpaid\"", html);
        Assert.True(HtmlHelpers.FormAction(html, "/Orders/1/Pay"));
    }

    // ---------- POST Pay ----------

    [Fact]
    public async Task Pay_success_redirects_to_details_with_a_flash_and_sends_token_and_key()
    {
        await SignInUserAsync("alice", token: "tok");
        Fake.On(HttpMethod.Post, "api/orders/1/pay", FakeResponse.Json(200,
            new { id = 7, orderId = 1, amount = 25m, currency = "USD", status = "Succeeded", providerReference = "fake_ch_x",
                  failureReason = (string?)null, createdAtUtc = DateTime.UtcNow, updatedAtUtc = DateTime.UtcNow }));
        ScriptOrder(payment: "Paid");

        var resp = await PostFormAsync("/Orders/1/Pay", PayFields("tok_mastercard", "key-abc"), TokenPage);

        AssertRedirect(resp, "/Orders/1");
        var req = Fake.Single(HttpMethod.Post, "api/orders/1/pay");
        Assert.Equal("Bearer tok", req.Authorization);
        var body = req.Json();
        Assert.Equal("tok_mastercard", body.GetProperty("paymentToken").GetString());
        Assert.Equal("key-abc", body.GetProperty("idempotencyKey").GetString());
        Assert.Equal("Payment received.", HtmlHelpers.Alert(await GetHtmlAsync("/Orders/1"), "success"));
    }

    [Fact]
    public async Task Pay_declined_402_shows_the_reason()
    {
        await SignInUserAsync("alice");
        Fake.On(HttpMethod.Post, "api/orders/1/pay", FakeResponse.Json(402, new
        {
            title = "Payment declined", status = 402, detail = "Payment declined: card_declined.",
            paymentId = 3, reason = "card_declined"
        }));
        ScriptOrder();

        AssertRedirect(await PostFormAsync("/Orders/1/Pay", PayFields("tok_decline"), TokenPage), "/Orders/1");

        Assert.Equal("Payment declined: card_declined.", HtmlHelpers.Alert(await GetHtmlAsync("/Orders/1"), "danger"));
    }

    [Fact]
    public async Task Pay_409_shows_the_detail()
    {
        await SignInUserAsync("alice");
        Fake.On(HttpMethod.Post, "api/orders/1/pay", FakeResponse.Json(409, ApiJson.ConflictProblem("The order is already paid.")));
        ScriptOrder(payment: "Paid");

        await PostFormAsync("/Orders/1/Pay", PayFields(), TokenPage);

        Assert.Equal("The order is already paid.", HtmlHelpers.Alert(await GetHtmlAsync("/Orders/1"), "danger"));
    }

    [Fact]
    public async Task Pay_400_shows_the_validation_message()
    {
        await SignInUserAsync("alice");
        Fake.On(HttpMethod.Post, "api/orders/1/pay",
            FakeResponse.Json(400, ApiJson.ValidationProblem("PaymentToken", "Payment token is invalid.")));
        ScriptOrder();

        await PostFormAsync("/Orders/1/Pay", PayFields("bogus"), TokenPage);

        Assert.Equal("Payment token is invalid.", HtmlHelpers.Alert(await GetHtmlAsync("/Orders/1"), "danger"));
    }

    [Fact]
    public async Task Pay_404_shows_order_not_found()
    {
        await SignInUserAsync("alice");
        Fake.On(HttpMethod.Post, "api/orders/1/pay", FakeResponse.Status(404));
        ScriptOrder();

        await PostFormAsync("/Orders/1/Pay", PayFields(), TokenPage);

        Assert.Equal("Order not found.", HtmlHelpers.Alert(await GetHtmlAsync("/Orders/1"), "danger"));
    }

    [Fact]
    public async Task Pay_429_shows_the_too_many_requests_message()
    {
        await SignInUserAsync("alice");
        Fake.On(HttpMethod.Post, "api/orders/1/pay", FakeResponse.Status(429));
        ScriptOrder();

        await PostFormAsync("/Orders/1/Pay", PayFields(), TokenPage);

        Assert.Equal("Too many requests. Please try again later.", HtmlHelpers.Alert(await GetHtmlAsync("/Orders/1"), "danger"));
    }

    [Fact]
    public async Task Pay_502_shows_an_unavailable_message_on_the_details_page()
    {
        await SignInUserAsync("alice");
        Fake.On(HttpMethod.Post, "api/orders/1/pay", FakeResponse.Json(502, new { title = "Payment provider error", status = 502 }));
        ScriptOrder();

        await PostFormAsync("/Orders/1/Pay", PayFields("tok_error"), TokenPage);

        var alert = HtmlHelpers.Alert(await GetHtmlAsync("/Orders/1"), "danger");
        Assert.NotNull(alert);
        Assert.Contains("unavailable", alert, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Pay_401_signs_out_and_redirects_to_login()
    {
        await SignInUserAsync("alice");
        Fake.On(HttpMethod.Post, "api/orders/1/pay", FakeResponse.Status(401));
        Fake.On(HttpMethod.Post, "api/account/refresh", FakeResponse.Status(401));

        var resp = await PostFormAsync("/Orders/1/Pay", PayFields(), TokenPage);

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("/Account/Login", resp.Headers.Location!.ToString());
    }

    // ---------- Lists ----------

    [Fact]
    public async Task Index_shows_a_Payment_column_with_badges()
    {
        await SignInUserAsync();
        Fake.On(HttpMethod.Get, "api/orders", FakeResponse.Json(200, ApiJson.Paged(new[]
        {
            ApiJson.OrderSummary(11, "Pending", paymentStatus: "Unpaid"),
            ApiJson.OrderSummary(12, "Pending", paymentStatus: "Paid"),
            ApiJson.OrderSummary(13, "Cancelled", paymentStatus: "Refunded"),
            ApiJson.OrderSummary(14, "Pending")   // field missing: defaults to Unpaid
        }, pageSize: 10)));

        var html = await GetHtmlAsync("/Orders");

        Assert.True(HtmlHelpers.HasText(html, "Payment"));
        Assert.Contains("data-payment-status=\"Paid\"", html);
        Assert.Contains("data-payment-status=\"Refunded\"", html);
        Assert.Equal(2, Regex.Matches(html, "data-payment-status=\"Unpaid\"").Count);
        Assert.Contains("badge bg-warning text-dark\">Pending", html);   // the status badge is unchanged
    }

    [Fact]
    public async Task Manage_shows_a_Payment_column_with_badges()
    {
        await SignInAdminAsync();
        Fake.On(HttpMethod.Get, "api/orders/all", FakeResponse.Json(200, ApiJson.Paged(new[]
        {
            ApiJson.OrderSummary(21, "Pending", userName: "bob", paymentStatus: "Paid"),
            ApiJson.OrderSummary(22, "Pending", userName: "eve", paymentStatus: "Unpaid")
        }, pageSize: 10)));

        var html = await GetHtmlAsync("/Orders/Manage");

        Assert.True(HtmlHelpers.HasText(html, "Payment"));
        Assert.Contains("data-payment-status=\"Paid\"", html);
        Assert.Contains("data-payment-status=\"Unpaid\"", html);
    }
}
