using System.Net;

namespace CategoryProduct.MVC.Tests;

/// <summary>Self-tests of the test infrastructure (FakeApi, MvcFactory, MvcTestBase, helpers).</summary>
public class InfrastructureTests : MvcTestBase
{
    public InfrastructureTests(MvcFactory factory) : base(factory) { }

    private static HttpRequestMessage Req(HttpMethod m, string url, HttpContent? c = null) =>
        new(m, "http://fake-api/" + url) { Content = c };

    private static async Task<HttpResponseMessage> Send(FakeApi fake, HttpRequestMessage req)
    {
        using var invoker = new HttpMessageInvoker(fake, disposeHandler: false);
        return await invoker.SendAsync(req, CancellationToken.None);
    }

    [Fact]
    public async Task Factory_boots_and_login_page_has_antiforgery_token()
    {
        var html = await GetHtmlAsync("/Account/Login");
        Assert.False(string.IsNullOrEmpty(HtmlHelpers.ExtractAntiforgeryToken(html)));
        Assert.True(HtmlHelpers.FormAction(html, "/Account/Login"));
    }

    [Fact]
    public async Task FakeApi_routes_by_method_and_path_and_later_registration_wins()
    {
        Fake.On(HttpMethod.Get, "api/category", FakeResponse.Json(200, new { a = 1 }));
        Fake.On(HttpMethod.Get, "/API/Category/", FakeResponse.Status(404));
        using var r = await Send(Fake, Req(HttpMethod.Get, "api/category"));
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task FakeApi_sequence_returns_nth_then_repeats_last()
    {
        Fake.OnSequence(HttpMethod.Get, "api/cart", FakeResponse.Status(200), FakeResponse.Status(409));
        var codes = new List<int>();
        for (var i = 0; i < 3; i++)
        {
            using var r = await Send(Fake, Req(HttpMethod.Get, "api/cart"));
            codes.Add((int)r.StatusCode);
        }
        Assert.Equal(new[] { 200, 409, 409 }, codes);
    }

    [Fact]
    public async Task FakeApi_query_predicate_selects_route()
    {
        Fake.On(HttpMethod.Get, "api/orders", FakeResponse.Raw(200, "p1", "text/plain"));
        Fake.OnQuery(HttpMethod.Get, "api/orders", q => q.Contains("page=2"), FakeResponse.Raw(200, "p2", "text/plain"));
        using var a = await Send(Fake, Req(HttpMethod.Get, "api/orders?page=2"));
        using var b = await Send(Fake, Req(HttpMethod.Get, "api/orders?page=1"));
        Assert.Equal("p2", await a.Content.ReadAsStringAsync());
        Assert.Equal("p1", await b.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task FakeApi_throw_and_hang_raise_exceptions()
    {
        Fake.Throw(HttpMethod.Get, "api/a");
        Fake.Hang(HttpMethod.Get, "api/b");
        await Assert.ThrowsAsync<HttpRequestException>(() => Send(Fake, Req(HttpMethod.Get, "api/a")));
        await Assert.ThrowsAsync<TaskCanceledException>(() => Send(Fake, Req(HttpMethod.Get, "api/b")));
    }

    [Fact]
    public async Task FakeApi_unscripted_call_throws_naming_method_and_path_and_is_recorded()
    {
        Fake.On(HttpMethod.Get, "api/known", FakeResponse.Status(200));
        var ex = await Assert.ThrowsAsync<FakeApiUnmatchedException>(() => Send(Fake, Req(HttpMethod.Post, "api/foo?x=1")));
        Assert.Contains("POST api/foo?x=1", ex.Message);
        Assert.Contains("GET api/known", ex.Message);
        Assert.Single(Fake.Requests);
    }

    [Fact]
    public async Task FakeApi_records_authorization_and_json_and_multipart_bodies()
    {
        Fake.On(HttpMethod.Post, "api/x", FakeResponse.Status(204));
        var json = new StringContent("{\"quantity\":3}", System.Text.Encoding.UTF8, "application/json");
        var req = Req(HttpMethod.Post, "api/x", json);
        req.Headers.Authorization = new("Bearer", "tok");
        using (await Send(Fake, req)) { }

        var multipart = new MultipartFormDataContent();
        var file = new ByteArrayContent(new byte[] { 1, 2, 3 });
        file.Headers.ContentType = new("image/png");
        multipart.Add(file, "file", "x.png");
        using (await Send(Fake, Req(HttpMethod.Post, "api/x", multipart))) { }

        var calls = Fake.Calls(HttpMethod.Post, "api/x");
        Assert.Equal(2, calls.Count);
        Assert.Equal("Bearer tok", calls[0].Authorization);
        Assert.Equal(3, calls[0].Json().GetProperty("quantity").GetInt32());
        Assert.Null(calls[1].Authorization);
        Assert.Contains("name=file; filename=x.png", calls[1].BodyText.Replace("\"", ""));
        Assert.Contains("image/png", calls[1].BodyText);
        Assert.True(calls[1].BodyBytes.Length > 3);
    }

    [Fact]
    public async Task FakeApi_single_asserts_exactly_one()
    {
        Fake.On(HttpMethod.Get, "api/s", FakeResponse.Status(200));
        using (await Send(Fake, Req(HttpMethod.Get, "api/s"))) { }
        Assert.Equal("api/s", Fake.Single(HttpMethod.Get, "api/s").Path);
        using (await Send(Fake, Req(HttpMethod.Get, "api/s"))) { }
        Assert.ThrowsAny<Exception>(() => Fake.Single(HttpMethod.Get, "api/s"));
    }

    [Fact]
    public async Task SignInAdmin_then_Users_page_renders_and_sends_bearer()
    {
        await SignInAdminAsync(token: "tok-admin");
        Fake.On(HttpMethod.Get, "api/users", FakeResponse.Json(200,
            ApiJson.Paged(new[] { ApiJson.User(1, "alice"), ApiJson.User(2, "root", "Admin") })));

        var html = await GetHtmlAsync("/Users");

        Assert.True(HtmlHelpers.HasText(html, "alice"));
        var call = Fake.Single(HttpMethod.Get, "api/users");
        Assert.Equal("Bearer tok-admin", call.Authorization);
    }

    [Fact]
    public async Task SignInUser_is_not_admin()
    {
        await SignInUserAsync();
        using var r = await GetAsync("/Users");
        Assert.Contains("/Account/AccessDenied", r.Headers.Location?.OriginalString);
        Assert.Empty(Fake.Requests);
    }

    [Fact]
    public async Task Anonymous_protected_page_redirects_to_login_with_return_url()
    {
        using var r = await GetAsync("/Users");
        AssertLoginRedirect(r, "/Users");
    }

    [Fact]
    public async Task Test_starts_with_empty_log_and_no_cookies_first()
    {
        Assert.Empty(Fake.Requests);
        await SignInAdminAsync();
        Fake.On(HttpMethod.Get, "api/users", FakeResponse.Json(200, ApiJson.Paged(new[] { ApiJson.User(1, "a") })));
        await GetHtmlAsync("/Users");
        Assert.Single(Fake.Requests);
    }

    [Fact]
    public async Task Test_starts_with_empty_log_and_no_cookies_second()
    {
        Assert.Empty(Fake.Requests);
        using var r = await GetAsync("/Users");
        AssertLoginRedirect(r, "/Users");
    }

    [Fact]
    public async Task Register_posts_with_antiforgery_and_redirects_home()
    {
        Fake.On(HttpMethod.Post, "api/account/register", FakeResponse.Status(201));
        ScriptLogin("User");
        var resp = await PostFormAsync("/Account/Register", new Dictionary<string, string>
            { ["UserName"] = "bob", ["Password"] = "P@ssw0rd!", ["ConfirmPassword"] = "P@ssw0rd!" });
        AssertRedirect(resp, "/");
    }

    [Fact]
    public void ApiJson_builders_produce_camelCase_shapes()
    {
        var login = FakeResponse.Json(200, ApiJson.Login("Admin", 60, "t", "r"));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var cart = System.Text.Json.JsonSerializer.Serialize(
            ApiJson.Cart(ApiJson.CartLine(1, "p", 2.5m, 2)), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.Contains("\"itemCount\":2", cart);
        Assert.Contains("\"total\":5", cart);
    }
}
