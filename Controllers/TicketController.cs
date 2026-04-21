using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuanLyKhachSan.Models.ViewModels;
using QuanLyKhachSan.Services;
using System.Security.Claims;

namespace QuanLyKhachSan.Controllers
{
    [Authorize]
    public class TicketController : Controller
    {
        private readonly TicketService _ticketService;
        private readonly RoomService _roomService;
        private readonly UserService _userService;

        public TicketController(TicketService ticketService, RoomService roomService, UserService userService)
        {
            _ticketService = ticketService;
            _roomService = roomService;
            _userService = userService;
        }

        [HttpGet]
        public async Task<IActionResult> Index(TicketFilterViewModel filter)
        {
            var tickets = await _ticketService.GetTickets(filter);
            ViewBag.Filter = filter;
            ViewBag.Rooms = await _roomService.GetAllRooms();
            return View(tickets);
        }

        [HttpGet]
        public async Task<IActionResult> Detail(int id)
        {
            var ticket = await _ticketService.GetTicketById(id);
            if (ticket == null) return NotFound();
            ViewBag.Users = await _userService.GetActiveUsers();
            return View(ticket);
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            ViewBag.Rooms = await _roomService.GetAllRooms();
            return View(new CreateTicketViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateTicketViewModel model)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.Rooms = await _roomService.GetAllRooms();
                return View(model);
            }

            try
            {
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
                var ticket = await _ticketService.CreateTicket(model, userId);
                TempData["Success"] = $"Tạo ticket thành công: {ticket.TicketNumber}";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                ViewBag.Rooms = await _roomService.GetAllRooms();
                return View(model);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Update(int id, UpdateTicketViewModel model)
        {
            try
            {
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
                await _ticketService.UpdateTicket(id, model, userId);
                TempData["Success"] = "Cập nhật ticket thành công";
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
            }
            return RedirectToAction("Detail", new { id });
        }
    }
}
