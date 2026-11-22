using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CategoryProduct.API.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CategoryProduct.API.Tests
{
    public class ProductImageConcurrencyTests : IClassFixture<InventoryTests.RacingFactory>
    {
        private readonly InventoryTests.RacingFactory _factory;
        public ProductImageConcurrencyTests(InventoryTests.RacingFactory factory) => _factory = factory;

        private static byte[] Png() => new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4, 5, 6, 7, 8 };

        private static Task<HttpResponseMessage> UploadAsync(HttpClient c, int id)
        {
            var form = new MultipartFormDataContent();
            var part = new ByteArrayContent(Png());
            part.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(part, "file", "a.png");
            return c.PostAsync($"api/product/{id}/image", form);
        }

        private async Task<string?> ImageFileNameAsync(int id)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            return await db.Products.AsNoTracking().Where(p => p.Id == id).Select(p => p.ImageFileName).SingleAsync();
        }

        private int FileCount() =>
            Directory.Exists(_factory.ImageStoragePath) ? Directory.GetFiles(_factory.ImageStoragePath, "*", SearchOption.AllDirectories).Length : 0;

        [Fact]
        public async Task Upload_LosingRace_Returns409_KeepsReference_AndCleansNewFile()
        {
            var admin = await AuthHelper.LoggedInAsync(_factory, ApiFactory.UserName);
            var id = await InventoryHelper.CreateProductAsync(admin, 5);
            var filesBefore = FileCount();

            _factory.Interceptor.Arm(1);
            var r = await UploadAsync(admin, id);
            _factory.Interceptor.Disarm();

            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            var problem = await r.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
            Assert.Equal(409, problem!.Status);
            Assert.Null(await ImageFileNameAsync(id));
            Assert.Equal(filesBefore, FileCount());
        }

        [Fact]
        public async Task Delete_LosingRace_Returns409_KeepsReferenceAndFile()
        {
            var admin = await AuthHelper.LoggedInAsync(_factory, ApiFactory.UserName);
            var id = await InventoryHelper.CreateProductAsync(admin, 5);
            Assert.Equal(HttpStatusCode.OK, (await UploadAsync(admin, id)).StatusCode);
            var name = await ImageFileNameAsync(id);
            Assert.NotNull(name);
            var files = FileCount();

            _factory.Interceptor.Arm(1);
            var r = await admin.DeleteAsync($"api/product/{id}/image");
            _factory.Interceptor.Disarm();

            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Equal(name, await ImageFileNameAsync(id));
            Assert.Equal(files, FileCount());
        }

        [Fact]
        public async Task Disarmed_UploadAndDelete_StillSucceed()
        {
            var admin = await AuthHelper.LoggedInAsync(_factory, ApiFactory.UserName);
            var id = await InventoryHelper.CreateProductAsync(admin, 5);
            _factory.Interceptor.Disarm();
            Assert.Equal(HttpStatusCode.OK, (await UploadAsync(admin, id)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"api/product/{id}/image")).StatusCode);
            Assert.Null(await ImageFileNameAsync(id));
        }
    }
}
