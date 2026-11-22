namespace CategoryProduct.MVC.Tests;

/// <summary>IHttpClientFactory that hands out clients backed by pre-registered handlers, keyed by client name.</summary>
public sealed class StubHttpClientFactory : IHttpClientFactory
{
    private readonly Dictionary<string, HttpMessageHandler> _handlers = new();

    public List<string> CreatedNames { get; } = new();

    public StubHttpClientFactory Add(string name, HttpMessageHandler handler)
    {
        _handlers[name] = handler;
        return this;
    }

    public HttpClient CreateClient(string name)
    {
        CreatedNames.Add(name);
        if (!_handlers.TryGetValue(name, out var handler))
            throw new InvalidOperationException($"No stub handler registered for client '{name}'.");
        return new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("http://stub/") };
    }
}
