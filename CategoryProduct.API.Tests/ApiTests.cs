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

        private async Task<string> LoginAsync(string userName = ApiFactory.UserName)
        {
            var response = await _client.PostAsJsonAsync("api/account/login",
                new { userName, password = ApiFactory.Password });
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
        public async Task PostCategory_NonAdminToken_Returns403()
        {
            var token = await LoginAsync(ApiFactory.PlainUserName);
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/category")
            {
                Content = JsonContent.Create(new { name = "New" })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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

        [Fact]
        public async Task DeleteCategory_WithProducts_Returns409()
        {
            var token = await LoginAsync();
            var cat = await SendAsync(HttpMethod.Post, "api/category", token, new { name = "HasProducts" });
            var catId = (await cat.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
            var prod = await SendAsync(HttpMethod.Post, "api/product", token, new { name = "Prod", price = 1, quantity = 1, categoryId = catId });
            Assert.True(prod.IsSuccessStatusCode);

            var del = await SendAsync(HttpMethod.Delete, $"api/category/{catId}", token);
            Assert.Equal(HttpStatusCode.Conflict, del.StatusCode);
            var still = await _client.GetAsync($"api/category/{catId}");
            Assert.Equal(HttpStatusCode.OK, still.StatusCode);
        }

        private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
        {
            using var request = new HttpRequestMessage(method, url);
            if (body != null) request.Content = JsonContent.Create(body);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return await _client.SendAsync(request);
        }

        [Fact]
        public async Task Products_Search_Filters_By_Name()
        {
            var json = await _client.GetFromJsonAsync<JsonElement>("api/product?search=widget");
            Assert.Equal(2, json.GetProperty("totalCount").GetInt32());
        }

        [Fact]
        public async Task Products_PriceRange_And_Sort_Desc()
        {
            var json = await _client.GetFromJsonAsync<JsonElement>("api/product?minPrice=15&maxPrice=30&sortBy=price&desc=true");
            Assert.Equal(2, json.GetProperty("totalCount").GetInt32());
            var prices = json.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("price").GetDecimal()).ToList();
            Assert.Equal(new decimal[] { 30, 20 }, prices);
        }

        [Fact]
        public async Task Products_MinPrice_Greater_Than_MaxPrice_Returns_400()
        {
            var response = await _client.GetAsync("api/product?minPrice=50&maxPrice=10");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task Categories_Search_Returns_Matching_TotalCount()
        {
            var json = await _client.GetFromJsonAsync<JsonElement>("api/category?search=category 2");
            Assert.Equal(1, json.GetProperty("totalCount").GetInt32());
        }
    }
}

namespace CategoryProduct.API.Tests
{
    public class RateLimitTests : IClassFixture<RateLimitTests.LimitedFactory>
    {
        public class LimitedFactory : ApiFactory
        {
            protected override int LoginPermitLimit => 2;
        }

        private readonly HttpClient _client;

        public RateLimitTests(LimitedFactory factory) => _client = factory.CreateClient();

        [Fact]
        public async Task Login_ExceedingLimit_Returns429()
        {
            var body = new { userName = ApiFactory.UserName, password = "wrong" };
            for (var i = 0; i < 2; i++)
                Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostAsJsonAsync("api/account/login", body)).StatusCode);
            Assert.Equal(HttpStatusCode.TooManyRequests, (await _client.PostAsJsonAsync("api/account/login", body)).StatusCode);
        }
    }
}
