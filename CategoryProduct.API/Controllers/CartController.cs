using System.Security.Claims;
using CategoryProduct.API.Data;
using CategoryProduct.API.Dtos;
using CategoryProduct.API.Infrastructure;
using CategoryProduct.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CategoryProduct.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class CartController : ControllerBase
    {
        private const int MaxPerLine = 100;
        private readonly ApplicationDbContext _context;

        public CartController(ApplicationDbContext context) => _context = context;

        private int? CurrentUserId() =>
            int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

        private async Task<CartResponseDto> BuildCartAsync(int userId)
        {
            var items = await _context.CartItems.AsNoTracking()
                .Where(c => c.UserId == userId)
                .OrderBy(c => c.AddedAtUtc).ThenBy(c => c.Id)
                .Select(c => new CartItemResponseDto(c.ProductId, c.Product!.Name, c.Product.Price, c.Quantity,
                    c.Product.Price * c.Quantity, c.Product.Quantity))
                .ToListAsync();
            return OrderMappingExtensions.BuildCart(items);
        }

        [HttpGet]
        public async Task<ActionResult<CartResponseDto>> Get()
        {
            if (CurrentUserId() is not int userId) return Unauthorized();
            return await BuildCartAsync(userId);
        }

        [HttpPost("items")]
        public async Task<ActionResult<CartResponseDto>> AddItem(AddCartItemRequestDto request)
        {
            if (CurrentUserId() is not int userId) return Unauthorized();

            if (!await _context.Products.AnyAsync(p => p.Id == request.ProductId))
                return NotFound();

            for (var attempt = 1; ; attempt++)
            {
                var line = await _context.CartItems
                    .FirstOrDefaultAsync(c => c.UserId == userId && c.ProductId == request.ProductId);
                if (line == null)
                {
                    _context.CartItems.Add(new CartItem
                    {
                        UserId = userId,
                        ProductId = request.ProductId,
                        Quantity = request.Quantity,
                        AddedAtUtc = DateTime.UtcNow
                    });
                }
                else
                {
                    if (line.Quantity + request.Quantity > MaxPerLine)
                    {
                        ModelState.AddModelError("Quantity", "The cart cannot hold more than 100 of one product.");
                        return ValidationProblem(ModelState);
                    }
                    line.Quantity += request.Quantity;
                }

                try
                {
                    await _context.SaveChangesAsync();
                    break;
                }
                catch (DbUpdateException) when (attempt == 1)
                {
                    // Unique (UserId, ProductId) race: re-read and merge once.
                    _context.ChangeTracker.Clear();
                }
            }

            return await BuildCartAsync(userId);
        }

        [HttpPut("items/{productId:int}")]
        public async Task<ActionResult<CartResponseDto>> UpdateItem(int productId, UpdateCartItemRequestDto request)
        {
            if (CurrentUserId() is not int userId) return Unauthorized();
            var line = await _context.CartItems.FirstOrDefaultAsync(c => c.UserId == userId && c.ProductId == productId);
            if (line == null) return NotFound();
            line.Quantity = request.Quantity;
            await _context.SaveChangesAsync();
            return await BuildCartAsync(userId);
        }

        [HttpDelete("items/{productId:int}")]
        public async Task<ActionResult<CartResponseDto>> RemoveItem(int productId)
        {
            if (CurrentUserId() is not int userId) return Unauthorized();
            var line = await _context.CartItems.FirstOrDefaultAsync(c => c.UserId == userId && c.ProductId == productId);
            if (line == null) return NotFound();
            _context.CartItems.Remove(line);
            await _context.SaveChangesAsync();
            return await BuildCartAsync(userId);
        }

        [HttpDelete]
        public async Task<IActionResult> Clear()
        {
            if (CurrentUserId() is not int userId) return Unauthorized();
            var lines = await _context.CartItems.Where(c => c.UserId == userId).ToListAsync();
            _context.CartItems.RemoveRange(lines);
            await _context.SaveChangesAsync();
            return NoContent();
        }
    }
}
