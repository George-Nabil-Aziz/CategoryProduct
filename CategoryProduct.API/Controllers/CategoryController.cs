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
    public class CategoryController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public CategoryController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Get(int page = 1, int pageSize = 5)
        {
            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 1, 50);

            var totalCount = await _context.Categories.CountAsync();
            var categories = await _context.Categories
                .OrderBy(c => c.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(c => new CategoryResponseDto(c.Id, c.Name))
                .ToListAsync();

            return Ok(new PagedResponseDto<CategoryResponseDto>(
                categories, page, pageSize, totalCount, (int)Math.Ceiling(totalCount / (double)pageSize)));
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> Get(int id)
        {
            var category = await _context.Categories.FindAsync(id);

            if (category == null)
                return NotFound();

            return Ok(new CategoryResponseDto(category.Id, category.Name));
        }

        [Authorize(Roles = "Admin")]
        [HttpPost]
        public async Task<IActionResult> Post(CategoryRequestDto request)
        {
            var category = new Category { Name = request.Name };

            _context.Categories.Add(category);
            await _context.SaveChangesAsync();

            return Ok(new CategoryResponseDto(category.Id, category.Name));
        }

        [Authorize(Roles = "Admin")]
        [HttpPut("{id}")]
        public async Task<IActionResult> Put(int id, CategoryRequestDto request)
        {
            var oldCategory = await _context.Categories.FindAsync(id);

            if (oldCategory == null)
                return NotFound();

            oldCategory.Name = request.Name;

            await _context.SaveChangesAsync();

            return Ok(new CategoryResponseDto(oldCategory.Id, oldCategory.Name));
        }

        [Authorize(Roles = "Admin")]
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var category = await _context.Categories.FindAsync(id);

            if (category == null)
                return NotFound();

            if (await _context.Products.AnyAsync(p => p.CategoryId == id))
                return Problem(statusCode: 409, title: "Category has products", detail: "Delete or move its products first.");

            _context.Categories.Remove(category);
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpGet("{catId}/products")]
        public async Task<IActionResult> GetAllProducts(int catId)
        {
            var products = await _context.Products
                .Where(p => p.CategoryId == catId)
                .Select(p => new ProductResponseDto(p.Id, p.Name, p.Price, p.Quantity, p.CategoryId))
                .ToListAsync();

            return Ok(products);
        }
    }
}