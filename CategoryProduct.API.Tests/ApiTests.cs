using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace CategoryProduct.API.Tests
{
    public class ApiTests : IClassFixture<ApiFactory>
    {
        private readonly HttpClient _client;

        public ApiTests(ApiFactory factory)
        {
            _client = factory.CreateClient();
        }

        private async Task<string> LoginAsync()
        {
            var response = await _client.PostAsJsonAsync("api/account/login",
                new { userName = ApiFactory.UserName, password = ApiFactory.Password });
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            return json.GetProperty("token").GetString()!;
        }

        [Fact]
        public async Task Login_CorrectCredentials_ReturnsToken()
        {
            var token = await LoginAsync();
            Assert.False(string.IsNullOrWhiteSpace(token));
        }

        [Fact]
        public async Task Login_WrongPassword_Returns401()
        {
            var response = await _client.PostAsJsonAsync("api/account/login",
                new { userName = ApiFactory.UserName, password = "wrong" });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task PostCategory_WithoutToken_Returns401()
        {
            var response = await _client.PostAsJsonAsync("api/category", new { name = "New" });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task GetCategories_PageSizeAbove50_IsClamped()
        {
            var response = await _client.GetAsync("api/category?page=1&pageSize=100");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(50, json.GetProperty("pageSize").GetInt32());
            Assert.Equal(ApiFactory.SeedCategoryCount, json.GetProperty("totalCount").GetInt32());
        }

        [Fact]
        public async Task PostProduct_NonExistentCategory_Returns400()
        {
            var token = await LoginAsync();
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/product")
            {
                Content = JsonContent.Create(new { name = "Widget", price = 10, quantity = 1, categoryId = 9999 })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }
}
