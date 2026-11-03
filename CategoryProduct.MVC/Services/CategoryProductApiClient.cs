using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using CategoryProduct.MVC.Models;

namespace CategoryProduct.MVC.Services
{
    public class CategoryProductApiClient
    {
        private const int MaxPageSize = 50;
        private readonly HttpClient _http;

        private readonly IHttpContextAccessor _context;

        public CategoryProductApiClient(HttpClient http, IHttpContextAccessor context)
        {
            _http = http;
            _context = context;
        }

        // Attaches the signed-in user's JWT (stored in the auth cookie) to a single outgoing request.
        private void ApplyBearerToken(HttpRequestMessage request, string? token = null)
        {
            token ??= _context.HttpContext?.User.FindFirst("access_token")?.Value;
            if (!string.IsNullOrEmpty(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        public async Task<LoginResult?> LoginAsync(string userName, string password)
        {
            using var response = await _http.PostAsJsonAsync("api/account/login", new { userName, password });
            if (response.StatusCode == HttpStatusCode.Unauthorized) return null;
            if (response.StatusCode == HttpStatusCode.TooManyRequests) return LoginResult.TooManyRequests;
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<LoginResult>();
        }

        public Task<ApiResult> RegisterAsync(string userName, string password) =>
            SendAsync(HttpMethod.Post, "api/account/register", new { userName, password });

        public async Task<PagedResult<UserItem>> GetUsersAsync(int page, int pageSize = 10, string? search = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"api/users?page={page}&pageSize={pageSize}&search={Uri.EscapeDataString(search ?? "")}");
            ApplyBearerToken(request);
            using var response = await _http.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<PagedResult<UserItem>>())!;
        }

        public Task<ApiResult> ChangeUserRoleAsync(int id, string role) =>
            SendAsync(HttpMethod.Put, $"api/users/{id}/role", new { role });

        // ---------- Categories ----------
        public async Task<PagedResult<Category>> GetCategoriesAsync(int page, int pageSize = 5, string? search = null) =>
            (await _http.GetFromJsonAsync<PagedResult<Category>>($"api/category?page={page}&pageSize={pageSize}&search={Uri.EscapeDataString(search ?? "")}"))!;

        public async Task<List<Category>> GetAllCategoriesAsync()
        {
            var all = new List<Category>();
            for (int page = 1; ; page++)
            {
                var result = await GetCategoriesAsync(page, MaxPageSize);
                all.AddRange(result.Items);
                if (page >= result.TotalPages) return all;
            }
        }

        public Task<Category?> GetCategoryAsync(int id) => GetOrNullAsync<Category>($"api/category/{id}");

        public async Task<List<Product>> GetCategoryProductsAsync(int categoryId) =>
            (await _http.GetFromJsonAsync<List<Product>>($"api/category/{categoryId}/products")) ?? new();

        public Task<ApiResult> CreateCategoryAsync(Category c) =>
            SendAsync(HttpMethod.Post, "api/category", new CategoryRequest(c.Name));

        public Task<ApiResult> UpdateCategoryAsync(int id, Category c) =>
            SendAsync(HttpMethod.Put, $"api/category/{id}", new CategoryRequest(c.Name));

        public Task<ApiResult> DeleteCategoryAsync(int id) => SendAsync(HttpMethod.Delete, $"api/category/{id}");

        // ---------- Products ----------
        public async Task<PagedResult<Product>> GetProductsAsync(int page, int pageSize = 5, string? search = null,
            int? categoryId = null, decimal? minPrice = null, decimal? maxPrice = null, string? sortBy = null, bool desc = false) =>
            (await _http.GetFromJsonAsync<PagedResult<Product>>(
                $"api/product?page={page}&pageSize={pageSize}&search={Uri.EscapeDataString(search ?? "")}&categoryId={categoryId}"
                + $"&minPrice={minPrice?.ToString(System.Globalization.CultureInfo.InvariantCulture)}&maxPrice={maxPrice?.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
                + $"&sortBy={Uri.EscapeDataString(sortBy ?? "")}&desc={desc.ToString().ToLowerInvariant()}"))!;

        public Task<Product?> GetProductAsync(int id) => GetOrNullAsync<Product>($"api/product/{id}");

        public Task<ApiResult> CreateProductAsync(Product p) =>
            SendAsync(HttpMethod.Post, "api/product", new ProductRequest(p.Name, p.Price, p.Quantity, p.CategoryId));

        public Task<ApiResult> UpdateProductAsync(int id, Product p) =>
            SendAsync(HttpMethod.Put, $"api/product/{id}", new ProductRequest(p.Name, p.Price, p.Quantity, p.CategoryId));

        public Task<ApiResult> DeleteProductAsync(int id) => SendAsync(HttpMethod.Delete, $"api/product/{id}");

        // ---------- helpers ----------
        private async Task<T?> GetOrNullAsync<T>(string url) where T : class
        {
            var response = await _http.GetAsync(url);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<T>();
        }

        private async Task<ApiResult> SendAsync(HttpMethod method, string url, object? body = null)
        {
            using var request = new HttpRequestMessage(method, url);
            if (body != null) request.Content = JsonContent.Create(body);
            ApplyBearerToken(request);

            using var response = await _http.SendAsync(request);

            if (response.IsSuccessStatusCode) return new ApiResult { Success = true };
            if (response.StatusCode == HttpStatusCode.NotFound) return new ApiResult { NotFound = true };
            if (response.StatusCode == HttpStatusCode.Forbidden) return new ApiResult { Forbidden = true };
            if (response.StatusCode == HttpStatusCode.TooManyRequests) return new ApiResult { TooManyRequests = true };
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return new ApiResult { Unauthorized = true };

            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                string? detail = null;
                try { detail = (await response.Content.ReadFromJsonAsync<ConflictProblem>())?.Detail; }
                catch (JsonException) { }
                return new ApiResult { Conflict = detail ?? "The request conflicts with existing data." };
            }

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                var errors = new Dictionary<string, string[]>();
                try
                {
                    var problem = await response.Content.ReadFromJsonAsync<ValidationProblem>();
                    if (problem?.Errors != null) errors = problem.Errors;
                }
                catch (JsonException) { }
                return new ApiResult { Errors = errors };
            }

            response.EnsureSuccessStatusCode();
            return new ApiResult();
        }

        public record LoginResult(string Token, DateTime ExpiresAtUtc, string Role)
        {
            public static readonly LoginResult TooManyRequests = new("", default, "");
        }

        private record ValidationProblem(Dictionary<string, string[]>? Errors);
        private record ConflictProblem(string? Title, string? Detail);
    }
}
