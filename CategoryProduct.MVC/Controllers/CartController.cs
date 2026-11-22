using CategoryProduct.MVC.Models;
using CategoryProduct.MVC.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CategoryProduct.MVC.Controllers
{
    [Authorize]
    public class CartController : Controller
    {
        private readonly CategoryProductApiClient _api;

        public CartController(CategoryProductApiClient api) => _api = api;

        public async Task<IActionResult> Index()
        {
            var result = await _api.GetCartAsync();
            if (result.Unauthorized) return await SessionExpiredAsync();
            return View(result.Value ?? new CartModel(Array.Empty<CartLine>(), 0, 0m));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Add(int productId, int quantity = 1, string? returnUrl = null)
        {
            var result = await _api.AddCartItemAsync(productId, quantity);
            if (result.Unauthorized) return await SessionExpiredAsync();
            if (result.Success) TempData["Success"] = "Added to cart.";
            else TempData["Error"] = ErrorMessage(result, "Could not add the product to the cart.");
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)) return LocalRedirect(returnUrl);
            return RedirectToAction("Index", "Products");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Update(int productId, int quantity)
        {
            var result = await _api.UpdateCartItemAsync(productId, quantity);
            if (result.Unauthorized) return await SessionExpiredAsync();
            if (result.Success) TempData["Success"] = "Cart updated.";
            else TempData["Error"] = ErrorMessage(result, "Could not update the cart.");
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Remove(int productId)
        {
            var result = await _api.RemoveCartItemAsync(productId);
            if (result.Unauthorized) return await SessionExpiredAsync();
            if (result.Success) TempData["Success"] = "Item removed.";
            else TempData["Error"] = ErrorMessage(result, "Could not remove the item.");
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Clear()
        {
            var result = await _api.ClearCartAsync();
            if (result.Unauthorized) return await SessionExpiredAsync();
            if (result.Success) TempData["Success"] = "Cart cleared.";
            else TempData["Error"] = ErrorMessage(result, "Could not clear the cart.");
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Checkout()
        {
            var result = await _api.CheckoutAsync();
            if (result.Unauthorized) return await SessionExpiredAsync();
            if (result.Success && result.Value != null)
            {
                TempData["Success"] = "Order placed.";
                return RedirectToAction("Details", "Orders", new { id = result.Value.Id });
            }
            if (result.Conflict == null && result.Errors.Count > 0) TempData["Error"] = "Your cart is empty.";
            else TempData["Error"] = ErrorMessage(result, "Could not place the order.");
            return RedirectToAction(nameof(Index));
        }

        private static string ErrorMessage(ApiResult result, string fallback)
        {
            if (result.Conflict != null) return result.Conflict;
            if (result.Errors.Count > 0) return string.Join(" ", result.Errors.Values.SelectMany(v => v));
            if (result.NotFound) return "Not found.";
            if (result.TooManyRequests) return "Too many requests. Please try again later.";
            return fallback;
        }

        private async Task<IActionResult> SessionExpiredAsync()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login", "Account", new { returnUrl = Request.Path + Request.QueryString });
        }
    }
}
