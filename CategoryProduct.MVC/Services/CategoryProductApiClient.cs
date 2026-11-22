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

        // Best effort: revokes the refresh token server-side; failures are ignored.
        public async Task LogoutAsync(string? refreshToken)
        {
            if (string.IsNullOrEmpty(refreshToken)) return;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "api/account/logout")
                { Content = JsonContent.Create(new { refreshToken }) };
                ApplyBearerToken(request);
                request.Options.Set(TokenRefreshHandler.SkipRefreshOption, true);
                using var _ = await _http.SendAsync(request);
            }
            catch (Exception) { }
        }

        public Task<ApiResult> RegisterAsync(string userName, string password, string? email = null) =>
            SendAsync(HttpMethod.Post, "api/account/register",
                string.IsNullOrWhiteSpace(email)
                    ? new { userName, password, email = (string?)null }
                    : new { userName, password, email = (string?)email.Trim() });

        public Task<ApiResult<ProfileItem>> GetMeAsync() => GetAsync<ProfileItem>("api/account/me");

        public Task<ApiResult<ProfileItem>> SetEmailAsync(string? email) =>
            SendAsync<ProfileItem>(HttpMethod.Put, "api/account/email",
                new { email = string.IsNullOrWhiteSpace(email) ? null : email.Trim() });

        public Task<ApiResult<PagedResult<UserItem>>> GetUsersAsync(int page, int pageSize = 10, string? search = null) =>
            GetAsync<PagedResult<UserItem>>(
                $"api/users?page={page}&pageSize={pageSize}&search={Uri.EscapeDataString(search ?? "")}");

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

        public async Task<(byte[] Bytes, string ContentType)?> GetProductImageAsync(int id)
        {
            using var response = await _http.GetAsync($"api/product/{id}/image");
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync();
            return (bytes, response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream");
        }

        public Task<ApiResult> UploadProductImageAsync(int id, Stream content, string fileName, string contentType)
        {
            var form = new MultipartFormDataContent();
            var file = new StreamContent(content);
            file.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
            form.Add(file, "file", fileName);
            return SendAsync(HttpMethod.Post, $"api/product/{id}/image", form);
        }

        public Task<ApiResult> DeleteProductImageAsync(int id) => SendAsync(HttpMethod.Delete, $"api/product/{id}/image");

        // ---------- Inventory (Admin) ----------
        public async Task<PagedResult<StockMovementItem>?> GetStockMovementsAsync(int id, int page, int pageSize = 10)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"api/product/{id}/stock-movements?page={page}&pageSize={pageSize}");
            ApplyBearerToken(request);
            using var response = await _http.SendAsync(request);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<PagedResult<StockMovementItem>>();
        }

        public async Task<int> GetLowStockCountAsync()
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "api/product/low-stock?page=1&pageSize=1");
            ApplyBearerToken(request);
            using var response = await _http.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<PagedResult<Product>>())!.TotalCount;
        }

        public Task<ApiResult> AddStockMovementAsync(int id, StockMovementRequest r) =>
            SendAsync(HttpMethod.Post, $"api/product/{id}/stock-movements", r);

        // ---------- Cart and orders (signed-in users) ----------
        public Task<ApiResult<CartModel>> GetCartAsync() => GetAsync<CartModel>("api/cart");

        public Task<ApiResult<CartModel>> AddCartItemAsync(int productId, int quantity) =>
            SendAsync<CartModel>(HttpMethod.Post, "api/cart/items", new AddCartItemRequest(productId, quantity));

        public Task<ApiResult<CartModel>> UpdateCartItemAsync(int productId, int quantity) =>
            SendAsync<CartModel>(HttpMethod.Put, $"api/cart/items/{productId}", new UpdateCartItemRequest(quantity));

        public Task<ApiResult<CartModel>> RemoveCartItemAsync(int productId) =>
            SendAsync<CartModel>(HttpMethod.Delete, $"api/cart/items/{productId}");

        public Task<ApiResult> ClearCartAsync() => SendAsync(HttpMethod.Delete, "api/cart");

        public Task<ApiResult<OrderModel>> CheckoutAsync() => SendAsync<OrderModel>(HttpMethod.Post, "api/orders");

        public Task<ApiResult<PagedResult<OrderSummaryItem>>> GetMyOrdersAsync(int page, int pageSize = 10) =>
            GetAsync<PagedResult<OrderSummaryItem>>($"api/orders?page={page}&pageSize={pageSize}");

        public Task<ApiResult<OrderModel>> GetOrderAsync(int id) => GetAsync<OrderModel>($"api/orders/{id}");

        public Task<ApiResult<PagedResult<OrderSummaryItem>>> GetAllOrdersAsync(string? status, int page, int pageSize = 10) =>
            GetAsync<PagedResult<OrderSummaryItem>>(
                $"api/orders/all?page={page}&pageSize={pageSize}&status={Uri.EscapeDataString(status ?? "")}");

        public Task<ApiResult<OrderModel>> CancelOrderAsync(int id) =>
            SendAsync<OrderModel>(HttpMethod.Post, $"api/orders/{id}/cancel");

        public Task<ApiResult<OrderModel>> SetOrderStatusAsync(int id, string status) =>
            SendAsync<OrderModel>(HttpMethod.Put, $"api/orders/{id}/status", new UpdateOrderStatusRequest(status));

        // ---------- helpers ----------
        private async Task<ApiResult<T>> GetAsync<T>(string url)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            ApplyBearerToken(request);
            using var response = await _http.SendAsync(request);
            return await ToResultAsync<T>(response);
        }

        private async Task<ApiResult<T>> SendAsync<T>(HttpMethod method, string url, object? body = null)
        {
            using var request = new HttpRequestMessage(method, url) { Content = body != null ? JsonContent.Create(body) : null };
            ApplyBearerToken(request);
            using var response = await _http.SendAsync(request);
            return await ToResultAsync<T>(response);
        }

        private static async Task<ApiResult<T>> ToResultAsync<T>(HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode)
                return new ApiResult<T> { Success = true, Value = await response.Content.ReadFromJsonAsync<T>() };
            if (response.StatusCode == HttpStatusCode.NotFound) return new ApiResult<T> { NotFound = true };
            if (response.StatusCode == HttpStatusCode.Forbidden) return new ApiResult<T> { Forbidden = true };
            if (response.StatusCode == HttpStatusCode.TooManyRequests) return new ApiResult<T> { TooManyRequests = true };
            if (response.StatusCode == HttpStatusCode.Unauthorized) return new ApiResult<T> { Unauthorized = true };

            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                string? detail = null;
                try { detail = (await response.Content.ReadFromJsonAsync<ConflictProblem>())?.Detail; }
                catch (JsonException) { }
                return new ApiResult<T> { Conflict = detail ?? "The request conflicts with existing data." };
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
                return new ApiResult<T> { Errors = errors };
            }

            response.EnsureSuccessStatusCode();
            return new ApiResult<T>();
        }

        private async Task<T?> GetOrNullAsync<T>(string url) where T : class
        {
            var response = await _http.GetAsync(url);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<T>();
        }

        private Task<ApiResult> SendAsync(HttpMethod method, string url, object? body = null) =>
            SendAsync(method, url, body != null ? JsonContent.Create(body) : null);

        private async Task<ApiResult> SendAsync(HttpMethod method, string url, HttpContent? content)
        {
            using var request = new HttpRequestMessage(method, url) { Content = content };
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

        public record LoginResult(string Token, DateTime ExpiresAtUtc, string Role, string RefreshToken, DateTime RefreshTokenExpiresAtUtc)
        {
            public static readonly LoginResult TooManyRequests = new("", default, "", "", default);
        }

        private record ValidationProblem(Dictionary<string, string[]>? Errors);
        private record ConflictProblem(string? Title, string? Detail);
    }
}
