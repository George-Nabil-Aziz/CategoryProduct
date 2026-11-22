using CategoryProduct.MVC.Models;
using CategoryProduct.MVC.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CategoryProduct.MVC.Controllers
{
    [Authorize]
    public class OrdersController : Controller
    {
        private static readonly string[] Statuses = { "Pending", "Confirmed", "Cancelled" };
        private readonly CategoryProductApiClient _api;

        public OrdersController(CategoryProductApiClient api) => _api = api;

        public async Task<IActionResult> Index(int page = 1)
        {
            var result = await _api.GetMyOrdersAsync(Math.Max(page, 1));
            if (result.Unauthorized) return await SessionExpiredAsync();
            if (result.Value == null) return NotFound();
            ViewBag.CurrentPage = result.Value.Page;
            ViewBag.TotalPages = result.Value.TotalPages;
            return View(result.Value.Items);
        }

        [HttpGet("Orders/{id:int}")]
        public async Task<IActionResult> Details(int id)
        {
            var result = await _api.GetOrderAsync(id);
            if (result.Unauthorized) return await SessionExpiredAsync();
            if (result.NotFound || result.Value == null) return NotFound();
            return View(result.Value);
        }

        [HttpPost("Orders/{id:int}/Cancel")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancel(int id, string? returnTo = null, int page = 1, string? filter = null)
        {
            var result = await _api.CancelOrderAsync(id);
            if (result.Unauthorized) return await SessionExpiredAsync();
            if (result.Success) TempData["Success"] = "Order cancelled.";
            else TempData["Error"] = ErrorMessage(result, "Could not cancel the order.");
            if (returnTo == "manage" && User.IsInRole("Admin"))
                return RedirectToAction(nameof(Manage), new { page, status = filter });
            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost("Orders/{id:int}/Pay")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Pay(int id, string? paymentToken, string? idempotencyKey)
        {
            ApiResult<PaymentModel> result;
            try
            {
                result = await _api.PayOrderAsync(id, paymentToken ?? string.Empty, idempotencyKey ?? string.Empty);
            }
            catch (ApiUnavailableException)
            {
                // The API answered 5xx (for example 502 "Payment provider error") or could not be reached.
                TempData["Error"] = "The payment could not be completed because the payment service is unavailable. Please try again later.";
                return RedirectToAction(nameof(Details), new { id });
            }
            if (result.Unauthorized) return await SessionExpiredAsync();
            if (result.Success) TempData["Success"] = "Payment received.";
            else TempData["Error"] = ErrorMessage(result, "Could not process the payment.");
            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpGet("Orders/Manage")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Manage(int page = 1, string? status = null)
        {
            var filter = Statuses.FirstOrDefault(s => string.Equals(s, status, StringComparison.OrdinalIgnoreCase));
            var result = await _api.GetAllOrdersAsync(filter, Math.Max(page, 1));
            if (result.Unauthorized) return await SessionExpiredAsync();
            if (result.Value == null) return NotFound();
            ViewBag.CurrentPage = result.Value.Page;
            ViewBag.TotalPages = result.Value.TotalPages;
            ViewBag.Status = filter;
            return View(result.Value.Items);
        }

        [HttpPost("Orders/{id:int}/Status")]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetStatus(int id, string status, string? returnTo = null, int page = 1, string? filter = null)
        {
            var result = await _api.SetOrderStatusAsync(id, status);
            if (result.Unauthorized) return await SessionExpiredAsync();
            if (result.Success) TempData["Success"] = "Order updated.";
            else TempData["Error"] = ErrorMessage(result, "Could not update the order.");
            if (returnTo == "manage") return RedirectToAction(nameof(Manage), new { page, status = filter });
            return RedirectToAction(nameof(Details), new { id });
        }

        private static string ErrorMessage(ApiResult result, string fallback)
        {
            if (result.Declined != null) return result.Declined;
            if (result.Conflict != null) return result.Conflict;
            if (result.Errors.Count > 0) return string.Join(" ", result.Errors.Values.SelectMany(v => v));
            if (result.NotFound) return "Order not found.";
            if (result.Forbidden) return "You do not have permission to do that.";
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
