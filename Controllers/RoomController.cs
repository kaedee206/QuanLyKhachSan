using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuanLyKhachSan.Models.Enums;
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

        [HttpGet]
        public async Task<IActionResult> Listing(RoomSearchViewModel search)
        {
            var searchWithoutType = new RoomSearchViewModel
            {
                CheckInDate = search.CheckInDate,
                CheckOutDate = search.CheckOutDate,
                NumGuests = search.NumGuests
            };
            var allMatchingRooms = await _roomService.FindAvailableRooms(searchWithoutType);
            var roomTypes = await _roomService.GetAllRoomTypes();

            var rooms = search.RoomTypeId.HasValue
                ? allMatchingRooms.Where(r => r.RoomTypeId == search.RoomTypeId.Value).ToList()
                : allMatchingRooms;

            ViewBag.RoomTypes = roomTypes;
            ViewBag.Search = search;
            ViewBag.AvailableByType = allMatchingRooms
                .Where(r => r.Status == RoomStatus.Available)
                .GroupBy(r => r.RoomTypeId)
                .ToDictionary(g => g.Key, g => g.Count());
            ViewBag.TotalAvailableAll = allMatchingRooms.Count(r => r.Status == RoomStatus.Available);
            ViewBag.TotalRoomsAll = allMatchingRooms.Count;

            return View(rooms);
        }

        [Authorize(Roles = "Admin,Manager,Receptionist")]
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var grid = await _roomService.GetRoomGrid();
            return View(grid);
        }

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

        [Authorize(Roles = "Admin,Manager,Housekeeping,Receptionist")]
        [HttpGet]
        public async Task<IActionResult> Cleaning()
        {
            var rooms = await _roomService.GetAllRooms();
            var cleaningRooms = rooms.Where(r => r.Status == Models.Enums.RoomStatus.Cleaning).ToList();
            return View(cleaningRooms);
        }

        [Authorize(Roles = "Admin,Manager,Housekeeping,Receptionist")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkCleaned(int id, [FromQuery] string? returnUrl = null)
        {
            return await ProcessMarkCleaned(id, returnUrl);
        }

        [Authorize(Roles = "Admin,Manager,Housekeeping,Receptionist")]
        [HttpGet]
        [ActionName("MarkCleaned")]
        public async Task<IActionResult> MarkCleanedGet(int id, [FromQuery] string? returnUrl = null)
        {
            return await ProcessMarkCleaned(id, returnUrl);
        }

        private async Task<IActionResult> ProcessMarkCleaned(int id, string? returnUrl = null)
        {
            try
            {
                var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
                int userId = int.TryParse(userIdClaim, out var uid) ? uid : 1;
                var room = await _roomService.UpdateCleaningStatus(id, RoomStatus.Available, userId);
                TempData["Success"] = $"Phòng {room.RoomNumber} đã được dọn sạch và chuyển sang trạng thái Sẵn sàng đón khách (Available).";
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
            }

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            var referer = Request.Headers["Referer"].ToString();
            if (!string.IsNullOrEmpty(referer) && (referer.Contains("/Room") || referer.Contains("/Report") || referer.Contains("/Admin")))
            {
                return Redirect(referer);
            }

            return RedirectToAction("Cleaning");
        }

        [Authorize(Roles = "Admin,Manager,Housekeeping,Receptionist")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateCleaning(int id, Models.Enums.RoomStatus status)
        {
            try
            {
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
                await _roomService.UpdateCleaningStatus(id, status, userId);
                TempData["Success"] = "Cập nhật trạng thái dọn phòng thành công";
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
            }
            return RedirectToAction("Cleaning");
        }

        [HttpGet]
        public async Task<IActionResult> Types()
        {
            var types = await _roomService.GetAllRoomTypes();
            return View(types);
        }
    }
}
