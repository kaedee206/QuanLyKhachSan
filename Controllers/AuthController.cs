using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using QuanLyKhachSan.Models.ViewModels;
using QuanLyKhachSan.Services;
using System.Security.Claims;

namespace QuanLyKhachSan.Controllers
{
    public class AuthController : Controller
    {
        private readonly AuthService _authService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(AuthService authService, ILogger<AuthController> logger)
        {
            _authService = authService;
            _logger = logger;
        }

        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                var role = User.FindFirstValue(ClaimTypes.Role);
                if (!string.IsNullOrEmpty(role))
                {
                    return Redirect(RedirectByRoleName(role));
                }
                return RedirectToAction("Index", "Admin");
            }
            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            if (!ModelState.IsValid)
                return View(model);

            var user = await _authService.ValidateUser(model.Username, model.Password);
            if (user == null)
            {
                ModelState.AddModelError("", "Tên đăng nhập hoặc mật khẩu không đúng");
                return View(model);
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new(ClaimTypes.Name, user.Username),
                new("FullName", user.FullName),
                new(ClaimTypes.Role, user.Role.ToString()),
                new(ClaimTypes.Email, user.Email ?? "")
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                new AuthenticationProperties
                {
                    IsPersistent = model.RememberMe,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddHours(24)
                });

            _logger.LogInformation("User {Username} logged in with role {Role}", user.Username, user.Role);

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            return RedirectByRole(user.Role.ToString());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (int.TryParse(userId, out var id))
            {
                await _authService.LogLogout(id);
            }

            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login");
        }

        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }

        [HttpGet]
        public IActionResult ChangePassword()
        {
            if (User.Identity?.IsAuthenticated != true)
                return RedirectToAction("Login");
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var result = await _authService.ChangePassword(userId, model.CurrentPassword, model.NewPassword);

            if (!result)
            {
                ModelState.AddModelError("", "Mật khẩu hiện tại không đúng");
                return View(model);
            }

            TempData["Success"] = "Đổi mật khẩu thành công";
            return RedirectToAction("Index", "Admin");
        }

        private IActionResult RedirectByRole(string role)
        {
            var (action, controller) = GetRedirectTarget(role);
            return RedirectToAction(action, controller);
        }

        private string RedirectByRoleName(string role)
        {
            var (action, controller) = GetRedirectTarget(role);
            return $"/{controller}/{action}";
        }

        private (string action, string controller) GetRedirectTarget(string role)
        {
            return role.ToLower() switch
            {
                "admin" => ("Index", "Admin"),
                "manager" => ("Revenue", "Report"),
                "receptionist" => ("Index", "Booking"),
                "housekeeping" => ("Cleaning", "Room"),
                "maintenance" => ("Index", "Ticket"),
                _ => ("Index", "Admin")
            };
        }
    }
}
