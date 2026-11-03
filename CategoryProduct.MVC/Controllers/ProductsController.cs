using CategoryProduct.MVC.Models;
using CategoryProduct.MVC.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace CategoryProduct.MVC.Controllers
{
    public class ProductsController : Controller
    {
        private readonly CategoryProductApiClient _api;

        public ProductsController(CategoryProductApiClient api) => _api = api;

        public async Task<IActionResult> Index(int page = 1, string? search = null, int? categoryId = null,
            decimal? minPrice = null, decimal? maxPrice = null, string? sortBy = null, bool desc = false)
        {
            var result = await _api.GetProductsAsync(Math.Max(page, 1), 5, search, categoryId, minPrice, maxPrice, sortBy, desc);
            var categories = await _api.GetAllCategoriesAsync();
            var names = categories.ToDictionary(c => c.Id, c => c.Name);
            ViewBag.Categories = new SelectList(categories, "Id", "Name", categoryId);
            ViewBag.Search = search; ViewBag.CategoryId = categoryId; ViewBag.MinPrice = minPrice;
            ViewBag.MaxPrice = maxPrice; ViewBag.SortBy = sortBy; ViewBag.Desc = desc;
            foreach (var p in result.Items)
                p.CategoryName = names.GetValueOrDefault(p.CategoryId);
            ViewBag.CurrentPage = result.Page;
            ViewBag.TotalPages = result.TotalPages;
            return View(result.Items);
        }

        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();
            var product = await _api.GetProductAsync(id.Value);
            if (product == null) return NotFound();
            await ResolveCategoryNameAsync(product);
            return View(product);
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create()
        {
            await LoadCategoriesAsync(null);
            return View();
        }

        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Id,Name,Price,Quantity,CategoryId")] Product product)
        {
            if (ModelState.IsValid)
            {
                var result = await _api.CreateProductAsync(product);
                if (result.Success) return RedirectToAction(nameof(Index));
                if (result.NotFound) return NotFound();
                if (result.Unauthorized) return await SessionExpiredAsync();
                AddErrors(result);
            }
            await LoadCategoriesAsync(product.CategoryId);
            return View(product);
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var product = await _api.GetProductAsync(id.Value);
            if (product == null) return NotFound();
            await LoadCategoriesAsync(product.CategoryId);
            return View(product);
        }

        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int? id, [Bind("Id,Name,Price,Quantity,CategoryId")] Product product)
        {
            if (id != product.Id) return NotFound();
            if (ModelState.IsValid)
            {
                var result = await _api.UpdateProductAsync(product.Id, product);
                if (result.Success) return RedirectToAction(nameof(Index));
                if (result.NotFound) return NotFound();
                if (result.Unauthorized) return await SessionExpiredAsync();
                AddErrors(result);
            }
            await LoadCategoriesAsync(product.CategoryId);
            return View(product);
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();
            var product = await _api.GetProductAsync(id.Value);
            if (product == null) return NotFound();
            await ResolveCategoryNameAsync(product);
            return View(product);
        }

        [Authorize(Roles = "Admin")]
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int? id)
        {
            if (id == null) return NotFound();
            var result = await _api.DeleteProductAsync(id.Value);
            if (result.Success) return RedirectToAction(nameof(Index));
            if (result.NotFound) return NotFound();
            if (result.Unauthorized) return await SessionExpiredAsync();
            AddErrors(result);
            var product = await _api.GetProductAsync(id.Value);
            if (product == null) return NotFound();
            await ResolveCategoryNameAsync(product);
            return View("Delete", product);
        }

        private async Task LoadCategoriesAsync(int? selected) =>
            ViewData["CategoryId"] = new SelectList(await _api.GetAllCategoriesAsync(), "Id", "Name", selected);

        private async Task ResolveCategoryNameAsync(Product product) =>
            product.CategoryName = (await _api.GetCategoryAsync(product.CategoryId))?.Name;

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
