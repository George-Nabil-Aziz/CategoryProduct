using System.Security.Claims;
using CategoryProduct.MVC.Models;
using CategoryProduct.MVC.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
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

            var result = await _api.RegisterAsync(model.UserName, model.Password, model.Email);
            if (!result.Success)
            {
                if (result.TooManyRequests)
                    ModelState.AddModelError(string.Empty, "Too many attempts. Try again in a minute.");
                else if (result.Conflict == "Email already in use.")
                    ModelState.AddModelError(nameof(model.Email), "Email already in use.");
                else if (result.Conflict != null)
                    ModelState.AddModelError(nameof(model.UserName), "Username already taken.");
                else if (result.Errors.Count > 0)
                    foreach (var (field, messages) in result.Errors)
                        foreach (var m in messages)
                            ModelState.AddModelError(
                                field is "UserName" or "Password" or "Email" ? field : string.Empty, m);
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
        [Authorize]
        public async Task<IActionResult> Profile()
        {
            var me = await _api.GetMeAsync();
            if (me.Unauthorized) return await SessionExpiredAsync();
            if (me.TooManyRequests)
                throw new HttpRequestException("Too many requests.", null, System.Net.HttpStatusCode.TooManyRequests);
            if (!me.Success) throw new HttpRequestException("Could not load the profile.");

            ViewData["ProfileUserName"] = me.Value!.UserName;
            ViewData["ProfileRole"] = me.Value.Role;
            return View(new ProfileViewModel { Email = me.Value.Email });
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Profile(ProfileViewModel model)
        {
            ViewData["ProfileUserName"] = User.Identity?.Name;
            ViewData["ProfileRole"] = User.FindFirst(ClaimTypes.Role)?.Value;
            if (!ModelState.IsValid) return View(model);

            var result = await _api.SetEmailAsync(model.Email);
            if (result.Unauthorized) return await SessionExpiredAsync();
            if (result.Success)
            {
                TempData["Success"] = result.Value?.Email == null ? "Email removed." : "Email saved.";
                return RedirectToAction(nameof(Profile));
            }

            if (result.TooManyRequests)
                ModelState.AddModelError(string.Empty, ApiRateLimit.Message);
            else if (result.Conflict != null)
                ModelState.AddModelError(nameof(model.Email), result.Conflict);
            else if (result.Errors.Count > 0)
                foreach (var (field, messages) in result.Errors)
                    foreach (var m in messages)
                        ModelState.AddModelError(field == "Email" ? field : string.Empty, m);
            else
                ModelState.AddModelError(string.Empty, "Could not save the email. Please try again.");
            return View(model);
        }

        // ---------- Password management ----------
        private const string InvalidResetLinkMessage = "This reset link is invalid or has expired.";

        [HttpGet]
        public IActionResult ForgotPassword() => View(new ForgotPasswordViewModel());

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var result = await _api.ForgotPasswordAsync(model.UserNameOrEmail.Trim());
            if (result.TooManyRequests)
            {
                ModelState.AddModelError(string.Empty, ApiRateLimit.Message);
                return View(model);
            }
            if (result.Errors.Count > 0)
            {
                foreach (var (_, messages) in result.Errors)
                    foreach (var m in messages)
                        ModelState.AddModelError(nameof(model.UserNameOrEmail), m);
                return View(model);
            }
            return RedirectToAction(nameof(ForgotPasswordConfirmation));
        }

        [HttpGet]
        public IActionResult ForgotPasswordConfirmation() => View();

        [HttpGet]
        public IActionResult ResetPassword(string? token = null)
        {
            ApplyResetPageHeaders();
            if (string.IsNullOrWhiteSpace(token))
            {
                ViewData["InvalidLink"] = InvalidResetLinkMessage;
                return View(new ResetPasswordViewModel());
            }
            return View(new ResetPasswordViewModel { Token = token });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
        {
            ApplyResetPageHeaders();
            if (!ModelState.IsValid) return View(model);

            var result = await _api.ResetPasswordAsync(model.Token, model.NewPassword);
            if (result.Success)
            {
                TempData["Success"] = "Your password was changed. Sign in with your new password.";
                return RedirectToAction(nameof(Login));
            }

            if (result.TooManyRequests)
                ModelState.AddModelError(string.Empty, ApiRateLimit.Message);
            else if (result.Errors.ContainsKey("Token"))
            {
                ViewData["InvalidLink"] = InvalidResetLinkMessage;
                return View(new ResetPasswordViewModel());
            }
            else if (result.Errors.Count > 0)
                foreach (var (field, messages) in result.Errors)
                    foreach (var m in messages)
                        ModelState.AddModelError(field == "NewPassword" ? nameof(model.NewPassword) : string.Empty, m);
            else
                ModelState.AddModelError(string.Empty, "Could not reset the password. Please try again.");
            return View(model);
        }

        private void ApplyResetPageHeaders()
        {
            Response.Headers["Referrer-Policy"] = "no-referrer";
            Response.Headers["Cache-Control"] = "no-store";
        }

        [HttpGet]
        [Authorize]
        public IActionResult ChangePassword() => View(new ChangePasswordViewModel());

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var result = await _api.ChangePasswordAsync(model.CurrentPassword, model.NewPassword);
            if (result.Unauthorized) return await SessionExpiredAsync(nameof(ChangePassword));
            if (result.Success && result.Value != null)
            {
                await SignInAsync(User.Identity?.Name ?? string.Empty, result.Value);
                TempData["Success"] = "Password changed.";
                return RedirectToAction(nameof(Profile));
            }

            if (result.TooManyRequests)
                ModelState.AddModelError(string.Empty, ApiRateLimit.Message);
            else if (result.Errors.Count > 0)
                foreach (var (field, messages) in result.Errors)
                    foreach (var m in messages)
                        ModelState.AddModelError(
                            field switch
                            {
                                "CurrentPassword" => nameof(model.CurrentPassword),
                                "NewPassword" => nameof(model.NewPassword),
                                _ => string.Empty
                            }, m);
            else
                ModelState.AddModelError(string.Empty, "Could not change the password. Please try again.");
            return View(model);
        }

        private async Task<IActionResult> SessionExpiredAsync(string returnAction = nameof(Profile))
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction(nameof(Login), new { returnUrl = Url.Action(returnAction) });
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
