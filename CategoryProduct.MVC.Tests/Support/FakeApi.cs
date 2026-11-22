using System.Net;
using System.Text;
using System.Text.Json;

namespace CategoryProduct.MVC.Tests;

/// <summary>Thrown when the MVC app calls an API route nobody scripted. Not an HttpRequestException on purpose.</summary>
public sealed class FakeApiUnmatchedException : Exception
{
    public FakeApiUnmatchedException(string message) : base(message) { }
}

/// <summary>One request seen by <see cref="FakeApi"/>. <see cref="Path"/> has no leading slash and is lower-cased.</summary>
public sealed record RecordedRequest(
    int Seq, HttpMethod Method, string Path, string Query, string? Authorization,
    string? ContentType, string BodyText, byte[] BodyBytes)
{
    /// <summary>Parses <see cref="BodyText"/> as JSON (for JSON request bodies).</summary>
    public JsonElement Json() => JsonDocument.Parse(BodyText).RootElement.Clone();
}

/// <summary>A scripted response. Use the static factories.</summary>
public sealed class FakeResponse
{
    internal HttpStatusCode StatusCode { get; init; }
    internal byte[]? Content { get; init; }
    internal string? ContentType { get; init; }
    internal Exception? ToThrow { get; init; }

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>JSON body (camelCase via JsonSerializerDefaults.Web).</summary>
    public static FakeResponse Json(int status, object? body) => new()
    {
        StatusCode = (HttpStatusCode)status,
        Content = JsonSerializer.SerializeToUtf8Bytes(body, Web),
        ContentType = "application/json"
    };

    /// <summary>Empty body (204, 401, 403, 404, 429 ...).</summary>
    public static FakeResponse Status(int status) => new() { StatusCode = (HttpStatusCode)status };

    /// <summary>Binary body, e.g. a product image.</summary>
    public static FakeResponse Bytes(int status, byte[] bytes, string contentType) =>
        new() { StatusCode = (HttpStatusCode)status, Content = bytes, ContentType = contentType };

    /// <summary>Raw text body with the given content type.</summary>
    public static FakeResponse Raw(int status, string text, string contentType) =>
        new() { StatusCode = (HttpStatusCode)status, Content = Encoding.UTF8.GetBytes(text), ContentType = contentType };

    internal static FakeResponse Fail(Exception ex) => new() { ToThrow = ex };
}

/// <summary>
/// In-memory stand-in for the API, used as the primary HttpMessageHandler. Routes match on method + path
/// (case-insensitive, no leading/trailing slash, no query). Later registrations win over earlier ones.
/// </summary>
public sealed class FakeApi : HttpMessageHandler
{
    private sealed record Route(HttpMethod Method, string Path, Func<string, bool>? Query,
        Func<RecordedRequest, FakeResponse> Handler, string Description);

    private readonly object _gate = new();
    private readonly List<Route> _routes = new();
    private readonly List<RecordedRequest> _requests = new();

    private static string Norm(string path) => path.Trim('/').ToLowerInvariant();

    /// <summary>Always answer with <paramref name="response"/>.</summary>
    public void On(HttpMethod method, string path, FakeResponse response) =>
        Add(method, path, null, _ => response, "fixed");

    /// <summary>Compute the answer from the request (e.g. inspect Authorization or BodyText).</summary>
    public void On(HttpMethod method, string path, Func<RecordedRequest, FakeResponse> handler) =>
        Add(method, path, null, handler, "computed");

    /// <summary>Like On, but only when the raw query string (e.g. "?page=2") satisfies the predicate.</summary>
    public void OnQuery(HttpMethod method, string path, Func<string, bool> query, FakeResponse response) =>
        Add(method, path, query, _ => response, "query-filtered");

    /// <summary>The n-th call returns the n-th response; the last one repeats.</summary>
    public void OnSequence(HttpMethod method, string path, params FakeResponse[] responses)
    {
        if (responses.Length == 0) throw new ArgumentException("At least one response required.", nameof(responses));
        var n = 0;
        Add(method, path, null, _ => responses[Math.Min(Interlocked.Increment(ref n) - 1, responses.Length - 1)],
            $"sequence({responses.Length})");
    }

    /// <summary>Connection failure: the handler throws <paramref name="ex"/> (default HttpRequestException).</summary>
    public void Throw(HttpMethod method, string path, Exception? ex = null) =>
        Add(method, path, null, _ => FakeResponse.Fail(ex ?? new HttpRequestException("FakeApi: connection refused")), "throw");

    /// <summary>Simulates a timeout: throws TaskCanceledException while the caller token is not cancelled.</summary>
    public void Hang(HttpMethod method, string path) =>
        Add(method, path, null, _ => FakeResponse.Fail(new TaskCanceledException("FakeApi: simulated timeout")), "hang");

    private void Add(HttpMethod method, string path, Func<string, bool>? query,
        Func<RecordedRequest, FakeResponse> handler, string desc)
    {
        lock (_gate) _routes.Add(new Route(method, Norm(path), query, handler, desc));
    }

    /// <summary>Clears routes and the request log.</summary>
    public void Reset()
    {
        lock (_gate) { _routes.Clear(); _requests.Clear(); }
    }

    /// <summary>Clears only the request log (routes stay). Sign-in helpers call this so tests start with a clean log.</summary>
    public void ClearRequests()
    {
        lock (_gate) _requests.Clear();
    }

    /// <summary>Snapshot of all requests in order.</summary>
    public IReadOnlyList<RecordedRequest> Requests
    {
        get { lock (_gate) return _requests.ToList(); }
    }

    /// <summary>Requests matching method and path.</summary>
    public IReadOnlyList<RecordedRequest> Calls(HttpMethod method, string path)
    {
        var p = Norm(path);
        return Requests.Where(r => r.Method == method && r.Path == p).ToList();
    }

    /// <summary>Asserts exactly one request matches and returns it.</summary>
    public RecordedRequest Single(HttpMethod method, string path)
    {
        var calls = Calls(method, path);
        if (calls.Count != 1)
            throw new Xunit.Sdk.XunitException(
                $"Expected exactly one {method} {Norm(path)} but saw {calls.Count}. Log: " +
                string.Join("; ", Requests.Select(r => $"{r.Method} {r.Path}{r.Query}")));
        return calls[0];
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var bytes = request.Content is null ? Array.Empty<byte>() : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        var uri = request.RequestUri!;
        var path = Norm(uri.AbsolutePath);
        RecordedRequest rec;
        Route[] routes;
        lock (_gate)
        {
            rec = new RecordedRequest(_requests.Count + 1, request.Method, path, uri.Query,
                request.Headers.Authorization?.ToString(),
                request.Content?.Headers.ContentType?.ToString(),
                Encoding.UTF8.GetString(bytes), bytes);
            _requests.Add(rec);
            routes = _routes.ToArray();
        }

        for (var i = routes.Length - 1; i >= 0; i--)
        {
            var r = routes[i];
            if (r.Method != request.Method || r.Path != path) continue;
            if (r.Query != null && !r.Query(uri.Query)) continue;
            var resp = r.Handler(rec);
            if (resp.ToThrow != null)
            {
                if (resp.ToThrow is TaskCanceledException && cancellationToken.IsCancellationRequested)
                    cancellationToken.ThrowIfCancellationRequested();
                throw resp.ToThrow;
            }
            var msg = new HttpResponseMessage(resp.StatusCode) { RequestMessage = request };
            if (resp.Content != null)
            {
                msg.Content = new ByteArrayContent(resp.Content);
                if (resp.ContentType != null)
                    msg.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(resp.ContentType);
            }
            return msg;
        }

        string scripted;
        lock (_gate)
            scripted = _routes.Count == 0 ? "(none)"
                : string.Join(", ", _routes.Select(r => $"{r.Method} {r.Path} [{r.Description}]"));
        throw new FakeApiUnmatchedException(
            $"No FakeApi route for {request.Method} {path}{uri.Query}. Scripted: {scripted}");
    }

    // HttpClientFactory disposes/recycles handlers; MvcFactory owns the real lifetime.
    protected override void Dispose(bool disposing) { }
}
