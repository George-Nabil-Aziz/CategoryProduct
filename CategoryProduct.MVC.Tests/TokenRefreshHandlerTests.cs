using Microsoft.AspNetCore.Http;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using CategoryProduct.MVC.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CategoryProduct.MVC.Tests;

/// <summary>Unit tests for TokenRefreshHandler with a stubbed inner handler, stubbed refresh client and recording auth service.</summary>
public class TokenRefreshHandlerTests
{
    private sealed class Rig
    {
        public required TokenRefreshHandler Handler { get; init; }
        public required DefaultHttpContext Http { get; init; }
        public required StubHandler Inner { get; init; }
        public required StubHandler Refresh { get; init; }
        public required RecordingAuthenticationService Auth { get; init; }

        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage req) =>
            new HttpMessageInvoker(Handler, disposeHandler: false).SendAsync(req, CancellationToken.None);
    }

    private static ClaimsPrincipal Principal(string? expiresAt, string? refresh = "r1", bool authenticated = true)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, "alice"), new(ClaimTypes.Role, "User"), new("access_token", "old")
        };
        if (refresh != null) claims.Add(new Claim("refresh_token", refresh));
        if (expiresAt != null) claims.Add(new Claim("access_token_expires_at", expiresAt));
        return new ClaimsPrincipal(authenticated ? new ClaimsIdentity(claims, "Cookies") : new ClaimsIdentity(claims));
    }

    private static string In(TimeSpan span) => DateTime.UtcNow.Add(span).ToString("O");

    private static HttpResponseMessage LoginOk(string token = "new") =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(ApiJson.Login("User", 900, token, "r2")) };

    private static HttpResponseMessage Resp(int status) => new((HttpStatusCode)status);

    private static Rig Build(ClaimsPrincipal user, StubHandler inner, StubHandler? refresh = null)
    {
        var auth = new RecordingAuthenticationService();
        var http = new DefaultHttpContext();
        http.RequestServices = new ServiceCollection().AddSingleton<IAuthenticationService>(auth).BuildServiceProvider();
        http.User = user;
        refresh ??= new StubHandler(_ => LoginOk());
        var factory = new StubHttpClientFactory().Add(TokenRefreshHandler.RefreshClientName, refresh);
        var handler = new TokenRefreshHandler(new HttpContextAccessor { HttpContext = http }, factory,
            new AuthSessionService(), NullLogger<TokenRefreshHandler>.Instance) { InnerHandler = inner };
        return new Rig { Handler = handler, Http = http, Inner = inner, Refresh = refresh, Auth = auth };
    }

    private static HttpRequestMessage Req(bool bearer = true, string body = "{\"a\":1}")
    {
        var r = new HttpRequestMessage(HttpMethod.Post, "http://stub/api/thing?x=1")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        if (bearer) r.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "old");
        return r;
    }

    [Fact]
    public async Task No_bearer_passes_through_untouched()
    {
        var rig = Build(Principal(In(TimeSpan.FromSeconds(10))), StubHandler.Always(401));
        using var resp = await rig.SendAsync(Req(bearer: false));
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.Equal(1, rig.Inner.Calls);
        Assert.Equal(0, rig.Refresh.Calls);
    }

    [Fact]
    public async Task Anonymous_user_passes_through_untouched()
    {
        var rig = Build(Principal(In(TimeSpan.FromSeconds(10)), authenticated: false), StubHandler.Always(401));
        using var resp = await rig.SendAsync(Req());
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.Equal(1, rig.Inner.Calls);
        Assert.Equal(0, rig.Refresh.Calls);
        Assert.Equal("Bearer old", rig.Inner.Requests[0].Authorization);
    }

    [Fact]
    public async Task Missing_http_context_passes_through()
    {
        var inner = StubHandler.Always(200);
        var handler = new TokenRefreshHandler(new HttpContextAccessor(), new StubHttpClientFactory(),
            new AuthSessionService(), NullLogger<TokenRefreshHandler>.Instance) { InnerHandler = inner };
        using var resp = await new HttpMessageInvoker(handler).SendAsync(Req(), CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task Token_expiring_in_30_seconds_refreshes_first_and_resigns_in()
    {
        var rig = Build(Principal(In(TimeSpan.FromSeconds(30))), StubHandler.Always(200));
        using var resp = await rig.SendAsync(Req());

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(1, rig.Refresh.Calls);
        Assert.Equal(1, rig.Inner.Calls);
        Assert.Equal("Bearer new", rig.Inner.Requests[0].Authorization);
        Assert.Contains("\"refreshToken\":\"r1\"", rig.Refresh.Requests[0].BodyText);
        Assert.Equal("api/account/refresh", rig.Refresh.Requests[0].Uri!.AbsolutePath.TrimStart('/'));

        var signIn = Assert.Single(rig.Auth.SignIns);
        Assert.Equal("new", signIn.Principal.FindFirst("access_token")!.Value);
        Assert.Equal("r2", signIn.Principal.FindFirst("refresh_token")!.Value);
        Assert.Equal("alice", signIn.Principal.Identity!.Name);
        Assert.Same(signIn.Principal, rig.Http.User);
    }

    [Fact]
    public async Task Token_expiring_in_15_minutes_does_not_refresh()
    {
        var rig = Build(Principal(In(TimeSpan.FromMinutes(15))), StubHandler.Always(200));
        using var resp = await rig.SendAsync(Req());
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(0, rig.Refresh.Calls);
        Assert.Equal(1, rig.Inner.Calls);
        Assert.Empty(rig.Auth.SignIns);
    }

    [Fact]
    public async Task Malformed_expiry_claim_is_treated_as_not_expiring()
    {
        var rig = Build(Principal("not-a-date"), StubHandler.Always(200));
        using var resp = await rig.SendAsync(Req());
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(0, rig.Refresh.Calls);
        Assert.Equal(1, rig.Inner.Calls);
    }

    [Fact]
    public async Task Inner_401_then_refresh_ok_retries_with_new_bearer_and_identical_request()
    {
        var inner = new StubHandler((_, n) => Resp(n == 1 ? 401 : 200));
        var rig = Build(Principal(In(TimeSpan.FromMinutes(15))), inner);
        using var resp = await rig.SendAsync(Req(body: "{\"name\":\"zed\"}"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(2, inner.Calls);
        Assert.Equal(1, rig.Refresh.Calls);
        var (first, second) = (inner.Requests[0], inner.Requests[1]);
        Assert.Equal("Bearer old", first.Authorization);
        Assert.Equal("Bearer new", second.Authorization);
        Assert.Equal(first.Method, second.Method);
        Assert.Equal(first.Uri, second.Uri);
        Assert.Equal(first.Body, second.Body);
        Assert.Equal("{\"name\":\"zed\"}", second.BodyText);
        Assert.Equal(first.ContentType, second.ContentType);
        Assert.Single(rig.Auth.SignIns);
    }

    [Fact]
    public async Task Refresh_401_returns_original_401()
    {
        var rig = Build(Principal(In(TimeSpan.FromMinutes(15))), StubHandler.Always(401), new StubHandler(_ => Resp(401)));
        using var resp = await rig.SendAsync(Req());
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.Equal(1, rig.Inner.Calls);
        Assert.Equal(1, rig.Refresh.Calls);
        Assert.Empty(rig.Auth.SignIns);
    }

    [Fact]
    public async Task Refresh_throwing_HttpRequestException_returns_original_401()
    {
        var refresh = new StubHandler(_ => throw new HttpRequestException("down"));
        var rig = Build(Principal(In(TimeSpan.FromMinutes(15))), StubHandler.Always(401), refresh);
        using var resp = await rig.SendAsync(Req());
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.Equal(1, rig.Inner.Calls);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{ this is not json")]
    public async Task Refresh_200_with_null_or_invalid_json_returns_original_401(string body)
    {
        var refresh = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });
        var rig = Build(Principal(In(TimeSpan.FromMinutes(15))), StubHandler.Always(401), refresh);
        using var resp = await rig.SendAsync(Req());
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.Equal(1, rig.Inner.Calls);
        Assert.Equal(1, rig.Refresh.Calls);
        Assert.Empty(rig.Auth.SignIns);
    }

    [Fact]
    public async Task Preemptive_refresh_failed_then_401_does_not_refresh_again()
    {
        var rig = Build(Principal(In(TimeSpan.FromSeconds(10))), StubHandler.Always(401), new StubHandler(_ => Resp(401)));
        using var resp = await rig.SendAsync(Req());
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.Equal(1, rig.Refresh.Calls);
        Assert.Equal(1, rig.Inner.Calls);
    }

    [Fact]
    public async Task Preemptive_refresh_succeeded_then_401_does_not_refresh_again()
    {
        var rig = Build(Principal(In(TimeSpan.FromSeconds(10))), StubHandler.Always(401));
        using var resp = await rig.SendAsync(Req());
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.Equal(1, rig.Refresh.Calls);
        Assert.Equal(1, rig.Inner.Calls);
        Assert.Equal("Bearer new", rig.Inner.Requests[0].Authorization);
    }

    [Fact]
    public async Task Two_requests_on_same_context_after_failed_refresh_do_not_retry_refresh()
    {
        var rig = Build(Principal(In(TimeSpan.FromMinutes(15))), StubHandler.Always(401), new StubHandler(_ => Resp(401)));
        using var first = await rig.SendAsync(Req());
        using var second = await rig.SendAsync(Req());
        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        Assert.Equal(1, rig.Refresh.Calls);
        Assert.Equal(2, rig.Inner.Calls);
    }

    [Fact]
    public async Task Missing_refresh_claim_means_no_refresh_call()
    {
        var rig = Build(Principal(In(TimeSpan.FromSeconds(10)), refresh: null), StubHandler.Always(401));
        using var resp = await rig.SendAsync(Req());
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.Equal(0, rig.Refresh.Calls);
        Assert.Equal(1, rig.Inner.Calls);
        Assert.Equal("Bearer old", rig.Inner.Requests[0].Authorization);
    }
}
