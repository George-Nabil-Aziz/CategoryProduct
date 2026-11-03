using CategoryProduct.API.Data;
using CategoryProduct.API.Dtos;
using CategoryProduct.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CategoryProduct.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProductController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public ProductController(ApplicationDbContext context)
        {
            _context = context;
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

            var products = await ordered
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(p => new ProductResponseDto(p.Id, p.Name, p.Price, p.Quantity, p.CategoryId))
                .ToListAsync();

            return Ok(new PagedResponseDto<ProductResponseDto>(
                products, page, pageSize, totalCount, (int)Math.Ceiling(totalCount / (double)pageSize)));
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> Get(int id)
        {
            var product = await _context.Products.FindAsync(id);

            if (product == null)
                return NotFound();

            return Ok(new ProductResponseDto(product.Id, product.Name, product.Price, product.Quantity, product.CategoryId));
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
            await _context.SaveChangesAsync();

            return Ok(new ProductResponseDto(product.Id, product.Name, product.Price, product.Quantity, product.CategoryId));
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

            oldProduct.Name = request.Name;
            oldProduct.Price = request.Price;
            oldProduct.Quantity = request.Quantity;
            oldProduct.CategoryId = request.CategoryId;

            await _context.SaveChangesAsync();

            return Ok(new ProductResponseDto(oldProduct.Id, oldProduct.Name, oldProduct.Price, oldProduct.Quantity, oldProduct.CategoryId));
        }

        [Authorize(Roles = "Admin")]
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var product = await _context.Products.FindAsync(id);

            if (product == null)
                return NotFound();

            _context.Products.Remove(product);
            await _context.SaveChangesAsync();

            return Ok();
        }
    }
}