using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CategoryProduct.API.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CategoryProduct.API.Tests
{
    public class ProductImageTests : IClassFixture<ApiFactory>
    {
        private const int MaxBytes = 2097152;
        private readonly ApiFactory _factory;
        private readonly HttpClient _client;

        public ProductImageTests(ApiFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
        }

        // ---------- helpers ----------
        private static byte[] Png() => new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4, 5, 6, 7, 8 };
        private static byte[] Jpeg() => new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1 };
        private static byte[] WebP() => Encoding.ASCII.GetBytes("RIFF").Concat(new byte[] { 8, 0, 0, 0 })
            .Concat(Encoding.ASCII.GetBytes("WEBPVP8 ")).ToArray();

        private async Task<string> LoginAsync(string userName = ApiFactory.UserName)
        {
            var response = await _client.PostAsJsonAsync("api/account/login",
                new { userName, password = ApiFactory.Password });
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            return json.GetProperty("token").GetString()!;
        }

        private async Task<int> CreateProductAsync(string token)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "api/product")
            {
                Content = JsonContent.Create(new { name = "Img " + Guid.NewGuid().ToString("N")[..6], price = 5, quantity = 1, categoryId = 1 })
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var res = await _client.SendAsync(req);
            res.EnsureSuccessStatusCode();
            return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        }

        private static MultipartFormDataContent Multipart(byte[] bytes, string fileName, string contentType, string field = "file")
        {
            var form = new MultipartFormDataContent();
            var part = new ByteArrayContent(bytes);
            part.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            form.Add(part, field, fileName);
            return form;
        }

        private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string? token, HttpContent? content = null)
        {
            var req = new HttpRequestMessage(method, url) { Content = content };
            if (token != null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return _client.SendAsync(req);
        }

        private Task<HttpResponseMessage> UploadAsync(int id, string? token, byte[] bytes, string fileName, string contentType, string field = "file") =>
            SendAsync(HttpMethod.Post, $"api/product/{id}/image", token, Multipart(bytes, fileName, contentType, field));

        private string[] FilesFor(int id) =>
            Directory.Exists(_factory.ImageStoragePath)
                ? Directory.GetFiles(_factory.ImageStoragePath, $"{id}-*").Select(Path.GetFileName).ToArray()!
                : Array.Empty<string>();

        private static async Task<string?> ImageUrlAsync(HttpResponseMessage res) =>
            (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("imageUrl").GetString();

        // ---------- US1: upload ----------
        [Theory]
        [InlineData("a.png", "image/png", "png")]
        [InlineData("a.jpg", "image/jpeg", "jpg")]
        [InlineData("a.jpeg", "image/jpeg", "jpg")]
        [InlineData("a.webp", "image/webp", "webp")]
        public async Task Upload_ValidImage_Returns200AndStoresFile(string name, string contentType, string ext)
        {
            var token = await LoginAsync();
            var id = await CreateProductAsync(token);
            var bytes = ext switch { "png" => Png(), "jpg" => Jpeg(), _ => WebP() };

            var res = await UploadAsync(id, token, bytes, name, contentType);

            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            Assert.False(string.IsNullOrEmpty(await ImageUrlAsync(res)));
            var files = FilesFor(id);
            var single = Assert.Single(files);
            Assert.EndsWith("." + ext, single);
        }

        public static IEnumerable<object[]> Rejections()
        {
            var html = Encoding.ASCII.GetBytes("<html><script>alert(1)</script></html>");
            var exe = new byte[] { 0x4D, 0x5A, 0x90, 0, 3, 0, 0, 0, 4, 0, 0, 0, 0xFF, 0xFF };
            var wav = Encoding.ASCII.GetBytes("RIFF").Concat(new byte[] { 8, 0, 0, 0 }).Concat(Encoding.ASCII.GetBytes("WAVEfmt ")).ToArray();
            yield return new object[] { "a.gif", "image/gif", Png() };
            yield return new object[] { "a.txt", "text/plain", Png() };
            yield return new object[] { "a.png", "text/html", Png() };
            yield return new object[] { "a.png", "image/png", html };
            yield return new object[] { "a.png", "image/png", exe };
            yield return new object[] { "a.jpg", "image/jpeg", Png() };
            yield return new object[] { "a.png", "image/png", Array.Empty<byte>() };
            yield return new object[] { "a.webp", "image/webp", wav };
            yield return new object[] { "noext", "image/png", Png() };
        }

        [Theory]
        [MemberData(nameof(Rejections))]
        public async Task Upload_InvalidFile_Returns400WithFileError(string name, string contentType, byte[] bytes)
        {
            var token = await LoginAsync();
            var id = await CreateProductAsync(token);

            var res = await UploadAsync(id, token, bytes, name, contentType);

            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            var json = await res.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(json.GetProperty("errors").TryGetProperty("file", out _));
            Assert.Empty(FilesFor(id));
        }

        [Fact]
        public async Task Upload_MissingFileField_Returns400()
        {
            var token = await LoginAsync();
            var id = await CreateProductAsync(token);

            var res = await UploadAsync(id, token, Png(), "a.png", "image/png", field: "other");

            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            var json = await res.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(json.GetProperty("errors").TryGetProperty("file", out _));
            Assert.Empty(FilesFor(id));
        }

        [Fact]
        public async Task Upload_OverMaxBytes_Returns400()
        {
            var token = await LoginAsync();
            var id = await CreateProductAsync(token);
            var big = new byte[MaxBytes + 1];
            Png().CopyTo(big, 0);

            var res = await UploadAsync(id, token, big, "big.png", "image/png");

            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            var json = await res.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(json.GetProperty("errors").TryGetProperty("file", out _));
            Assert.Empty(FilesFor(id));
        }

        [Fact]
        public async Task Upload_ExactlyMaxBytes_Succeeds()
        {
            var token = await LoginAsync();
            var id = await CreateProductAsync(token);
            var big = new byte[MaxBytes];
            Png().CopyTo(big, 0);

            var res = await UploadAsync(id, token, big, "big.png", "image/png");

            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        }

        [Fact]
        public async Task Upload_TraversalClientName_StoresGeneratedNameInsideDirectory()
        {
            var token = await LoginAsync();
            var id = await CreateProductAsync(token);

            var res = await UploadAsync(id, token, Png(), "../../evil.png", "image/png");

            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var name = Assert.Single(FilesFor(id))!;
            Assert.Matches($@"^{id}-[0-9a-f]{{32}}\.png$", name);
            Assert.False(File.Exists(Path.Combine(_factory.ImageStoragePath, "..", "evil.png")));
            Assert.DoesNotContain("evil", name);
        }

        [Fact]
        public async Task Upload_Anonymous_Returns401()
        {
            var token = await LoginAsync();
            var id = await CreateProductAsync(token);

            var res = await UploadAsync(id, null, Png(), "a.png", "image/png");

            Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
            Assert.Empty(FilesFor(id));
        }

        [Fact]
        public async Task Upload_NonAdmin_Returns403()
        {
            var admin = await LoginAsync();
            var id = await CreateProductAsync(admin);
            var user = await LoginAsync(ApiFactory.PlainUserName);

            var res = await UploadAsync(id, user, Png(), "a.png", "image/png");

            Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
            Assert.Empty(FilesFor(id));
        }

        [Fact]
        public async Task Upload_UnknownProduct_Returns404AndWritesNothing()
        {
            var token = await LoginAsync();
            var before = Directory.Exists(_factory.ImageStoragePath) ? Directory.GetFiles(_factory.ImageStoragePath, "999999-*").Length : 0;

            var res = await UploadAsync(999999, token, Png(), "a.png", "image/png");

            Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
            Assert.Empty(FilesFor(999999));
            Assert.Equal(0, before);
        }

        [Fact]
        public async Task Delete_Anonymous_Returns401_NonAdmin403_Unknown404()
        {
            var admin = await LoginAsync();
            var id = await CreateProductAsync(admin);
            var user = await LoginAsync(ApiFactory.PlainUserName);

            Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(HttpMethod.Delete, $"api/product/{id}/image", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Delete, $"api/product/{id}/image", user)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Delete, "api/product/999999/image", admin)).StatusCode);
        }

        [Fact]
        public async Task Upload_Replace_LeavesOnlyNewFile()
        {
            var token = await LoginAsync();
            var id = await CreateProductAsync(token);
            Assert.Equal(HttpStatusCode.OK, (await UploadAsync(id, token, Png(), "a.png", "image/png")).StatusCode);
            var first = Assert.Single(FilesFor(id));

            var res = await UploadAsync(id, token, Jpeg(), "b.jpg", "image/jpeg");

            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var second = Assert.Single(FilesFor(id));
            Assert.NotEqual(first, second);
            Assert.False(File.Exists(Path.Combine(_factory.ImageStoragePath, first!)));
        }

        [Fact]
        public async Task DeleteImage_RemovesFileAndClearsUrl_AndIsIdempotent()
        {
            var token = await LoginAsync();
            var id = await CreateProductAsync(token);
            await UploadAsync(id, token, Png(), "a.png", "image/png");
            Assert.Single(FilesFor(id));

            var del = await SendAsync(HttpMethod.Delete, $"api/product/{id}/image", token);

            Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
            Assert.Empty(FilesFor(id));
            var get = await _client.GetFromJsonAsync<JsonElement>($"api/product/{id}");
            Assert.Equal(JsonValueKind.Null, get.GetProperty("imageUrl").ValueKind);

            var again = await SendAsync(HttpMethod.Delete, $"api/product/{id}/image", token);
            Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        }

        [Fact]
        public async Task ExceptionHandler_BadHttpRequest413_Returns413()
        {
            var services = new ServiceCollection().AddLogging().BuildServiceProvider();
            var ctx = new DefaultHttpContext { RequestServices = services };
            ctx.Response.Body = new MemoryStream();
            var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);

            var handled = await handler.TryHandleAsync(ctx,
                new BadHttpRequestException("too big", StatusCodes.Status413PayloadTooLarge), CancellationToken.None);

            Assert.True(handled);
            Assert.Equal(413, ctx.Response.StatusCode);
            Assert.Equal("application/problem+json", ctx.Response.ContentType);
        }

        [Fact]
        public async Task ExceptionHandler_OtherException_Returns500()
        {
            var services = new ServiceCollection().AddLogging().BuildServiceProvider();
            var ctx = new DefaultHttpContext { RequestServices = services };
            ctx.Response.Body = new MemoryStream();
            var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);

            await handler.TryHandleAsync(ctx, new InvalidOperationException("x"), CancellationToken.None);

            Assert.Equal(500, ctx.Response.StatusCode);
        }

        // ---------- US2: read ----------
        [Theory]
        [InlineData("a.png", "image/png")]
        [InlineData("a.jpg", "image/jpeg")]
        [InlineData("a.webp", "image/webp")]
        public async Task GetImage_Anonymous_ReturnsExactBytesAndHeaders(string name, string contentType)
        {
            var token = await LoginAsync();
            var id = await CreateProductAsync(token);
            var bytes = contentType switch { "image/png" => Png(), "image/jpeg" => Jpeg(), _ => WebP() };
            await UploadAsync(id, token, bytes, name, contentType);

            var res = await _client.GetAsync($"api/product/{id}/image");

            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            Assert.Equal(contentType, res.Content.Headers.ContentType?.MediaType);
            Assert.Equal(bytes, await res.Content.ReadAsByteArrayAsync());
            Assert.Contains("nosniff", res.Headers.GetValues("X-Content-Type-Options"));
            Assert.NotNull(res.Headers.ETag);
            Assert.Contains("no-cache", res.Headers.CacheControl?.ToString());

            var req = new HttpRequestMessage(HttpMethod.Get, $"api/product/{id}/image");
            req.Headers.IfNoneMatch.Add(res.Headers.ETag!);
            Assert.Equal(HttpStatusCode.NotModified, (await _client.SendAsync(req)).StatusCode);
        }

        [Fact]
        public async Task GetImage_UnknownProductOrNoImage_Returns404()
        {
            var token = await LoginAsync();
            var id = await CreateProductAsync(token);

            Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("api/product/999999/image")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"api/product/{id}/image")).StatusCode);
        }

        [Fact]
        public async Task GetImage_FileMissingOnDisk_Returns404()
        {
            var token = await LoginAsync();
            var id = await CreateProductAsync(token);
            await UploadAsync(id, token, Png(), "a.png", "image/png");
            foreach (var f in FilesFor(id)) File.Delete(Path.Combine(_factory.ImageStoragePath, f!));

            Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"api/product/{id}/image")).StatusCode);
        }

        [Fact]
        public async Task ImageUrl_AppearsInGetListAndCategoryProducts_AndIsNullWithoutImage()
        {
            var token = await LoginAsync();
            var id = await CreateProductAsync(token);
            var other = await CreateProductAsync(token);
            await UploadAsync(id, token, Png(), "a.png", "image/png");
            var suffix = $"/api/product/{id}/image";

            var single = await _client.GetFromJsonAsync<JsonElement>($"api/product/{id}");
            var url = single.GetProperty("imageUrl").GetString()!;
            Assert.True(Uri.TryCreate(url, UriKind.Absolute, out _));
            Assert.EndsWith(suffix, url);

            var list = await _client.GetFromJsonAsync<JsonElement>("api/product?page=1&pageSize=50&sortBy=id&desc=true");
            var items = list.GetProperty("items").EnumerateArray().ToList();
            Assert.EndsWith(suffix, items.First(i => i.GetProperty("id").GetInt32() == id).GetProperty("imageUrl").GetString());
            Assert.Equal(JsonValueKind.Null, items.First(i => i.GetProperty("id").GetInt32() == other).GetProperty("imageUrl").ValueKind);

            var cat = await _client.GetFromJsonAsync<JsonElement>("api/category/1/products");
            var catItems = cat.EnumerateArray().ToList();
            Assert.EndsWith(suffix, catItems.First(i => i.GetProperty("id").GetInt32() == id).GetProperty("imageUrl").GetString());

            var seeded = catItems.First(i => i.GetProperty("name").GetString() == "Widget A");
            Assert.Equal(30m, seeded.GetProperty("price").GetDecimal());
            Assert.Equal(1, seeded.GetProperty("quantity").GetInt32());
            Assert.Equal(1, seeded.GetProperty("categoryId").GetInt32());
            Assert.Equal(JsonValueKind.Null, seeded.GetProperty("imageUrl").ValueKind);
        }

        // ---------- US3: product delete ----------
        [Fact]
        public async Task DeleteProduct_RemovesImageFile()
        {
            var token = await LoginAsync();
            var id = await CreateProductAsync(token);
            await UploadAsync(id, token, Png(), "a.png", "image/png");
            Assert.Single(FilesFor(id));

            var res = await SendAsync(HttpMethod.Delete, $"api/product/{id}", token);

            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            Assert.Empty(FilesFor(id));
        }

        [Fact]
        public async Task DeleteProduct_WithMissingFile_StillReturns200()
        {
            var token = await LoginAsync();
            var id = await CreateProductAsync(token);
            await UploadAsync(id, token, Png(), "a.png", "image/png");
            foreach (var f in FilesFor(id)) File.Delete(Path.Combine(_factory.ImageStoragePath, f!));

            var res = await SendAsync(HttpMethod.Delete, $"api/product/{id}", token);

            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        }
    }
}
