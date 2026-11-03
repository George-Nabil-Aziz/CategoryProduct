using CategoryProduct.API.Data;
using CategoryProduct.API.Dtos;
using CategoryProduct.API.Models;
using Microsoft.AspNetCore.Authorization;
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
            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 1, 50);

            var totalCount = _context.Products.Count();
            var products = _context.Products
                .OrderBy(p => p.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(p => new ProductResponseDto(p.Id, p.Name, p.Price, p.Quantity, p.CategoryId))
                .ToList();

            return Ok(new PagedResponseDto<ProductResponseDto>(
                products, page, pageSize, totalCount, (int)Math.Ceiling(totalCount / (double)pageSize)));
        }

        [HttpGet("{id}")]
        public IActionResult Get(int id)
        {
            var product = _context.Products.Find(id);

            if (product == null)
                return NotFound();

            return Ok(new ProductResponseDto(product.Id, product.Name, product.Price, product.Quantity, product.CategoryId));
        }

        [Authorize]
        [HttpPost]
        public IActionResult Post(ProductRequestDto request)
        {
            if (!_context.Categories.Any(c => c.Id == request.CategoryId))
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
            _context.SaveChanges();

            return Ok(new ProductResponseDto(product.Id, product.Name, product.Price, product.Quantity, product.CategoryId));
        }

        [Authorize]
        [HttpPut("{id}")]
        public IActionResult Put(int id, ProductRequestDto request)
        {
            if (!_context.Categories.Any(c => c.Id == request.CategoryId))
            {
                ModelState.AddModelError(nameof(request.CategoryId), "Category does not exist.");
                return ValidationProblem(ModelState);
            }

            var oldProduct = _context.Products.Find(id);

            if (oldProduct == null)
                return NotFound();

            oldProduct.Name = request.Name;
            oldProduct.Price = request.Price;
            oldProduct.Quantity = request.Quantity;
            oldProduct.CategoryId = request.CategoryId;

            _context.SaveChanges();

            return Ok(new ProductResponseDto(oldProduct.Id, oldProduct.Name, oldProduct.Price, oldProduct.Quantity, oldProduct.CategoryId));
        }

        [Authorize]
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