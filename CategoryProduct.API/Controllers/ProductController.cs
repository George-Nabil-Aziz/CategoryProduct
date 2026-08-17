using CategoryProduct.API.Data;
using CategoryProduct.API.Models;
using Microsoft.AspNetCore.Mvc;

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
        public IActionResult Get(int page = 1, int pageSize = 5)
        {
            return Ok(_context.Products
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList());
        }

        [HttpGet("{id}")]
        public IActionResult Get(int id)
        {
            var product = _context.Products.Find(id);

            if (product == null)
                return NotFound();

            return Ok(product);
        }

        [HttpPost]
        public IActionResult Post(Product product)
        {
            _context.Products.Add(product);
            _context.SaveChanges();

            return Ok(product);
        }

        [HttpPut("{id}")]
        public IActionResult Put(int id, Product product)
        {
            var oldProduct = _context.Products.Find(id);

            if (oldProduct == null)
                return NotFound();

            oldProduct.Name = product.Name;
            oldProduct.Price = product.Price;
            oldProduct.Quantity = product.Quantity;
            oldProduct.CategoryId = product.CategoryId;

            _context.SaveChanges();

            return Ok(oldProduct);
        }

        [HttpDelete("{id}")]
        public IActionResult Delete(int id)
        {
            var product = _context.Products.Find(id);

            if (product == null)
                return NotFound();

            _context.Products.Remove(product);
            _context.SaveChanges();

            return Ok();
        }
    }
}