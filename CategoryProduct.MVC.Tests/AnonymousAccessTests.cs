using System.Net;

namespace CategoryProduct.MVC.Tests;

public class AnonymousAccessTests : MvcTestBase
{
    public AnonymousAccessTests(MvcFactory factory) : base(factory) { }

    private static string PathOf(HttpResponseMessage r)
    {
        var l = r.Headers.Location!;
        return l.IsAbsoluteUri ? l.AbsolutePath : l.OriginalString.Split('?')[0];
    }

    private static void AssertAccessDenied(HttpResponseMessage r)
    {
        Assert.True((int)r.StatusCode is >= 300 and < 400, $"Expected redirect, got {(int)r.StatusCode}");
        Assert.Equal("/Account/AccessDenied", PathOf(r), ignoreCase: true);
    }

    public static IEnumerable<object[]> AnonGets() =>
        new[]
        {
            "/Cart", "/Orders", "/Orders/1", "/Orders/Manage", "/Inventory", "/Inventory/Product/1", "/Users",
            "/Categories/Create", "/Categories/Edit/1", "/Categories/Delete/1",
            "/Products/Create", "/Products/Edit/1", "/Products/Delete/1"
        }.Select(u => new object[] { u });

    [Theory]
    [MemberData(nameof(AnonGets))]
    public async Task Anonymous_get_redirects_to_login(string url)
    {
        var resp = await GetAsync(url);
        AssertLoginRedirect(resp, url);
        Assert.Empty(Fake.Requests);
    }

    public static IEnumerable<object[]> AnonPosts() =>
        new[]
        {
            "/Cart/Add", "/Cart/Checkout", "/Orders/1/Cancel", "/Inventory/Product/1", "/Users/ChangeRole",
            "/Products/UploadImage/1", "/Products/RemoveImage/1", "/Categories/Create"
        }.Select(u => new object[] { u });

    [Theory]
    [MemberData(nameof(AnonPosts))]
    public async Task Anonymous_post_redirects_to_login(string url)
    {
        var resp = await PostFormAsync(url, new Dictionary<string, string>(), tokenPageUrl: "/Account/Login");
        AssertLoginRedirect(resp, url);
        Assert.Empty(Fake.Requests);
    }

    public static IEnumerable<object[]> UserGets() =>
        new[] { "/Users", "/Inventory", "/Orders/Manage", "/Categories/Create", "/Products/Create", "/Products/Edit/1" }
            .Select(u => new object[] { u });

    [Theory]
    [MemberData(nameof(UserGets))]
    public async Task User_get_on_admin_pages_is_access_denied(string url)
    {
        await SignInUserAsync();
        var resp = await GetAsync(url);
        AssertAccessDenied(resp);
        Assert.Empty(Fake.Requests);
    }

    public static IEnumerable<object[]> UserPosts() =>
        new[] { "/Orders/1/Status", "/Users/ChangeRole", "/Products/UploadImage/1" }.Select(u => new object[] { u });

    [Theory]
    [MemberData(nameof(UserPosts))]
    public async Task User_post_on_admin_actions_is_access_denied(string url)
    {
        await SignInUserAsync();
        var resp = await PostFormAsync(url, new Dictionary<string, string>(), tokenPageUrl: "/Account/AccessDenied");
        AssertAccessDenied(resp);
        Assert.Empty(Fake.Requests);
    }

    public static IEnumerable<object[]> PublicGets() =>
        new[]
        {
            "/", "/Categories", "/Products", "/Products/Details/1", "/Categories/Details/1", "/Products/Image/1",
            "/Account/Login", "/Account/Register", "/Account/AccessDenied"
        }.Select(u => new object[] { u });

    [Theory]
    [MemberData(nameof(PublicGets))]
    public async Task Public_pages_are_not_login_redirects(string url)
    {
        Fake.On(HttpMethod.Get, "api/category", FakeResponse.Json(200, ApiJson.Paged(new[] { ApiJson.Category(1, "Books") })));
        Fake.On(HttpMethod.Get, "api/category/1", FakeResponse.Json(200, ApiJson.Category(1, "Books")));
        Fake.On(HttpMethod.Get, "api/category/1/products", FakeResponse.Json(200, Array.Empty<object>()));
        Fake.On(HttpMethod.Get, "api/product", FakeResponse.Json(200, ApiJson.Paged(new[] { ApiJson.Product(1, "Pen") })));
        Fake.On(HttpMethod.Get, "api/product/1", FakeResponse.Json(200, ApiJson.Product(1, "Pen")));
        Fake.On(HttpMethod.Get, "api/product/1/image", FakeResponse.Bytes(200, new byte[] { 1, 2, 3 }, "image/png"));

        var resp = await GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }
}
