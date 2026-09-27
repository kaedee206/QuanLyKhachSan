using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
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
        private readonly EmailRateLimiter _rateLimiter;

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
            _rateLimiter = EmailRateLimiter.Instance;
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

                if (!string.IsNullOrEmpty(booking.Email))
                {
                    var rateKey = $"booking_conf:{booking.BookingCode}:{booking.Email}";
                    _rateLimiter.CheckAndConsume(rateKey, cooldownSeconds: 60, maxInWindow: 3, windowMinutes: 30);
                }

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

            if (!string.IsNullOrEmpty(detail.Booking.Email))
            {
                var rateKey = $"booking_conf:{detail.Booking.BookingCode}:{detail.Booking.Email}";
                ViewBag.ResendCooldownSeconds = _rateLimiter.GetRemainingCooldown(rateKey, cooldownSeconds: 60);
            }
            else
            {
                ViewBag.ResendCooldownSeconds = 0;
            }

            return View(detail);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResendConfirmationEmail(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                TempData["Error"] = "Mã đặt phòng không hợp lệ.";
                return RedirectToAction("Lookup");
            }

            var detail = await _bookingService.GetBookingDetailByCode(code);
            if (detail == null)
            {
                TempData["Error"] = "Không tìm thấy thông tin đặt phòng.";
                return RedirectToAction("Lookup");
            }

            var booking = detail.Booking;
            if (string.IsNullOrWhiteSpace(booking.Email))
            {
                TempData["Error"] = "Đơn đặt phòng này không có thông tin email để gửi lại.";
                return RedirectToAction("Confirmation", new { code = booking.BookingCode });
            }

            // Rate limit: 60s cooldown, max 3 in 30 minutes
            var rateKey = $"booking_conf:{booking.BookingCode}:{booking.Email}";
            var rateCheck = _rateLimiter.CheckAndConsume(rateKey, cooldownSeconds: 60, maxInWindow: 3, windowMinutes: 30);
            if (!rateCheck.Allowed)
            {
                TempData["Error"] = rateCheck.Message;
                return RedirectToAction("Confirmation", new { code = booking.BookingCode });
            }

            try
            {
                await _emailService.SendBookingConfirmationEmail(booking);
                TempData["Success"] = $"Đã gửi lại email xác nhận đặt phòng tới {booking.Email}. Quý khách vui lòng kiểm tra hộp thư (kể cả mục Spam).";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Không thể gửi email lúc này: {ex.Message}";
            }

            return RedirectToAction("Confirmation", new { code = booking.BookingCode });
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
                
                try
                {
                    await _emailService.SendCheckOutInvoiceReceiptEmail(invoice);
                }
                catch
                {
                    // Email sending is fire-and-forget for client UX
                }

                TempData["Success"] = $"Check-out thành công. Hóa đơn: {invoice.InvoiceNumber} đã được gửi tới email khách hàng";
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
