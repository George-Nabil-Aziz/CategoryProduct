using System.Net;
using System.Net.Http.Headers;
using System.Globalization;

namespace CategoryProduct.MVC.Services
{
    public class TokenRefreshHandler(
        IHttpContextAccessor accessor,
        IHttpClientFactory factory,
        AuthSessionService session,
        ILogger<TokenRefreshHandler> logger) : DelegatingHandler
    {
        public const string RefreshClientName = "api-refresh";
        private const string TriedKey = "refresh_tried";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var http = accessor.HttpContext;
            // Only authenticated requests (bearer attached) take part in refresh logic.
            if (http == null || request.Headers.Authorization == null || http.User.Identity?.IsAuthenticated != true)
                return await base.SendAsync(request, ct);

            if (request.Content != null) await request.Content.LoadIntoBufferAsync(ct);

            if (ExpiresSoon(http) && await TryRefreshAsync(http, ct))
                SetBearer(request, http);

            var response = await base.SendAsync(request, ct);
            if (response.StatusCode != HttpStatusCode.Unauthorized || http.Items.ContainsKey(TriedKey))
                return response;

            if (!await TryRefreshAsync(http, ct)) return response;

            response.Dispose();
            using var retry = await CloneAsync(request);
            SetBearer(retry, http);
            return await base.SendAsync(retry, ct);
        }

        private static void SetBearer(HttpRequestMessage r, HttpContext http) =>
            r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", http.User.FindFirst("access_token")?.Value);

        private static bool ExpiresSoon(HttpContext http) =>
            DateTime.TryParse(http.User.FindFirst("access_token_expires_at")?.Value, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var exp) && exp.ToUniversalTime() <= DateTime.UtcNow.AddSeconds(60);

        private async Task<bool> TryRefreshAsync(HttpContext http, CancellationToken ct)
        {
            if (http.Items.ContainsKey(TriedKey)) return false;
            http.Items[TriedKey] = true;

            var refresh = http.User.FindFirst("refresh_token")?.Value;
            if (string.IsNullOrEmpty(refresh)) return false;
            try
            {
                var client = factory.CreateClient(RefreshClientName);
                using var response = await client.PostAsJsonAsync("api/account/refresh", new { refreshToken = refresh }, ct);
                if (!response.IsSuccessStatusCode) return false;
                var login = await response.Content.ReadFromJsonAsync<CategoryProductApiClient.LoginResult>(ct);
                if (login == null) return false;
                await session.SignInAsync(http, http.User.Identity!.Name ?? "", login);
                return true;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                logger.LogWarning(ex, "Token refresh failed");
                return false;
            }
        }

        private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage src)
        {
            var clone = new HttpRequestMessage(src.Method, src.RequestUri) { Version = src.Version };
            foreach (var h in src.Headers) clone.Headers.TryAddWithoutValidation(h.Key, h.Value);
            if (src.Content != null)
            {
                var bytes = await src.Content.ReadAsByteArrayAsync();
                clone.Content = new ByteArrayContent(bytes);
                foreach (var h in src.Content.Headers) clone.Content.Headers.TryAddWithoutValidation(h.Key, h.Value);
            }
            return clone;
        }
    }
}
