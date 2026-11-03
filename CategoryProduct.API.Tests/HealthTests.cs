using System.Net;
using System.Text.Json;

namespace CategoryProduct.API.Tests
{
    public class HealthTests : IClassFixture<ApiFactory>
    {
        private readonly ApiFactory _factory;

        public HealthTests(ApiFactory factory) => _factory = factory;

        [Fact]
        public async Task Health_ReturnsHealthyWithDatabaseCheck()
        {
            var client = _factory.CreateClient();

            var response = await client.GetAsync("/health");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("Healthy", doc.RootElement.GetProperty("status").GetString());
            Assert.Contains(doc.RootElement.GetProperty("checks").EnumerateArray(),
                c => c.GetProperty("name").GetString() == "database");
        }
    }
}
