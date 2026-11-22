using CategoryProduct.API.Services;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CategoryProduct.API.Tests
{
    public class ProductImageStoragePathTests
    {
        private sealed class FakeEnv(string root) : IHostEnvironment
        {
            public string EnvironmentName { get; set; } = "Test";
            public string ApplicationName { get; set; } = "Test";
            public string ContentRootPath { get; set; } = root;
            public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        }

        private static ProductImageStorage Create(string storagePath, string root) =>
            new(Options.Create(new ProductImageOptions { StoragePath = storagePath }),
                new FakeEnv(root), NullLogger<ProductImageStorage>.Instance);

        [Fact]
        public async Task RelativeStoragePath_ResolvesUnderContentRoot()
        {
            var root = Path.Combine(Path.GetTempPath(), "pi-root-" + Guid.NewGuid().ToString("N"));
            try
            {
                var storage = Create("imgs/sub", root);
                var name = await storage.SaveAsync(1, new MemoryStream(new byte[] { 1 }), "png");
                var path = storage.TryGetPath(name);
                Assert.NotNull(path);
                Assert.Equal(Path.GetFullPath(Path.Combine(root, "imgs", "sub", name)), path);
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Fact]
        public async Task AbsoluteStoragePath_IsUsedAsIs()
        {
            var abs = Path.Combine(Path.GetTempPath(), "pi-abs-" + Guid.NewGuid().ToString("N"));
            var root = Path.Combine(Path.GetTempPath(), "pi-other-" + Guid.NewGuid().ToString("N"));
            try
            {
                var storage = Create(abs, root);
                var name = await storage.SaveAsync(1, new MemoryStream(new byte[] { 1 }), "png");
                Assert.Equal(Path.GetFullPath(Path.Combine(abs, name)), storage.TryGetPath(name));
                Assert.False(Directory.Exists(root));
            }
            finally { if (Directory.Exists(abs)) Directory.Delete(abs, true); }
        }
    }
}
