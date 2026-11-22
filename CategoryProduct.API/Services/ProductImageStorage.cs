using Microsoft.Extensions.Options;

namespace CategoryProduct.API.Services
{
    public static class ProductImageFormats
    {
        private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["jpg"] = "image/jpeg",
            ["png"] = "image/png",
            ["webp"] = "image/webp"
        };

        /// <summary>Normalizes an extension (with or without dot); returns null if not allowed. ".jpeg" becomes "jpg".</summary>
        public static string? NormalizeExtension(string? ext)
        {
            if (string.IsNullOrEmpty(ext)) return null;
            ext = ext.TrimStart('.').ToLowerInvariant();
            if (ext == "jpeg") ext = "jpg";
            return ContentTypes.ContainsKey(ext) ? ext : null;
        }

        public static string? ContentTypeForExtension(string ext) =>
            ContentTypes.TryGetValue(ext, out var ct) ? ct : null;

        public static bool IsAllowedContentType(string? contentType) =>
            contentType != null && ContentTypes.Values.Contains(contentType.Trim().ToLowerInvariant());

        /// <summary>Detects the format from magic bytes: "jpg", "png", "webp" or null.</summary>
        public static string? Sniff(ReadOnlySpan<byte> h)
        {
            if (h.Length >= 3 && h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF) return "jpg";
            if (h.Length >= 8 && h[0] == 0x89 && h[1] == 0x50 && h[2] == 0x4E && h[3] == 0x47
                && h[4] == 0x0D && h[5] == 0x0A && h[6] == 0x1A && h[7] == 0x0A) return "png";
            if (h.Length >= 12 && h[0] == (byte)'R' && h[1] == (byte)'I' && h[2] == (byte)'F' && h[3] == (byte)'F'
                && h[8] == (byte)'W' && h[9] == (byte)'E' && h[10] == (byte)'B' && h[11] == (byte)'P') return "webp";
            return null;
        }
    }

    public class ProductImageStorage : IProductImageStorage
    {
        private readonly string _directory;
        private readonly ILogger<ProductImageStorage> _logger;

        public ProductImageStorage(IOptions<ProductImageOptions> options, IHostEnvironment env, ILogger<ProductImageStorage> logger)
        {
            _logger = logger;
            // Relative paths resolve against the content root; absolute paths are used as-is.
            var p = options.Value.StoragePath;
            _directory = Path.GetFullPath(Path.IsPathRooted(p) ? p : Path.Combine(env.ContentRootPath, p));
        }

        public async Task<string> SaveAsync(int productId, Stream content, string ext, CancellationToken cancellationToken = default)
        {
            var normalized = ProductImageFormats.NormalizeExtension(ext)
                ?? throw new ArgumentException("Unsupported extension.", nameof(ext));
            Directory.CreateDirectory(_directory);
            var fileName = $"{productId}-{Guid.NewGuid():N}.{normalized}";
            var path = Path.Combine(_directory, fileName);
            try
            {
                await using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await content.CopyToAsync(fs, cancellationToken);
            }
            catch
            {
                Delete(fileName);
                throw;
            }
            return fileName;
        }

        public void Delete(string? fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return;
            try
            {
                var path = ResolveSafe(fileName);
                if (path != null && File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete product image file {FileName}", fileName);
            }
        }

        public string? TryGetPath(string fileName)
        {
            var path = ResolveSafe(fileName);
            return path != null && File.Exists(path) ? path : null;
        }

        private string? ResolveSafe(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)
                || fileName.IndexOfAny(new[] { '/', '\\' }) >= 0
                || fileName.Contains("..")
                || fileName != Path.GetFileName(fileName))
                return null;
            var full = Path.GetFullPath(Path.Combine(_directory, fileName));
            var root = _directory.EndsWith(Path.DirectorySeparatorChar) ? _directory : _directory + Path.DirectorySeparatorChar;
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : null;
        }
    }
}
