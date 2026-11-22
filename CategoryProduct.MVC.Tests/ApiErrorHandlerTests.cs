using Microsoft.AspNetCore.Http;
using System.Net;
using CategoryProduct.MVC.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;

namespace CategoryProduct.MVC.Tests;

/// <summary>Unit tests for ApiErrorHandler and ApiUnavailableFilter.</summary>
public class ApiErrorHandlerTests
{
    private sealed class TrackingContent : HttpContent
    {
        public bool Disposed { get; private set; }
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) => Task.CompletedTask;
        protected override bool TryComputeLength(out long length) { length = 0; return true; }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }

    private static Task<HttpResponseMessage> Send(StubHandler inner, CancellationToken ct = default)
    {
        var handler = new ApiErrorHandler(NullLogger<ApiErrorHandler>.Instance) { InnerHandler = inner };
        return new HttpMessageInvoker(handler).SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://stub/api/x"), ct);
    }

    [Fact]
    public async Task HttpRequestException_becomes_ApiUnavailableException_with_inner_and_friendly_message()
    {
        var original = new HttpRequestException("refused");
        var ex = await Assert.ThrowsAsync<ApiUnavailableException>(() => Send(new StubHandler(_ => throw original)));
        Assert.Same(original, ex.InnerException);
        Assert.Equal(ApiUnavailableException.FriendlyMessage, ex.Message);
    }

    [Fact]
    public async Task Timeout_with_uncancelled_caller_token_becomes_ApiUnavailableException()
    {
        var original = new TaskCanceledException("timeout");
        var ex = await Assert.ThrowsAsync<ApiUnavailableException>(() => Send(new StubHandler(_ => throw original)));
        Assert.Same(original, ex.InnerException);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_as_TaskCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAsync<TaskCanceledException>(
            () => Send(new StubHandler(_ => throw new TaskCanceledException("cancelled")), cts.Token));
    }

    [Theory]
    [InlineData(500)]
    [InlineData(503)]
    public async Task Server_errors_become_ApiUnavailableException_and_dispose_the_response(int status)
    {
        var content = new TrackingContent();
        var inner = new StubHandler(_ => new HttpResponseMessage((HttpStatusCode)status) { Content = content });
        var ex = await Assert.ThrowsAsync<ApiUnavailableException>(() => Send(inner));
        Assert.Equal(ApiUnavailableException.FriendlyMessage, ex.Message);
        Assert.Null(ex.InnerException);
        Assert.True(content.Disposed);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(204)]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(404)]
    [InlineData(409)]
    public async Task Non_5xx_responses_are_returned_untouched(int status)
    {
        using var resp = await Send(StubHandler.Always(status));
        Assert.Equal(status, (int)resp.StatusCode);
    }

    private static ExceptionContext Ctx(Exception ex) =>
        new(new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>())
        {
            Exception = ex
        };

    [Fact]
    public void Filter_turns_ApiUnavailableException_into_503_ServiceUnavailable_view()
    {
        var ctx = Ctx(new ApiUnavailableException(ApiUnavailableException.FriendlyMessage));
        new ApiUnavailableFilter().OnException(ctx);

        var view = Assert.IsType<ViewResult>(ctx.Result);
        Assert.Equal("ServiceUnavailable", view.ViewName);
        Assert.Equal(503, view.StatusCode);
        Assert.Equal(ApiUnavailableException.FriendlyMessage, view.ViewData["Message"]);
        Assert.True(ctx.ExceptionHandled);
    }

    [Fact]
    public void Filter_ignores_other_exceptions()
    {
        var ctx = Ctx(new InvalidOperationException("boom"));
        new ApiUnavailableFilter().OnException(ctx);
        Assert.Null(ctx.Result);
        Assert.False(ctx.ExceptionHandled);
    }
}
