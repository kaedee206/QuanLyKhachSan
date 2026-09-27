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
        private readonly RoomService _roomService;

        public ReportController(ReportService reportService, RoomService roomService)
        {
            _reportService = reportService;
            _roomService = roomService;
        }

        [HttpGet]
        public IActionResult Index(string range = "7days")
        {
            return RedirectToAction("PmsOverview", new { range });
        }

        [HttpGet]
        public async Task<IActionResult> PmsOverview(string range = "7days")
        {
            var dashboard = await _reportService.GetDashboardSummary(range);
            var roomGrid = await _roomService.GetRoomGrid();
            dashboard.Floors = roomGrid.Floors;
            dashboard.RoomGrid = roomGrid.Floors.SelectMany(f => f.Rooms).ToList();
            return View(dashboard);
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
