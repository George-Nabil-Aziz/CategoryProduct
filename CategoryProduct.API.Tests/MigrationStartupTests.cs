using System.Net;
using CategoryProduct.API.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CategoryProduct.API.Tests
{
    public class MigrationStartupTests
    {
        private sealed class MigrateFactory : ApiFactory
        {
            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                base.ConfigureWebHost(builder);
                builder.UseSetting("Database:MigrateOnStartup", "true");
            }
        }

        [Fact]
        public async Task Helper_FlagTrue_InMemoryProvider_SkipsWithoutException()
        {
            using var factory = new ApiFactory();
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:MigrateOnStartup"] = "true" })
                .Build();

            await DatabaseMigrator.MigrateIfEnabledAsync(factory.Services, config, NullLogger.Instance);
        }

        [Fact]
        public async Task Startup_FlagTrue_InMemory_ServesHealth()
        {
            using var factory = new MigrateFactory();
            var response = await factory.CreateClient().GetAsync("/health");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task Startup_FlagUnset_ServesHealth()
        {
            using var factory = new ApiFactory();
            Assert.False(factory.Services.GetRequiredService<IConfiguration>().GetValue("Database:MigrateOnStartup", false));
            var response = await factory.CreateClient().GetAsync("/health");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}
