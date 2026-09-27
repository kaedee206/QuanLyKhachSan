using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuanLyKhachSan.Models.ViewModels;
using QuanLyKhachSan.Services;
using System.Security.Claims;

namespace QuanLyKhachSan.Controllers
{
    [Authorize(Roles = "Admin,Manager,Receptionist")]
    public class ServiceController : Controller
    {
        private readonly ServiceManagementService _serviceService;

        public ServiceController(ServiceManagementService serviceService)
        {
            _serviceService = serviceService;
        }

        [HttpGet]
        public async Task<IActionResult> Index(int page = 1)
        {
            var services = await _serviceService.GetAllServices(page);
            return View(services);
        }

        [HttpGet]
        public IActionResult Create(string? bookingCode = null, string? invoiceNumber = null)
        {
            return View(new AddServiceViewModel
            {
                BookingCode = bookingCode ?? string.Empty,
                ReturnInvoiceNumber = invoiceNumber
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(AddServiceViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            try
            {
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
                await _serviceService.AddService(model, userId);
                TempData["Success"] = "Thêm dịch vụ thành công";

                if (!string.IsNullOrEmpty(model.ReturnInvoiceNumber))
                {
                    return RedirectToAction("Detail", "Invoice", new { id = model.ReturnInvoiceNumber });
                }

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
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            await _serviceService.DeleteService(id, userId);
            TempData["Success"] = "Xóa dịch vụ thành công";
            return RedirectToAction("Index");
        }
    }
}
