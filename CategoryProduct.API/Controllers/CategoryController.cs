using CategoryProduct.API.Data;
using CategoryProduct.API.Dtos;
using CategoryProduct.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
        public IActionResult Get(int page = 1, int pageSize = 5)
        {
            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 1, 50);

            var totalCount = _context.Categories.Count();
            var categories = _context.Categories
                .OrderBy(c => c.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(c => new CategoryResponseDto(c.Id, c.Name))
                .ToList();

            return Ok(new PagedResponseDto<CategoryResponseDto>(
                categories, page, pageSize, totalCount, (int)Math.Ceiling(totalCount / (double)pageSize)));
        }

        [HttpGet("{id}")]
        public IActionResult Get(int id)
        {
            var category = _context.Categories.Find(id);

            if (category == null)
                return NotFound();

            return Ok(new CategoryResponseDto(category.Id, category.Name));
        }

        [Authorize]
        [HttpPost]
        public IActionResult Post(CategoryRequestDto request)
        {
            var category = new Category { Name = request.Name };

            _context.Categories.Add(category);
            _context.SaveChanges();

            return Ok(new CategoryResponseDto(category.Id, category.Name));
        }

        [Authorize]
        [HttpPut("{id}")]
        public IActionResult Put(int id, CategoryRequestDto request)
        {
            var oldCategory = _context.Categories.Find(id);

            if (oldCategory == null)
                return NotFound();

            oldCategory.Name = request.Name;

            _context.SaveChanges();

            return Ok(new CategoryResponseDto(oldCategory.Id, oldCategory.Name));
        }

        [Authorize]
        [HttpDelete("{id}")]
        public IActionResult Delete(int id)
        {
            var category = _context.Categories.Find(id);

            if (category == null)
                return NotFound();

            _context.Categories.Remove(category);
            _context.SaveChanges();

            return Ok();
        }

        [HttpGet("{catId}/products")]
        public IActionResult GetAllProducts(int catId)
        {
            var products = _context.Products
                .Where(p => p.CategoryId == catId)
                .Select(p => new ProductResponseDto(p.Id, p.Name, p.Price, p.Quantity, p.CategoryId))
                .ToList();

            return Ok(products);
        }
    }
}