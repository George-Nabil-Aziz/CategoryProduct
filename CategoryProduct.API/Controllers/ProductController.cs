using CategoryProduct.API.Data;
using CategoryProduct.API.Dtos;
using CategoryProduct.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using System.Security.Claims;
using CategoryProduct.API.Infrastructure;
using CategoryProduct.API.Services;

namespace CategoryProduct.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProductController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IProductImageStorage _storage;
        private readonly ProductImageOptions _imageOptions;
        private readonly ILogger<ProductController> _logger;
        private readonly InventoryService _inventory;
        private readonly int _threshold;

        public ProductController(ApplicationDbContext context, IProductImageStorage storage,
            IOptions<ProductImageOptions> imageOptions, ILogger<ProductController> logger,
            InventoryService inventory, IOptions<InventoryOptions> inventoryOptions)
        {
            _context = context;
            _storage = storage;
            _imageOptions = imageOptions.Value;
            _logger = logger;
            _inventory = inventory;
            _threshold = inventoryOptions.Value.Effective;
        }

        [HttpGet]
        public async Task<IActionResult> Get(int page = 1, int pageSize = 5, string? search = null,
            int? categoryId = null, decimal? minPrice = null, decimal? maxPrice = null,
            string? sortBy = null, bool desc = false)
        {
            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 1, 50);

            if (minPrice > maxPrice)
            {
                ModelState.AddModelError(nameof(minPrice), "minPrice cannot be greater than maxPrice.");
                return ValidationProblem(ModelState);
            }

            var query = _context.Products.AsQueryable();
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                query = query.Where(p => p.Name.ToLower().Contains(term));
            }
            if (categoryId != null) query = query.Where(p => p.CategoryId == categoryId);
            if (minPrice != null) query = query.Where(p => p.Price >= minPrice);
            if (maxPrice != null) query = query.Where(p => p.Price <= maxPrice);

            var totalCount = await query.CountAsync();

            var ordered = (sortBy?.ToLowerInvariant(), desc) switch
            {
                ("name", false) => query.OrderBy(p => p.Name).ThenBy(p => p.Id),
                ("name", true) => query.OrderByDescending(p => p.Name).ThenBy(p => p.Id),
                ("price", false) => query.OrderBy(p => p.Price).ThenBy(p => p.Id),
                ("price", true) => query.OrderByDescending(p => p.Price).ThenBy(p => p.Id),
                (_, true) => query.OrderByDescending(p => p.Id),
                _ => query.OrderBy(p => p.Id)
            };

            var entities = await ordered
                .AsNoTracking()
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
            var products = entities.Select(p => p.ToResponse(Url, _threshold)).ToList();

            return Ok(new PagedResponseDto<ProductResponseDto>(
                products, page, pageSize, totalCount, (int)Math.Ceiling(totalCount / (double)pageSize)));
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> Get(int id)
        {
            var product = await _context.Products.FindAsync(id);

            if (product == null)
                return NotFound();

            return Ok(product.ToResponse(Url, _threshold));
        }

        [Authorize(Roles = "Admin")]
        [HttpGet("low-stock")]
        public async Task<IActionResult> GetLowStock(int page = 1, int pageSize = 10)
        {
            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 1, 50);

            var query = _context.Products.AsNoTracking().Where(p => p.Quantity < _threshold);
            var totalCount = await query.CountAsync();
            var entities = await query
                .OrderBy(p => p.Quantity).ThenBy(p => p.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
            var items = entities.Select(p => p.ToResponse(Url, _threshold)).ToList();

            return Ok(new PagedResponseDto<ProductResponseDto>(
                items, page, pageSize, totalCount, (int)Math.Ceiling(totalCount / (double)pageSize)));
        }

        [Authorize(Roles = "Admin")]
        [HttpGet("{id}/stock-movements")]
        public async Task<IActionResult> GetStockMovements(int id, int page = 1, int pageSize = 10)
        {
            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 1, 50);

            if (!await _context.Products.AnyAsync(p => p.Id == id))
                return NotFound();

            var query = _context.StockMovements.AsNoTracking().Where(m => m.ProductId == id);
            var totalCount = await query.CountAsync();
            var items = await query
                .OrderByDescending(m => m.CreatedAtUtc).ThenByDescending(m => m.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(m => new StockMovementResponseDto(m.Id, m.ProductId, m.QuantityChange, m.QuantityAfter,
                    m.Reason.ToString(), m.Note, m.UserId, m.User != null ? m.User.UserName : null, m.CreatedAtUtc))
                .ToListAsync();

            return Ok(new PagedResponseDto<StockMovementResponseDto>(
                items, page, pageSize, totalCount, (int)Math.Ceiling(totalCount / (double)pageSize)));
        }

        [Authorize(Roles = "Admin")]
        [HttpPost("{id}/stock-movements")]
        public async Task<IActionResult> PostStockMovement(int id, StockMovementRequestDto request)
        {
            if (request.QuantityChange == 0)
                ModelState.AddModelError(nameof(request.QuantityChange), "Quantity change cannot be zero.");
            if (!StockMovementReasonExtensions.TryParseName(request.Reason, out var reason)
                || !StockMovementReasonExtensions.AdminReasons.Contains(reason))
                ModelState.AddModelError(nameof(request.Reason),
                    "Reason must be one of: " + string.Join(", ", StockMovementReasonExtensions.AdminReasons) + ".");
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
            var result = await _inventory.ApplyMovementAsync(id, request.QuantityChange, reason, note, CurrentUserId());

            switch (result.Status)
            {
                case MovementStatus.NotFound:
                    return NotFound();
                case MovementStatus.InsufficientStock:
                    return Conflict(new ProblemDetails
                    {
                        Status = 409, Title = "Conflict",
                        Detail = "The change would make the quantity negative or too large."
                    });
                case MovementStatus.Conflict:
                    return ConcurrencyConflict();
            }

            var m = result.Movement!;
            return StatusCode(201, new StockMovementResponseDto(m.Id, m.ProductId, m.QuantityChange, m.QuantityAfter,
                m.Reason.ToString(), m.Note, m.UserId, User.FindFirstValue(ClaimTypes.Name), m.CreatedAtUtc));
        }

        [Authorize(Roles = "Admin")]
        [HttpPost]
        public async Task<IActionResult> Post(ProductRequestDto request)
        {
            if (!await _context.Categories.AnyAsync(c => c.Id == request.CategoryId))
            {
                ModelState.AddModelError(nameof(request.CategoryId), "Category does not exist.");
                return ValidationProblem(ModelState);
            }

            var product = new Product
            {
                Name = request.Name,
                Price = request.Price,
                Quantity = request.Quantity,
                CategoryId = request.CategoryId
            };

            _context.Products.Add(product);
            if (request.Quantity > 0)
            {
                _context.StockMovements.Add(new StockMovement
                {
                    Product = product,
                    QuantityChange = request.Quantity,
                    QuantityAfter = request.Quantity,
                    Reason = StockMovementReason.Initial,
                    UserId = CurrentUserId(),
                    CreatedAtUtc = DateTime.UtcNow
                });
            }
            await _context.SaveChangesAsync();

            return Ok(product.ToResponse(Url, _threshold));
        }

        [Authorize(Roles = "Admin")]
        [HttpPut("{id}")]
        public async Task<IActionResult> Put(int id, ProductRequestDto request)
        {
            if (!await _context.Categories.AnyAsync(c => c.Id == request.CategoryId))
            {
                ModelState.AddModelError(nameof(request.CategoryId), "Category does not exist.");
                return ValidationProblem(ModelState);
            }

            var oldProduct = await _context.Products.FindAsync(id);

            if (oldProduct == null)
                return NotFound();

            if (request.Quantity != oldProduct.Quantity)
            {
                ModelState.AddModelError(nameof(request.Quantity), "Use stock movements to change quantity.");
                return ValidationProblem(ModelState);
            }

            oldProduct.Name = request.Name;
            oldProduct.Price = request.Price;
            oldProduct.CategoryId = request.CategoryId;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                return ConcurrencyConflict();
            }

            return Ok(oldProduct.ToResponse(Url, _threshold));
        }

        [Authorize(Roles = "Admin")]
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var product = await _context.Products.FindAsync(id);

            if (product == null)
                return NotFound();

            var imageFileName = product.ImageFileName;
            _context.Products.Remove(product);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                return ConcurrencyConflict();
            }
            _storage.Delete(imageFileName);

            return Ok();
        }

        [Authorize(Roles = "Admin")]
        [HttpPost("{id}/image")]
        [RequestSizeLimit(2_359_296)]
        public async Task<IActionResult> UploadImage(int id, IFormFile? file, CancellationToken cancellationToken)
        {
            var product = await _context.Products.FindAsync(new object[] { id }, cancellationToken);
            if (product == null)
                return NotFound();

            if (file == null || file.Length == 0)
                return FileError("A non-empty image file is required in the 'file' field.");
            if (file.Length > _imageOptions.MaxBytes)
                return FileError($"The image must be {_imageOptions.MaxBytes / (1024 * 1024.0):0.##} MB or smaller.");

            var ext = ProductImageFormats.NormalizeExtension(Path.GetExtension(file.FileName));
            if (ext == null)
                return FileError("Only .jpg, .jpeg, .png and .webp files are allowed.");
            if (!ProductImageFormats.IsAllowedContentType(file.ContentType))
                return FileError("The file content type must be image/jpeg, image/png or image/webp.");

            string newFileName;
            await using (var stream = file.OpenReadStream())
            {
                var header = new byte[12];
                var read = 0;
                while (read < header.Length)
                {
                    var n = await stream.ReadAsync(header.AsMemory(read), cancellationToken);
                    if (n == 0) break;
                    read += n;
                }
                if (ProductImageFormats.Sniff(header.AsSpan(0, read)) != ext)
                    return FileError("The file content does not match a supported image format.");

                stream.Seek(0, SeekOrigin.Begin);
                newFileName = await _storage.SaveAsync(id, stream, ext, cancellationToken);
            }

            var oldFileName = product.ImageFileName;
            product.ImageFileName = newFileName;
            try
            {
                await _context.SaveChangesAsync(CancellationToken.None);
            }
            catch
            {
                _storage.Delete(newFileName);
                throw;
            }
            _storage.Delete(oldFileName);

            return Ok(product.ToResponse(Url, _threshold));
        }

        [Authorize(Roles = "Admin")]
        [HttpDelete("{id}/image")]
        public async Task<IActionResult> DeleteImage(int id)
        {
            var product = await _context.Products.FindAsync(id);
            if (product == null)
                return NotFound();

            if (product.ImageFileName != null)
            {
                var fileName = product.ImageFileName;
                product.ImageFileName = null;
                await _context.SaveChangesAsync();
                _storage.Delete(fileName);
            }

            return NoContent();
        }

        [HttpGet("{id}/image")]
        public async Task<IActionResult> GetImage(int id)
        {
            var product = await _context.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
            if (product?.ImageFileName == null)
                return NotFound();

            var path = _storage.TryGetPath(product.ImageFileName);
            if (path == null)
            {
                _logger.LogWarning("Image file {FileName} for product {ProductId} is missing on disk", product.ImageFileName, id);
                return NotFound();
            }

            var contentType = ProductImageFormats.ContentTypeForExtension(Path.GetExtension(path).TrimStart('.'))
                ?? "application/octet-stream";
            Response.Headers[HeaderNames.CacheControl] = "no-cache";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            return PhysicalFile(path, contentType, null, new EntityTagHeaderValue($"\"{product.ImageFileName}\""));
        }

        private int? CurrentUserId() =>
            int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : null;

        private IActionResult ConcurrencyConflict() =>
            Conflict(new ProblemDetails
            {
                Status = 409, Title = "Conflict",
                Detail = "The product was changed by another request. Reload and try again."
            });

        private IActionResult FileError(string message)
        {
            ModelState.AddModelError("file", message);
            return ValidationProblem(ModelState);
        }
    }
}