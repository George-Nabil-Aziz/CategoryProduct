using CategoryProduct.MVC.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CategoryProduct.MVC.Controllers
{
    [Authorize(Roles = "Admin")]
    public class UsersController : Controller
    {
        private readonly CategoryProductApiClient _api;

        public UsersController(CategoryProductApiClient api) => _api = api;

        public async Task<IActionResult> Index(int page = 1, string? search = null)
        {
            var result = await _api.GetUsersAsync(Math.Max(page, 1), search: search);
            ViewBag.Search = search;
            ViewBag.CurrentPage = result.Page;
            ViewBag.TotalPages = result.TotalPages;
            return View(result.Items);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangeRole(int id, string role, int page = 1, string? search = null)
        {
            var result = await _api.ChangeUserRoleAsync(id, role);
            if (result.Success) TempData["Success"] = "Role updated.";
            else if (result.Conflict != null) TempData["Error"] = result.Conflict;
            else if (result.Errors.Count > 0) TempData["Error"] = string.Join(" ", result.Errors.Values.SelectMany(v => v));
            else if (result.NotFound) TempData["Error"] = "User not found.";
            else TempData["Error"] = "Could not change role.";
            return RedirectToAction(nameof(Index), new { page, search });
        }
    }
}
