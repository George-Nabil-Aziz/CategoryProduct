using System.Net;
using CategoryProduct.MVC.Services;

namespace CategoryProduct.MVC.Tests;

public class TooManyRequestsTests : MvcTestBase
{
    public TooManyRequestsTests(MvcFactory factory) : base(factory) { }

    private static async Task AssertTooManyRequests(HttpResponseMessage resp)
    {
        var html = await resp.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.TooManyRequests, resp.StatusCode);
        Assert.True(HtmlHelpers.HasText(html, "Too many requests"));
        Assert.True(HtmlHelpers.HasText(html, ApiRateLimit.Message));
        Assert.Contains("location.reload()", html);
        Assert.False(HtmlHelpers.HasText(html, "HttpRequestException"));
        Assert.False(HtmlHelpers.HasText(html, "Response status code"));
    }

    [Fact]
    public async Task Products_list_429_shows_page()
    {
        Fake.On(HttpMethod.Get, "api/product", FakeResponse.Status(429));
        Fake.On(HttpMethod.Get, "api/category", FakeResponse.Json(200, ApiJson.Paged(new[] { ApiJson.Category(1, "Books") })));
        using var resp = await GetAsync("/Products");
        await AssertTooManyRequests(resp);
    }

    [Fact]
    public async Task Product_details_429_shows_page()
    {
        Fake.On(HttpMethod.Get, "api/product/5", FakeResponse.Status(429));
        using var resp = await GetAsync("/Products/Details/5");
        await AssertTooManyRequests(resp);
    }

    [Fact]
    public async Task Category_lookup_for_create_form_429_shows_page()
    {
        await SignInAdminAsync();
        Fake.On(HttpMethod.Get, "api/category", FakeResponse.Status(429));
        using var resp = await GetAsync("/Products/Create");
        await AssertTooManyRequests(resp);
    }

    [Fact]
    public async Task Details_404_is_still_not_found()
    {
        Fake.On(HttpMethod.Get, "api/product/5", FakeResponse.Status(404));
        using var resp = await GetAsync("/Products/Details/5");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Details_503_is_still_service_unavailable()
    {
        Fake.On(HttpMethod.Get, "api/product/5", FakeResponse.Status(503));
        using var resp = await GetAsync("/Products/Details/5");
        var html = await resp.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
        Assert.True(HtmlHelpers.HasText(html, ApiUnavailableException.FriendlyMessage));
    }
}
