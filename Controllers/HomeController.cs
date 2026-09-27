using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using QuanLyKhachSan.Models;
using QuanLyKhachSan.Models.Enums;
using QuanLyKhachSan.Models.ViewModels;
using QuanLyKhachSan.Services;

namespace QuanLyKhachSan.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly RoomService _roomService;

        public HomeController(ILogger<HomeController> logger, RoomService roomService)
        {
            _logger = logger;
            _roomService = roomService;
        }

        public async Task<IActionResult> Index()
        {
            var allRooms = await _roomService.FindAvailableRooms(new RoomSearchViewModel());
            var roomTypes = await _roomService.GetAllRoomTypes();

            ViewBag.RoomTypes = roomTypes;
            ViewBag.AvailableByType = allRooms
                .Where(r => r.Status == RoomStatus.Available)
                .GroupBy(r => r.RoomTypeId)
                .ToDictionary(g => g.Key, g => g.Count());
            ViewBag.TotalByType = allRooms
                .GroupBy(r => r.RoomTypeId)
                .ToDictionary(g => g.Key, g => g.Count());
            ViewBag.TotalAvailable = allRooms.Count(r => r.Status == RoomStatus.Available);
            ViewBag.TotalRooms = allRooms.Count;

            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
