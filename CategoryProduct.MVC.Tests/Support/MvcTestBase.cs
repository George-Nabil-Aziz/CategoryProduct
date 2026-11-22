using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.WebUtilities;

namespace CategoryProduct.MVC.Tests;

/// <summary>
/// Base class for MVC tests. xunit creates one instance per test, so every test gets an empty <see cref="Fake"/>
/// (routes and request log) and a fresh <see cref="Client"/> (no cookies, no auto-redirect).
/// </summary>
public abstract class MvcTestBase : IClassFixture<MvcFactory>, IDisposable
{
    protected MvcTestBase(MvcFactory factory)
    {
        Factory = factory;
        Fake = factory.Fake;
        Fake.Reset();
        Client = NewAnonymousClient();
    }

    protected MvcFactory Factory { get; }

    /// <summary>The scripted API (reset for each test).</summary>
    protected FakeApi Fake { get; }

    /// <summary>Browser-like client: cookies kept, redirects NOT followed.</summary>
    protected HttpClient Client { get; }

    private readonly List<HttpClient> _extraClients = new();

    /// <summary>A new client with its own empty cookie jar (anonymous). Disposed with the test.</summary>
    protected HttpClient NewAnonymousClient()
    {
        var c = Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        _extraClients.Add(c);
        return c;
    }

    public virtual void Dispose()
    {
        foreach (var c in _extraClients) c.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>GET without following redirects.</summary>
    protected Task<HttpResponseMessage> GetAsync(string url) => Client.GetAsync(url);

    /// <summary>GET, assert 200, return the body.</summary>
    protected async Task<string> GetHtmlAsync(string url)
    {
        using var r = await Client.GetAsync(url);
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == HttpStatusCode.OK, $"GET {url} returned {(int)r.StatusCode}. Body: {Truncate(body)}");
        return body;
    }

    /// <summary>
    /// POST a url-encoded form. Fetches an antiforgery token from GET <paramref name="tokenPageUrl"/> (default: <paramref name="url"/>)
    /// first, which also sets the antiforgery cookie, and adds it as __RequestVerificationToken. The token-page GET is
    /// not scripted for the API unless that page calls the API: script those routes first.
    /// </summary>
    protected async Task<HttpResponseMessage> PostFormAsync(string url, IEnumerable<KeyValuePair<string, string>> fields,
        string? tokenPageUrl = null)
    {
        var token = HtmlHelpers.ExtractAntiforgeryToken(await GetHtmlAsync(tokenPageUrl ?? url));
        var all = fields.Append(new KeyValuePair<string, string>("__RequestVerificationToken", token));
        return await Client.PostAsync(url, new FormUrlEncodedContent(all));
    }

    /// <summary>POST a url-encoded form WITHOUT an antiforgery token (to test the 400 path).</summary>
    protected Task<HttpResponseMessage> PostFormNoTokenAsync(string url, IEnumerable<KeyValuePair<string, string>> fields) =>
        Client.PostAsync(url, new FormUrlEncodedContent(fields));

    /// <summary>POST multipart/form-data with one file part (default field name "file") plus text fields and the antiforgery token.</summary>
    protected async Task<HttpResponseMessage> PostMultipartAsync(string url, IEnumerable<KeyValuePair<string, string>> fields,
        string fileName, string contentType, byte[] bytes, string? tokenPageUrl = null, string fileField = "file")
    {
        var token = HtmlHelpers.ExtractAntiforgeryToken(await GetHtmlAsync(tokenPageUrl ?? url));
        var content = new MultipartFormDataContent();
        foreach (var (k, v) in fields) content.Add(new StringContent(v), k);
        content.Add(new StringContent(token), "__RequestVerificationToken");
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        content.Add(file, fileField, fileName);
        return await Client.PostAsync(url, content);
    }

    /// <summary>Scripts POST api/account/login to return a LoginResult for <paramref name="role"/>.</summary>
    protected void ScriptLogin(string role = "User", int expiresInSeconds = 900, string? token = null, string? refresh = null) =>
        Fake.On(HttpMethod.Post, "api/account/login", FakeResponse.Json(200, ApiJson.Login(role, expiresInSeconds, token, refresh)));

    /// <summary>Signs in as role User through the real login form; clears the fake's request log afterwards (routes stay).</summary>
    protected Task<HttpResponseMessage> SignInUserAsync(string name = "alice", int expiresInSeconds = 900,
        string? token = null, string? refresh = null) =>
        SignInAsync("User", name, expiresInSeconds, token, refresh);

    /// <summary>Signs in as role Admin through the real login form; clears the fake's request log afterwards (routes stay).</summary>
    protected Task<HttpResponseMessage> SignInAdminAsync(string name = "root", int expiresInSeconds = 900,
        string? token = null, string? refresh = null) =>
        SignInAsync("Admin", name, expiresInSeconds, token, refresh);

    private async Task<HttpResponseMessage> SignInAsync(string role, string name, int expiresInSeconds, string? token, string? refresh)
    {
        ScriptLogin(role, expiresInSeconds, token, refresh);
        var response = await PostFormAsync("/Account/Login",
            new Dictionary<string, string> { ["UserName"] = name, ["Password"] = "P@ssw0rd!" });
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith(".AspNetCore.Cookies=", StringComparison.Ordinal));
        Fake.ClearRequests();
        return response;
    }

    /// <summary>GET the Location of a redirect response (relative or absolute), still without following further redirects.</summary>
    protected Task<HttpResponseMessage> FollowAsync(HttpResponseMessage redirect)
    {
        Assert.True(redirect.Headers.Location != null, $"Expected a redirect but got {(int)redirect.StatusCode}.");
        return Client.GetAsync(redirect.Headers.Location);
    }

    private static string PathAndQuery(Uri? location) =>
        location == null ? "" : location.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString;

    /// <summary>Asserts a 3xx whose Location path+query equals <paramref name="pathAndQuery"/> (case-insensitive).</summary>
    protected static void AssertRedirect(HttpResponseMessage response, string pathAndQuery)
    {
        Assert.True((int)response.StatusCode is >= 300 and < 400, $"Expected a redirect but got {(int)response.StatusCode}.");
        Assert.Equal(pathAndQuery, PathAndQuery(response.Headers.Location), ignoreCase: true);
    }

    /// <summary>Asserts a redirect to /Account/Login whose returnUrl/ReturnUrl query value equals <paramref name="returnPath"/>.</summary>
    protected static void AssertLoginRedirect(HttpResponseMessage response, string returnPath)
    {
        Assert.True((int)response.StatusCode is >= 300 and < 400, $"Expected a redirect but got {(int)response.StatusCode}.");
        var pq = PathAndQuery(response.Headers.Location);
        var q = pq.IndexOf('?');
        var path = q < 0 ? pq : pq[..q];
        Assert.Equal("/Account/Login", path, ignoreCase: true);
        var query = QueryHelpers.ParseQuery(q < 0 ? "" : pq[q..]);
        var key = query.Keys.FirstOrDefault(k => string.Equals(k, "returnUrl", StringComparison.OrdinalIgnoreCase));
        Assert.True(key != null, $"Login redirect has no returnUrl: {pq}");
        Assert.Equal(returnPath, query[key!].ToString());
    }

    private static string Truncate(string s) => s.Length <= 500 ? s : s[..500] + "...";
}
