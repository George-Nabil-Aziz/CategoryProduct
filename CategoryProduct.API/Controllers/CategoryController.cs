using CategoryProduct.API.Data;
using CategoryProduct.API.Models;
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
            var categories = _context.Categories
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            return Ok(categories);
        }

        [HttpGet("{id}")]
        public IActionResult Get(int id)
        {
            var category = _context.Categories.Find(id);

            if (category == null)
                return NotFound();

            return Ok(category);
        }

        [HttpPost]
        public IActionResult Post(Category category)
        {
            _context.Categories.Add(category);
            _context.SaveChanges();

            return Ok(category);
        }

        [HttpPut("{id}")]
        public IActionResult Put(int id, Category category)
        {
            var oldCategory = _context.Categories.Find(id);

            if (oldCategory == null)
                return NotFound();

            oldCategory.Name = category.Name;

            _context.SaveChanges();

            return Ok(oldCategory);
        }

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
                .ToList();

            return Ok(products);
        }
    }
}