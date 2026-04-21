using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuanLyKhachSan.Models.Enums;
using QuanLyKhachSan.Models.ViewModels;
using QuanLyKhachSan.Services;

namespace QuanLyKhachSan.Controllers
{
    [Authorize(Roles = "Admin")]
    public class UserController : Controller
    {
        private readonly UserService _userService;

        public UserController(UserService userService)
        {
            _userService = userService;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string? search = null, UserRole? role = null, int page = 1)
        {
            var users = await _userService.GetUsers(search, role, page);
            ViewBag.Search = search;
            ViewBag.Role = role;
            return View(users);
        }

        [HttpGet]
        public IActionResult Create()
        {
            return View(new CreateUserViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateUserViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            try
            {
                await _userService.CreateUser(model);
                TempData["Success"] = "Tạo tài khoản thành công";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return View(model);
            }
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var user = await _userService.GetUserById(id);
            if (user == null) return NotFound();

            return View(new EditUserViewModel
            {
                Id = user.Id,
                FullName = user.FullName,
                Role = user.Role,
                Email = user.Email,
                Phone = user.Phone,
                IsActive = user.IsActive
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, EditUserViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            try
            {
                await _userService.UpdateUser(id, model);
                TempData["Success"] = "Cập nhật tài khoản thành công";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return View(model);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            await _userService.DeleteUser(id);
            TempData["Success"] = "Vô hiệu hóa tài khoản thành công";
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["Error"] = "Dữ liệu không hợp lệ";
                return RedirectToAction("Edit", new { id = model.UserId });
            }

            var result = await _userService.ResetPassword(model.UserId, model.NewPassword);
            if (result)
                TempData["Success"] = "Reset mật khẩu thành công";
            else
                TempData["Error"] = "Không tìm thấy tài khoản";

            return RedirectToAction("Index");
        }
    }
}
