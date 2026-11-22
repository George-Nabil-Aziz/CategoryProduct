namespace CategoryProduct.MVC.Tests;

/// <summary>A request seen by <see cref="StubHandler"/>, with the body read eagerly.</summary>
public sealed record StubRequest(HttpMethod Method, Uri? Uri, string? Authorization, string? ContentType, byte[] Body)
{
    public string BodyText => System.Text.Encoding.UTF8.GetString(Body);
}

/// <summary>
/// Delegate-based HttpMessageHandler for handler unit tests. The delegate gets the request and the 1-based call number;
/// it may return a response or throw. Counts calls and captures requests (method, URI, bearer, content type, body bytes).
/// </summary>
public sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, int, HttpResponseMessage> _respond;
    private readonly List<StubRequest> _requests = new();
    private int _calls;

    public StubHandler(Func<HttpRequestMessage, int, HttpResponseMessage> respond) => _respond = respond;

    public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : this((r, _) => respond(r)) { }

    /// <summary>Always answers with the given status code.</summary>
    public static StubHandler Always(int status) => new(_ => new HttpResponseMessage((System.Net.HttpStatusCode)status));

    public int Calls => Volatile.Read(ref _calls);

    public IReadOnlyList<StubRequest> Requests
    {
        get { lock (_requests) return _requests.ToList(); }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? Array.Empty<byte>() : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        lock (_requests)
        {
            _requests.Add(new StubRequest(request.Method, request.RequestUri, request.Headers.Authorization?.ToString(),
                request.Content?.Headers.ContentType?.ToString(), body));
        }
        var n = Interlocked.Increment(ref _calls);
        return _respond(request, n);
    }
}
