using CategoryProduct.MVC.Models;
using CategoryProduct.MVC.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CategoryProduct.MVC.Controllers
{
    [Authorize(Roles = "Admin")]
    public class InventoryController : Controller
    {
        private readonly CategoryProductApiClient _api;

        public InventoryController(CategoryProductApiClient api) => _api = api;

        public async Task<IActionResult> Index(int page = 1)
        {
            try
            {
                var result = await _api.GetProductsAsync(Math.Max(page, 1), 10);
                ViewBag.LowStockCount = await _api.GetLowStockCountAsync();
                ViewBag.CurrentPage = result.Page;
                ViewBag.TotalPages = result.TotalPages;
                return View(result.Items);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return await SessionExpiredAsync();
            }
        }

        [HttpGet("Inventory/Product/{id:int}")]
        public async Task<IActionResult> History(int id, int page = 1)
        {
            try
            {
                var product = await _api.GetProductAsync(id);
                if (product == null) return NotFound();
                var movements = await _api.GetStockMovementsAsync(id, Math.Max(page, 1));
                if (movements == null) return NotFound();
                ViewBag.Product = product;
                ViewBag.CurrentPage = movements.Page;
                ViewBag.TotalPages = movements.TotalPages;
                return View(movements.Items);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return await SessionExpiredAsync();
            }
        }

        [HttpPost("Inventory/Product/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddMovement(int id, int quantityChange, string reason, string? note, int page = 1)
        {
            var result = await _api.AddStockMovementAsync(id, new StockMovementRequest(quantityChange, reason, note));
            if (result.Unauthorized) return await SessionExpiredAsync();
            if (result.NotFound) return NotFound();
            if (result.Success) TempData["Success"] = "Stock movement recorded.";
            else if (result.Conflict != null) TempData["Error"] = result.Conflict;
            else if (result.Errors.Count > 0) TempData["Error"] = string.Join(" ", result.Errors.Values.SelectMany(v => v));
            else if (result.Forbidden) TempData["Error"] = "You do not have permission to change stock.";
            else if (result.TooManyRequests) TempData["Error"] = "Too many requests. Please try again later.";
            else TempData["Error"] = "Could not record the stock movement.";
            return RedirectToAction(nameof(History), new { id, page });
        }

        private async Task<IActionResult> SessionExpiredAsync()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login", "Account", new { returnUrl = Request.Path + Request.QueryString });
        }
    }
}
