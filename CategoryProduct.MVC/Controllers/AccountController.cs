using System.Security.Claims;
using CategoryProduct.MVC.Models;
using CategoryProduct.MVC.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace CategoryProduct.MVC.Controllers
{
    public class AccountController : Controller
    {
        private readonly CategoryProductApiClient _api;

        private readonly AuthSessionService _session;

        public AccountController(CategoryProductApiClient api, AuthSessionService session)
        {
            _api = api;
            _session = session;
        }

        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            return View(new LoginViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            if (!ModelState.IsValid) return View(model);

            var login = await _api.LoginAsync(model.UserName, model.Password);
            if (ReferenceEquals(login, CategoryProductApiClient.LoginResult.TooManyRequests))
            {
                ModelState.AddModelError(string.Empty, "Too many login attempts. Try again in a minute.");
                return View(model);
            }
            if (login == null)
            {
                ModelState.AddModelError(string.Empty, "Invalid username or password.");
                return View(model);
            }

            await SignInAsync(model.UserName, login);

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)) return LocalRedirect(returnUrl);
            return RedirectToAction("Index", "Categories");
        }

        private Task SignInAsync(string userName, CategoryProductApiClient.LoginResult login) =>
            _session.SignInAsync(HttpContext, userName, login);

        [HttpGet]
        public IActionResult Register() => View(new RegisterViewModel());

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var result = await _api.RegisterAsync(model.UserName, model.Password);
            if (!result.Success)
            {
                if (result.TooManyRequests)
                    ModelState.AddModelError(string.Empty, "Too many attempts. Try again in a minute.");
                else if (result.Conflict != null)
                    ModelState.AddModelError(nameof(model.UserName), "Username already taken.");
                else if (result.Errors.Count > 0)
                    foreach (var (field, messages) in result.Errors)
                        foreach (var m in messages)
                            ModelState.AddModelError(
                                field is "UserName" or "Password" ? field : string.Empty, m);
                else
                    ModelState.AddModelError(string.Empty, "Registration failed. Please try again.");
                return View(model);
            }

            var login = await _api.LoginAsync(model.UserName, model.Password);
            if (login == null || ReferenceEquals(login, CategoryProductApiClient.LoginResult.TooManyRequests))
                return RedirectToAction(nameof(Login));

            await SignInAsync(model.UserName, login);
            return RedirectToAction("Index", "Categories");
        }

        [HttpGet]
        public IActionResult AccessDenied() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _api.LogoutAsync(User.FindFirst("refresh_token")?.Value);
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Index", "Categories");
        }
    }
}
