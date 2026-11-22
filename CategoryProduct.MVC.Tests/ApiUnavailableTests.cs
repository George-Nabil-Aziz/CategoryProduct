using System.Net;
using CategoryProduct.MVC.Services;

namespace CategoryProduct.MVC.Tests;

public class ApiUnavailableTests : MvcTestBase
{
    public ApiUnavailableTests(MvcFactory factory) : base(factory) { }

    private static async Task AssertServiceUnavailable(HttpResponseMessage resp)
    {
        var html = await resp.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
        Assert.True(HtmlHelpers.HasText(html, "Service unavailable"));
        Assert.True(HtmlHelpers.HasText(html, ApiUnavailableException.FriendlyMessage));
    }

    [Fact]
    public async Task Categories_with_connection_failure_returns_503_page()
    {
        Fake.Throw(HttpMethod.Get, "api/category", new HttpRequestException("refused"));

        using var resp = await GetAsync("/Categories");

        await AssertServiceUnavailable(resp);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    public async Task Products_with_5xx_returns_503_page(int status)
    {
        Fake.On(HttpMethod.Get, "api/product", FakeResponse.Status(status));
        Fake.On(HttpMethod.Get, "api/category", FakeResponse.Status(status));

        using var resp = await GetAsync("/Products");

        await AssertServiceUnavailable(resp);
    }

    [Fact]
    public async Task Timeout_returns_503()
    {
        Fake.Hang(HttpMethod.Get, "api/category");

        using var resp = await GetAsync("/Categories");

        await AssertServiceUnavailable(resp);
    }

    [Fact]
    public async Task Authenticated_cart_with_api_500_returns_503()
    {
        await SignInUserAsync();
        Fake.On(HttpMethod.Get, "api/cart", FakeResponse.Status(500));

        using var resp = await GetAsync("/Cart");

        await AssertServiceUnavailable(resp);
    }

    [Fact]
    public async Task Logout_with_api_down_still_signs_out()
    {
        await SignInUserAsync(refresh: "r1");
        Fake.Throw(HttpMethod.Post, "api/account/logout");

        var resp = await PostFormAsync("/Account/Logout", new Dictionary<string, string>(), "/Account/AccessDenied");

        Assert.NotEqual(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
        AssertRedirect(resp, "/");
        Assert.Single(Fake.Calls(HttpMethod.Post, "api/account/logout"));
        AssertLoginRedirect(await GetAsync("/Cart"), "/Cart");
    }

    [Fact]
    public async Task Login_with_api_down_returns_503()
    {
        Fake.Throw(HttpMethod.Post, "api/account/login");

        using var resp = await PostFormAsync("/Account/Login",
            new Dictionary<string, string> { ["UserName"] = "alice", ["Password"] = "P@ssw0rd!" });

        await AssertServiceUnavailable(resp);
    }
}
