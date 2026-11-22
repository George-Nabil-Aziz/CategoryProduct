using System.Net;

namespace CategoryProduct.MVC.Tests;

/// <summary>Unhandled exceptions in Production produce the friendly error page (500) without leaking details.</summary>
public class ErrorPageTests : MvcTestBase, IClassFixture<ProductionMvcFactory>
{
    private const string Marker = "secret-marker-9f3a";

    public ErrorPageTests(ProductionMvcFactory factory) : base(factory) { }

    private async Task<(HttpResponseMessage Resp, string Html)> GetErrorAsync()
    {
        Fake.Throw(HttpMethod.Get, "api/category", new InvalidOperationException(Marker));
        var resp = await GetAsync("/Categories");
        return (resp, await resp.Content.ReadAsStringAsync());
    }

    private static void AssertFriendly(HttpResponseMessage resp, string html)
    {
        Assert.Equal(HttpStatusCode.InternalServerError, resp.StatusCode);
        Assert.True(HtmlHelpers.HasText(html, "Something went wrong"));
        Assert.Contains("Back to Categories", html);
        Assert.Matches("<a [^>]*href=\"/\"[^>]*>Back to Categories</a>", html);
        Assert.DoesNotContain(Marker, html);
        Assert.DoesNotContain("InvalidOperationException", html);
        Assert.DoesNotContain("   at ", html);
        Assert.DoesNotContain("Development Mode", html);
    }

    [Fact]
    public async Task Unhandled_exception_on_get_shows_friendly_500_without_details()
    {
        var (resp, html) = await GetErrorAsync();
        AssertFriendly(resp, html);
    }

    [Fact]
    public async Task Error_page_shows_request_id()
    {
        var (_, html) = await GetErrorAsync();
        Assert.Matches("Request ID:</strong>\\s*<code>[^<]+</code>", html);
    }

    [Fact]
    public async Task Error_page_is_not_cacheable()
    {
        var (resp, _) = await GetErrorAsync();
        Assert.Contains("no-store", resp.Headers.CacheControl?.ToString() ?? "");
    }

    [Fact]
    public async Task Unhandled_exception_on_post_shows_friendly_500()
    {
        await SignInAdminAsync();
        Fake.On(HttpMethod.Get, "api/category", FakeResponse.Json(200, ApiJson.Paged(Array.Empty<object>())));
        var token = HtmlHelpers.ExtractAntiforgeryToken(await GetHtmlAsync("/Categories/Create"));
        Fake.Throw(HttpMethod.Post, "api/category", new InvalidOperationException(Marker));
        var resp = await Client.PostAsync("/Categories/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Name"] = "Gadgets",
            ["__RequestVerificationToken"] = token
        }));
        AssertFriendly(resp, await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Anonymous_direct_request_to_error_is_500_not_login_redirect()
    {
        using var resp = await GetAsync("/Home/Error");
        Assert.Equal(HttpStatusCode.InternalServerError, resp.StatusCode);
        Assert.True(HtmlHelpers.HasText(await resp.Content.ReadAsStringAsync(), "Something went wrong"));
    }

    [Fact]
    public async Task Signed_in_user_keeps_layout_on_error_page()
    {
        await SignInUserAsync("alice");
        var (resp, html) = await GetErrorAsync();
        Assert.Equal(HttpStatusCode.InternalServerError, resp.StatusCode);
        Assert.True(HtmlHelpers.HasText(html, "Hello, alice"));
    }

    [Theory]
    [InlineData("/Home")]
    [InlineData("/Home/Index")]
    public async Task Home_redirects_to_categories(string url)
    {
        using var resp = await GetAsync(url);
        AssertRedirect(resp, "/");
    }
}
