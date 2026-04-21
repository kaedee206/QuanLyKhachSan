using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuanLyKhachSan.Models.ViewModels;
using QuanLyKhachSan.Services;
using System.Security.Claims;

namespace QuanLyKhachSan.Controllers
{
    public class BookingController : Controller
    {
        private readonly BookingService _bookingService;
        private readonly RoomService _roomService;
        private readonly ServiceManagementService _serviceService;
        private readonly EmailSftpService _emailService;

        public BookingController(
            BookingService bookingService,
            RoomService roomService,
            ServiceManagementService serviceService,
            EmailSftpService emailService)
        {
            _bookingService = bookingService;
            _roomService = roomService;
            _serviceService = serviceService;
            _emailService = emailService;
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            ViewBag.RoomTypes = await _roomService.GetAllRoomTypes();
            return View(new CreateBookingViewModel
            {
                CheckInDate = DateOnly.FromDateTime(DateTime.Today),
                CheckOutDate = DateOnly.FromDateTime(DateTime.Today.AddDays(1))
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateBookingViewModel model)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.RoomTypes = await _roomService.GetAllRoomTypes();
                return View(model);
            }

            try
            {
                int? userId = null;
                if (User.Identity?.IsAuthenticated == true)
                    userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

                var booking = await _bookingService.CreateBooking(model, userId);
                TempData["Success"] = $"Đặt phòng thành công! Mã booking: {booking.BookingCode}";

                await _emailService.SendBookingConfirmationEmail(booking);

                return RedirectToAction("Confirmation", new { code = booking.BookingCode });
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                ViewBag.RoomTypes = await _roomService.GetAllRoomTypes();
                return View(model);
            }
        }

        [HttpGet]
        public async Task<IActionResult> Confirmation(string code)
        {
            var detail = await _bookingService.GetBookingDetailByCode(code);
            if (detail == null) return NotFound();
            return View(detail);
        }

        [HttpGet]
        public IActionResult Lookup()
        {
            return View(new BookingLookupViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Lookup(BookingLookupViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var booking = await _bookingService.LookupBooking(model.BookingCode, model.Phone);
            if (booking == null)
            {
                ModelState.AddModelError("", "Không tìm thấy booking hoặc số điện thoại không khớp");
                return View(model);
            }

            return RedirectToAction("Confirmation", new { code = booking.BookingCode });
        }

        [Authorize(Roles = "Admin,Manager,Receptionist")]
        [HttpGet]
        public async Task<IActionResult> Index(BookingFilterViewModel filter)
        {
            var result = await _bookingService.GetBookings(filter);
            ViewBag.Filter = filter;
            return View(result);
        }

        [Authorize(Roles = "Admin,Manager,Receptionist")]
        [HttpGet]
        public async Task<IActionResult> Detail(int id)
        {
            var detail = await _bookingService.GetBookingDetail(id);
            if (detail == null) return NotFound();
            return View(detail);
        }

        [Authorize(Roles = "Admin,Manager,Receptionist")]
        [HttpGet]
        public async Task<IActionResult> AdminCreate()
        {
            ViewBag.RoomTypes = await _roomService.GetAllRoomTypes();
            return View(new CreateBookingViewModel
            {
                CheckInDate = DateOnly.FromDateTime(DateTime.Today),
                CheckOutDate = DateOnly.FromDateTime(DateTime.Today.AddDays(1))
            });
        }

        [Authorize(Roles = "Admin,Manager,Receptionist")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdminCreate(CreateBookingViewModel model)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.RoomTypes = await _roomService.GetAllRoomTypes();
                return View(model);
            }

            try
            {
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
                var booking = await _bookingService.CreateBooking(model, userId);

                await _emailService.SendBookingConfirmationEmail(booking);

                TempData["Success"] = $"Tạo booking thành công: {booking.BookingCode}";
                return RedirectToAction("Detail", new { id = booking.Id });
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                ViewBag.RoomTypes = await _roomService.GetAllRoomTypes();
                return View(model);
            }
        }

        [Authorize(Roles = "Admin,Manager,Receptionist")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Confirm(string code, int roomId)
        {
            try
            {
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
                await _bookingService.ConfirmBooking(code, roomId, userId);
                TempData["Success"] = "Xác nhận booking thành công";
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
            }
            return RedirectToAction("Index");
        }

        [Authorize(Roles = "Admin,Manager,Receptionist")]
        [HttpGet]
        public async Task<IActionResult> CheckIn()
        {
            var filter = new BookingFilterViewModel
            {
                Status = Models.Enums.BookingStatus.Confirmed,
                PageSize = 100
            };
            var bookings = await _bookingService.GetBookings(filter);
            return View(bookings);
        }

        [Authorize(Roles = "Admin,Manager,Receptionist")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CheckIn(string code, CheckInViewModel model)
        {
            try
            {
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
                await _bookingService.CheckIn(code, model, userId);
                TempData["Success"] = "Check-in thành công";
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
            }
            return RedirectToAction("CheckIn");
        }

        [Authorize(Roles = "Admin,Manager,Receptionist")]
        [HttpGet]
        public async Task<IActionResult> CheckOut()
        {
            var filter = new BookingFilterViewModel
            {
                Status = Models.Enums.BookingStatus.CheckedIn,
                PageSize = 100
            };
            var bookings = await _bookingService.GetBookings(filter);
            return View(bookings);
        }

        [Authorize(Roles = "Admin,Manager,Receptionist")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CheckOut(string code, CheckOutViewModel model)
        {
            try
            {
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
                var (booking, invoice) = await _bookingService.CheckOut(code, model, userId);
                TempData["Success"] = $"Check-out thành công. Hóa đơn: {invoice.InvoiceNumber}";
                return RedirectToAction("Detail", "Invoice", new { id = invoice.InvoiceNumber });
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
                return RedirectToAction("CheckOut");
            }
        }

        [Authorize(Roles = "Admin,Manager,Receptionist")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancel(string code)
        {
            try
            {
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
                await _bookingService.CancelBooking(code, userId);
                TempData["Success"] = "Hủy booking thành công";
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
            }
            return RedirectToAction("Index");
        }

        [Authorize(Roles = "Admin,Manager,Receptionist")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkNoShow(string code)
        {
            try
            {
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
                await _bookingService.MarkNoShow(code, userId);
                TempData["Success"] = "Đánh dấu no-show thành công";
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
            }
            return RedirectToAction("Index");
        }
    }
}
