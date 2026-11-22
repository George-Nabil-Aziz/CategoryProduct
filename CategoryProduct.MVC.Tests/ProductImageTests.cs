using System.Net;

namespace CategoryProduct.MVC.Tests;

public class ProductImageTests : MvcTestBase
{
    public ProductImageTests(MvcFactory factory) : base(factory) { }

    private static readonly byte[] Png =
        { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x01, 0x02, 0xFE, 0xFF, 0x10, 0x20, 0x30 };

    private void ScriptProduct(int id = 1, string? imageUrl = null)
    {
        Fake.On(HttpMethod.Get, $"api/product/{id}", FakeResponse.Json(200, ApiJson.Product(id, "Pen", 12m, 7, 2, imageUrl)));
        Fake.On(HttpMethod.Get, "api/category/2", FakeResponse.Json(200, ApiJson.Category(2, "Toys")));
    }

    private static string PathOf(HttpResponseMessage r)
    {
        var l = r.Headers.Location!;
        return l.IsAbsoluteUri ? l.AbsolutePath : l.OriginalString.Split('?')[0];
    }

    private static int IndexOf(byte[] haystack, byte[] needle) => haystack.AsSpan().IndexOf(needle);

    // ---------- Image ----------

    [Fact]
    public async Task Image_proxies_bytes_with_safe_headers_for_anonymous()
    {
        Fake.On(HttpMethod.Get, "api/product/1/image", FakeResponse.Bytes(200, Png, "image/png"));
        using var r = await GetAsync("/Products/Image/1");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(Png, await r.Content.ReadAsByteArrayAsync());
        Assert.Equal("image/png", r.Content.Headers.ContentType?.MediaType);
        Assert.Contains("nosniff", r.Headers.GetValues("X-Content-Type-Options"));
        Assert.True(r.Headers.CacheControl?.NoCache);
        Assert.Single(Fake.Calls(HttpMethod.Get, "api/product/1/image"));
    }

    [Fact]
    public async Task Image_api_404_is_404()
    {
        Fake.On(HttpMethod.Get, "api/product/1/image", FakeResponse.Status(404));
        using var r = await GetAsync("/Products/Image/1");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task Image_api_5xx_shows_service_unavailable()
    {
        Fake.On(HttpMethod.Get, "api/product/1/image", FakeResponse.Status(502));
        using var r = await GetAsync("/Products/Image/1");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
        Assert.True(HtmlHelpers.HasText(await r.Content.ReadAsStringAsync(), "Service unavailable"));
    }

    // ---------- UploadImage ----------

    [Fact]
    public async Task Upload_valid_png_forwards_multipart_with_bearer_and_redirects_to_details()
    {
        await SignInAdminAsync(token: "tok-a");
        ScriptProduct(1);
        Fake.On(HttpMethod.Post, "api/product/1/image", FakeResponse.Status(204));
        var resp = await PostMultipartAsync("/Products/UploadImage/1", new Dictionary<string, string>(),
            "pic.png", "image/png", Png, tokenPageUrl: "/Products/Details/1");
        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
        Assert.Equal("/Products/Details/1", PathOf(resp), ignoreCase: true);

        var call = Fake.Single(HttpMethod.Post, "api/product/1/image");
        Assert.Equal("Bearer tok-a", call.Authorization);
        Assert.StartsWith("multipart/form-data", call.ContentType);
        var text = call.BodyText.Replace("\"", "");
        Assert.Contains("name=file", text);
        Assert.Contains("filename=pic.png", text);
        Assert.Contains("image/png", text);
        Assert.True(IndexOf(call.BodyBytes, Png) >= 0, "uploaded bytes not found in forwarded body");
    }

    [Fact]
    public async Task Upload_without_file_shows_message_and_does_not_call_upload()
    {
        await SignInAdminAsync();
        ScriptProduct(1);
        var resp = await PostFormAsync("/Products/UploadImage/1", new Dictionary<string, string>(),
            tokenPageUrl: "/Products/Details/1");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.True(HtmlHelpers.HasText(await resp.Content.ReadAsStringAsync(), "Please choose an image file."));
        Assert.Empty(Fake.Calls(HttpMethod.Post, "api/product/1/image"));
    }

    [Fact]
    public async Task Upload_api_400_rerenders_details_with_message()
    {
        await SignInAdminAsync();
        ScriptProduct(1);
        Fake.On(HttpMethod.Post, "api/product/1/image", FakeResponse.Json(400,
            ApiJson.ValidationProblem("file", "Only JPG, PNG or WebP images are allowed.")));
        var resp = await PostMultipartAsync("/Products/UploadImage/1", new Dictionary<string, string>(),
            "pic.png", "image/png", Png, tokenPageUrl: "/Products/Details/1");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.True(HtmlHelpers.HasText(await resp.Content.ReadAsStringAsync(), "Only JPG, PNG or WebP images are allowed."));
    }

    [Fact]
    public async Task Upload_api_404_is_404()
    {
        await SignInAdminAsync();
        ScriptProduct(1);
        Fake.On(HttpMethod.Post, "api/product/1/image", FakeResponse.Status(404));
        var resp = await PostMultipartAsync("/Products/UploadImage/1", new Dictionary<string, string>(),
            "pic.png", "image/png", Png, tokenPageUrl: "/Products/Details/1");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Upload_api_409_shows_detail()
    {
        await SignInAdminAsync();
        ScriptProduct(1);
        Fake.On(HttpMethod.Post, "api/product/1/image", FakeResponse.Json(409, ApiJson.ConflictProblem("Image conflict on upload.")));
        var resp = await PostMultipartAsync("/Products/UploadImage/1", new Dictionary<string, string>(),
            "pic.png", "image/png", Png, tokenPageUrl: "/Products/Details/1");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.True(HtmlHelpers.HasText(await resp.Content.ReadAsStringAsync(), "Image conflict on upload."));
    }

    [Fact]
    public async Task Upload_by_non_admin_is_access_denied()
    {
        await SignInUserAsync();
        var resp = await PostMultipartAsync("/Products/UploadImage/1", new Dictionary<string, string>(),
            "pic.png", "image/png", Png, tokenPageUrl: "/Account/AccessDenied");
        Assert.True((int)resp.StatusCode is >= 300 and < 400);
        Assert.Equal("/Account/AccessDenied", PathOf(resp), ignoreCase: true);
        Assert.Empty(Fake.Requests);
    }

    // ---------- RemoveImage ----------

    [Fact]
    public async Task Remove_ok_deletes_with_bearer_and_redirects_to_details()
    {
        await SignInAdminAsync(token: "tok-a");
        ScriptProduct(1, imageUrl: "http://fake-api/i.png");
        Fake.On(HttpMethod.Delete, "api/product/1/image", FakeResponse.Status(204));
        var resp = await PostFormAsync("/Products/RemoveImage/1", new Dictionary<string, string>(),
            tokenPageUrl: "/Products/Details/1");
        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
        Assert.Equal("/Products/Details/1", PathOf(resp), ignoreCase: true);
        Assert.Equal("Bearer tok-a", Fake.Single(HttpMethod.Delete, "api/product/1/image").Authorization);
    }

    [Fact]
    public async Task Remove_api_404_is_404()
    {
        await SignInAdminAsync();
        ScriptProduct(1, imageUrl: "http://fake-api/i.png");
        Fake.On(HttpMethod.Delete, "api/product/1/image", FakeResponse.Status(404));
        var resp = await PostFormAsync("/Products/RemoveImage/1", new Dictionary<string, string>(),
            tokenPageUrl: "/Products/Details/1");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Remove_api_400_rerenders_details_with_message()
    {
        await SignInAdminAsync();
        ScriptProduct(1, imageUrl: "http://fake-api/i.png");
        Fake.On(HttpMethod.Delete, "api/product/1/image", FakeResponse.Json(400, ApiJson.ValidationProblem("image", "Cannot remove image.")));
        var resp = await PostFormAsync("/Products/RemoveImage/1", new Dictionary<string, string>(),
            tokenPageUrl: "/Products/Details/1");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.True(HtmlHelpers.HasText(await resp.Content.ReadAsStringAsync(), "Cannot remove image."));
    }

    [Fact]
    public async Task Remove_by_non_admin_is_access_denied()
    {
        await SignInUserAsync();
        var resp = await PostFormAsync("/Products/RemoveImage/1", new Dictionary<string, string>(),
            tokenPageUrl: "/Account/AccessDenied");
        Assert.True((int)resp.StatusCode is >= 300 and < 400);
        Assert.Equal("/Account/AccessDenied", PathOf(resp), ignoreCase: true);
        Assert.Empty(Fake.Requests);
    }

    [Fact]
    public async Task RemoveImage_api_409_shows_detail()
    {
        await SignInAdminAsync();
        ScriptProduct(1, imageUrl: "http://fake-api/i.png");
        Fake.On(HttpMethod.Delete, "api/product/1/image", FakeResponse.Json(409, ApiJson.ConflictProblem("Image conflict on remove.")));
        var resp = await PostFormAsync("/Products/RemoveImage/1", new Dictionary<string, string>(),
            tokenPageUrl: "/Products/Details/1");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.True(HtmlHelpers.HasText(await resp.Content.ReadAsStringAsync(), "Image conflict on remove."));
    }
}
