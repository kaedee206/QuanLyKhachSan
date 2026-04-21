using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuanLyKhachSan.Models.ViewModels;
using QuanLyKhachSan.Services;
using System.Security.Claims;

namespace QuanLyKhachSan.Controllers
{
    public class RoomController : Controller
    {
        private readonly RoomService _roomService;

        public RoomController(RoomService roomService)
        {
            _roomService = roomService;
        }

        // ─── PUBLIC: Danh sách phòng ─────────────────────────────

        [HttpGet]
        public async Task<IActionResult> Listing(RoomSearchViewModel search)
        {
            var rooms = await _roomService.FindAvailableRooms(search);
            ViewBag.RoomTypes = await _roomService.GetAllRoomTypes();
            ViewBag.Search = search;
            return View(rooms);
        }

        // ─── ADMIN: Room Grid ────────────────────────────────────

        [Authorize(Roles = "Admin,Manager,Receptionist")]
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var grid = await _roomService.GetRoomGrid();
            return View(grid);
        }

        // ─── ADMIN: Quản lý phòng ───────────────────────────────

        [Authorize(Roles = "Admin")]
        [HttpGet]
        public async Task<IActionResult> Manage()
        {
            var rooms = await _roomService.GetAllRooms();
            return View(rooms);
        }

        [Authorize(Roles = "Admin,Manager,Receptionist")]
        [HttpGet]
        public async Task<IActionResult> Detail(int id)
        {
            var room = await _roomService.GetRoomById(id);
            if (room == null) return NotFound();
            return View(room);
        }

        // ─── ADMIN: Cập nhật trạng thái phòng ───────────────────

        [Authorize(Roles = "Admin,Manager,Receptionist")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateStatus(int id, UpdateRoomStatusViewModel model)
        {
            try
            {
                var userId = int.Parse(User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier)!);
                await _roomService.UpdateRoomStatus(id, model.Status, userId);
                TempData["Success"] = "Cập nhật trạng thái phòng thành công";
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
            }
            return RedirectToAction("Index");
        }

        // ─── Housekeeping: Dọn phòng ────────────────────────────

        [Authorize(Roles = "Admin,Housekeeping")]
        [HttpGet]
        public async Task<IActionResult> Cleaning()
        {
            var rooms = await _roomService.GetAllRooms();
            var cleaningRooms = rooms.Where(r =>
                r.Status == Models.Enums.RoomStatus.Cleaning ||
                r.Status == Models.Enums.RoomStatus.Available).ToList();
            return View(cleaningRooms);
        }

        [Authorize(Roles = "Admin,Housekeeping")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateCleaning(int id, Models.Enums.RoomStatus status)
        {
            try
            {
                var userId = int.Parse(User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier)!);
                await _roomService.UpdateCleaningStatus(id, status, userId);
                TempData["Success"] = "Cập nhật trạng thái dọn phòng thành công";
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
            }
            return RedirectToAction("Cleaning");
        }

        // ─── PUBLIC: Room Types ──────────────────────────────────

        [HttpGet]
        public async Task<IActionResult> Types()
        {
            var types = await _roomService.GetAllRoomTypes();
            return View(types);
        }
    }
}
