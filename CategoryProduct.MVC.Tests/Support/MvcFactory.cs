using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace CategoryProduct.MVC.Tests;

/// <summary>
/// MVC host for tests: every HttpClient (typed API client and the refresh client) uses the single
/// <see cref="Fake"/> as its primary handler, so ApiErrorHandler/TokenRefreshHandler/CategoryProductApiClient stay real.
/// One instance per test class (xunit class fixture).
/// </summary>
public sealed class MvcFactory : WebApplicationFactory<Program>
{
    private readonly string _dpPath = Path.Combine(Path.GetTempPath(), "cp-mvc-dp-" + Guid.NewGuid().ToString("N"));
    private readonly string _logDir = Path.Combine(Path.GetTempPath(), "cp-mvc-logs-" + Guid.NewGuid().ToString("N"));

    /// <summary>The fake API shared by all clients of this host.</summary>
    public FakeApi Fake { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Api:BaseUrl", "http://fake-api/");
        builder.UseSetting("Hosting:UseHttpsRedirection", "false");
        builder.UseSetting("Cookie:SecurePolicy", "SameAsRequest");
        builder.UseSetting("DataProtection:KeysPath", _dpPath);
        builder.UseSetting("Serilog:WriteTo:1:Args:path", Path.Combine(_logDir, "mvc-.log"));

        builder.ConfigureServices(services =>
            services.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => Fake)));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        foreach (var d in new[] { _dpPath, _logDir })
        {
            try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
