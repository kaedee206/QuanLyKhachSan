using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuanLyKhachSan.Services;

namespace QuanLyKhachSan.Controllers
{
    [Authorize(Roles = "Admin,Manager,Receptionist")]
    public class AdminController : Controller
    {
        private readonly ReportService _reportService;
        private readonly RoomService _roomService;

        public AdminController(ReportService reportService, RoomService roomService)
        {
            _reportService = reportService;
            _roomService = roomService;
        }

        public async Task<IActionResult> Index()
        {
            var dashboard = await _reportService.GetDashboardSummary();
            var roomGrid = await _roomService.GetRoomGrid();
            dashboard.RoomGrid = roomGrid.Floors.SelectMany(f => f.Rooms).ToList();
            return View(dashboard);
        }
    }
}
