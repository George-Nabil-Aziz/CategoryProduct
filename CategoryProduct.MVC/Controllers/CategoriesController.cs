using CategoryProduct.MVC.Models;
using CategoryProduct.MVC.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CategoryProduct.MVC.Controllers
{
    public class CategoriesController : Controller
    {
        private readonly CategoryProductApiClient _api;

        public CategoriesController(CategoryProductApiClient api) => _api = api;

        public async Task<IActionResult> Index(int page = 1)
        {
            var result = await _api.GetCategoriesAsync(Math.Max(page, 1));
            ViewBag.CurrentPage = result.Page;
            ViewBag.TotalPages = result.TotalPages;
            return View(result.Items);
        }

        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();
            var category = await _api.GetCategoryAsync(id.Value);
            if (category == null) return NotFound();
            category.Products = await _api.GetCategoryProductsAsync(id.Value);
            return View(category);
        }

        [Authorize(Roles = "Admin")]
        public IActionResult Create() => View();

        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Id,Name")] Category category)
        {
            if (!ModelState.IsValid) return View(category);
            var result = await _api.CreateCategoryAsync(category);
            if (result.Success) return RedirectToAction(nameof(Index));
            if (result.NotFound) return NotFound();
            if (result.Unauthorized) return await SessionExpiredAsync();
            AddErrors(result);
            return View(category);
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var category = await _api.GetCategoryAsync(id.Value);
            return category == null ? NotFound() : View(category);
        }

        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int? id, [Bind("Id,Name")] Category category)
        {
            if (id != category.Id) return NotFound();
            if (!ModelState.IsValid) return View(category);
            var result = await _api.UpdateCategoryAsync(category.Id, category);
            if (result.Success) return RedirectToAction(nameof(Index));
            if (result.NotFound) return NotFound();
            if (result.Unauthorized) return await SessionExpiredAsync();
            AddErrors(result);
            return View(category);
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();
            var category = await _api.GetCategoryAsync(id.Value);
            return category == null ? NotFound() : View(category);
        }

        [Authorize(Roles = "Admin")]
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int? id)
        {
            if (id == null) return NotFound();
            var result = await _api.DeleteCategoryAsync(id.Value);
            if (result.Success) return RedirectToAction(nameof(Index));
            if (result.NotFound) return NotFound();
            if (result.Unauthorized) return await SessionExpiredAsync();
            AddErrors(result);
            var category = await _api.GetCategoryAsync(id.Value);
            return category == null ? NotFound() : View("Delete", category);
        }

        private async Task<IActionResult> SessionExpiredAsync()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login", "Account", new { returnUrl = Request.Path + Request.QueryString });
        }

        private void AddErrors(ApiResult result)
        {
            if (result.Unauthorized)
                ModelState.AddModelError(string.Empty, "You must be signed in to make changes.");
            if (result.Forbidden)
                ModelState.AddModelError(string.Empty, "You don't have permission to make changes.");
            foreach (var (field, messages) in result.Errors)
                foreach (var m in messages)
                    ModelState.AddModelError(field, m);
        }
    }
}
