using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuanLyKhachSan.Models.ViewModels;
using QuanLyKhachSan.Services;

namespace QuanLyKhachSan.Controllers
{
    [Authorize(Roles = "Admin,Manager")]
    public class ReportController : Controller
    {
        private readonly ReportService _reportService;

        public ReportController(ReportService reportService)
        {
            _reportService = reportService;
        }

        [HttpGet]
        public async Task<IActionResult> Revenue(ReportFilterViewModel? filter = null)
        {
            filter ??= new ReportFilterViewModel();
            var report = await _reportService.GetRevenueReport(filter);
            return View(report);
        }
    }
}
