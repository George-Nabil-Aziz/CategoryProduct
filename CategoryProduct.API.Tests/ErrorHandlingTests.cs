using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace CategoryProduct.API.Tests
{
    public class ThrowingFactory : ApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(s => s.AddTransient<IStartupFilter, ThrowFilter>());
        }

        private class ThrowFilter : IStartupFilter
        {
            public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
            {
                next(app);
                app.Map("/test/throw", b => b.Run(_ => throw new InvalidOperationException("boom secret")));
            };
        }
    }

    public class ErrorHandlingTests : IClassFixture<ThrowingFactory>
    {
        private readonly HttpClient _client;

        public ErrorHandlingTests(ThrowingFactory factory) => _client = factory.CreateClient();

        [Fact]
        public async Task UnhandledException_Returns500ProblemJson()
        {
            var response = await _client.GetAsync("/test/throw");
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            var body = await response.Content.ReadAsStringAsync();
            var json = JsonDocument.Parse(body).RootElement;
            Assert.Equal("An unexpected error occurred.", json.GetProperty("title").GetString());
            Assert.True(json.TryGetProperty("traceId", out _));
            Assert.DoesNotContain("boom secret", body);
        }
    }
}
