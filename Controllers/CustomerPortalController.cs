using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuanLyKhachSan.Data;
using QuanLyKhachSan.Models.Entities;
using QuanLyKhachSan.Models.Enums;
using QuanLyKhachSan.Models.ViewModels;
using QuanLyKhachSan.Services;

namespace QuanLyKhachSan.Controllers
{
    [Route("Customer")]
    public class CustomerPortalController : Controller
    {
        private readonly SunHotelDbContext _db;
        private readonly EmailSftpService _emailService;
        private readonly EmailRateLimiter _rateLimiter;
        private readonly ILogger<CustomerPortalController> _logger;

        // In-memory thread-safe store for OTP tokens (10 minute expiry)
        private static readonly ConcurrentDictionary<string, (int BookingId, string Otp, DateTime ExpiresAt, string Email)> _otpStore = new();

        public CustomerPortalController(
            SunHotelDbContext db,
            EmailSftpService emailService,
            ILogger<CustomerPortalController> logger)
        {
            _db = db;
            _emailService = emailService;
            _logger = logger;
            _rateLimiter = EmailRateLimiter.Instance;
        }

        // ════════════════════════════════════════════════════════════════════
        // 1. LOGIN & 4-FIELD VERIFICATION
        // ════════════════════════════════════════════════════════════════════
        [HttpGet("Login")]
        public IActionResult Login()
        {
            if (GetAuthenticatedBookingId().HasValue)
            {
                return RedirectToAction("Dashboard");
            }
            return View("~/Views/CustomerPortal/Login.cshtml", new CustomerLoginViewModel());
        }

        [HttpPost("RequestOtp")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RequestOtp(CustomerLoginViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View("~/Views/CustomerPortal/Login.cshtml", model);
            }

            var cleanCode = (model.BookingOrInvoiceCode ?? "").Trim().ToUpper();
            var cleanId = (model.IdNumber ?? "").Trim();
            var cleanEmail = (model.Email ?? "").Trim().ToLower();
            var cleanPhone = (model.Phone ?? "").Trim().Replace(" ", "").Replace(".", "").Replace("-", "");

            // Tìm kiếm booking theo mã booking hoặc mã hóa đơn
            var booking = await _db.Bookings
                .Include(b => b.Room)
                .Include(b => b.Invoice)
                .FirstOrDefaultAsync(b =>
                    b.BookingCode.ToUpper() == cleanCode ||
                    (b.Invoice != null && b.Invoice.InvoiceNumber.ToUpper() == cleanCode));

            if (booking == null)
            {
                ModelState.AddModelError("", "Không tìm thấy hồ sơ đặt phòng hoặc hóa đơn phù hợp với mã đã nhập.");
                return View("~/Views/CustomerPortal/Login.cshtml", model);
            }

            // Kiểm tra 3 trường còn lại: CCCD, Email, SĐT
            var dbId = (booking.IdNumber ?? "").Trim();
            var dbEmail = (booking.Email ?? "").Trim().ToLower();
            var dbPhone = (booking.Phone ?? "").Trim().Replace(" ", "").Replace(".", "").Replace("-", "");

            bool idMatch = string.Equals(dbId, cleanId, StringComparison.OrdinalIgnoreCase);
            bool emailMatch = string.Equals(dbEmail, cleanEmail, StringComparison.OrdinalIgnoreCase);
            bool phoneMatch = string.Equals(dbPhone, cleanPhone, StringComparison.OrdinalIgnoreCase);

            if (!idMatch || !emailMatch || !phoneMatch)
            {
                ModelState.AddModelError("", "Thông tin CCCD, Email hoặc Số điện thoại không trùng khớp với dữ liệu đăng ký phòng.");
                return View("~/Views/CustomerPortal/Login.cshtml", model);
            }

            var guestEmail = booking.Email ?? "";

            // Kiểm tra Rate-Limit trước khi sinh mã và gửi email (chống spam/bombing)
            var rateCheck = _rateLimiter.CheckAndConsume($"customer_otp:{guestEmail}", cooldownSeconds: 60, maxInWindow: 5, windowMinutes: 15);
            if (!rateCheck.IsAllowed)
            {
                ModelState.AddModelError("", rateCheck.ErrorMessage);
                return View("~/Views/CustomerPortal/Login.cshtml", model);
            }

            // Sinh mã OTP 6 chữ số ngẫu nhiên
            var otp = Random.Shared.Next(100000, 999999).ToString();
            var token = Guid.NewGuid().ToString("N");

            // Lưu vào store với thời hạn 10 phút
            _otpStore[token] = (booking.Id, otp, DateTime.UtcNow.AddMinutes(10), guestEmail);

            // Gửi email OTP
            try
            {
                await _emailService.SendCustomerPortalOtpEmail(guestEmail, booking.GuestName, otp);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi gửi email OTP cho khách hàng {Email}", guestEmail);
            }

            TempData["Success"] = "Mã xác thực OTP gồm 6 chữ số đã được gửi tới email của Quý khách. Vui lòng kiểm tra hộp thư!";
            return RedirectToAction("VerifyOtp", new { token });
        }

        // ════════════════════════════════════════════════════════════════════
        // 2. VERIFY OTP
        // ════════════════════════════════════════════════════════════════════
        [HttpGet("VerifyOtp")]
        public IActionResult VerifyOtp(string token)
        {
            if (string.IsNullOrEmpty(token) || !_otpStore.TryGetValue(token, out var otpData))
            {
                TempData["Error"] = "Phiên xác thực đã hết hạn hoặc không hợp lệ. Vui lòng đăng nhập lại.";
                return RedirectToAction("Login");
            }

            if (DateTime.UtcNow > otpData.ExpiresAt)
            {
                _otpStore.TryRemove(token, out _);
                TempData["Error"] = "Mã OTP đã hết thời hạn hiệu lực (10 phút). Vui lòng yêu cầu mã mới.";
                return RedirectToAction("Login");
            }

            var maskedEmail = MaskEmail(otpData.Email);
            var remainingCooldown = _rateLimiter.GetRemainingCooldown($"customer_otp:{otpData.Email}", 60);

            var model = new VerifyCustomerOtpViewModel
            {
                Token = token,
                MaskedEmail = maskedEmail,
                ExpiresInSeconds = (int)Math.Max(0, (otpData.ExpiresAt - DateTime.UtcNow).TotalSeconds),
                ResendCooldownSeconds = remainingCooldown
            };

            return View("~/Views/CustomerPortal/VerifyOtp.cshtml", model);
        }

        // ════════════════════════════════════════════════════════════════════
        // 2b. RESEND OTP (VỚI BẢO VỆ RATE-LIMIT 2 LỚP)
        // ════════════════════════════════════════════════════════════════════
        [HttpPost("ResendOtp")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResendOtp(string token)
        {
            if (string.IsNullOrEmpty(token) || !_otpStore.TryGetValue(token, out var otpData))
            {
                TempData["Error"] = "Phiên xác thực không hợp lệ hoặc đã hết hạn. Vui lòng đăng nhập lại.";
                return RedirectToAction("Login");
            }

            if (DateTime.UtcNow > otpData.ExpiresAt)
            {
                _otpStore.TryRemove(token, out _);
                TempData["Error"] = "Phiên xác thực đã hết thời hạn hiệu lực (10 phút). Vui lòng đăng nhập lại.";
                return RedirectToAction("Login");
            }

            // Kiểm tra Rate-Limit nghiêm ngặt: 60s cooldown giữa 2 lần bấm, tối đa 5 lần trong 15 phút
            var rateResult = _rateLimiter.CheckAndConsume($"customer_otp:{otpData.Email}", cooldownSeconds: 60, maxInWindow: 5, windowMinutes: 15);
            if (!rateResult.IsAllowed)
            {
                TempData["Error"] = rateResult.ErrorMessage;
                return RedirectToAction("VerifyOtp", new { token });
            }

            // Sinh mã OTP 6 chữ số mới và làm mới hạn sử dụng 10 phút
            var newOtp = Random.Shared.Next(100000, 999999).ToString();
            var newExpiresAt = DateTime.UtcNow.AddMinutes(10);
            _otpStore[token] = (otpData.BookingId, newOtp, newExpiresAt, otpData.Email);

            var booking = await _db.Bookings.FindAsync(otpData.BookingId);
            var guestName = booking?.GuestName ?? "Quý khách";

            try
            {
                await _emailService.SendCustomerPortalOtpEmail(otpData.Email, guestName, newOtp);
                TempData["Success"] = $"Mã OTP mới đã được gửi thành công tới email {MaskEmail(otpData.Email)}. Vui lòng kiểm tra cả hòm thư chính và mục Spam/Quảng cáo!";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi gửi lại email OTP cho khách hàng {Email}", otpData.Email);
                TempData["Error"] = "Hệ thống gặp sự cố tạm thời khi gửi email. Vui lòng liên hệ Lễ tân qua hotline 0901 234 567 để nhận hỗ trợ nhanh.";
            }

            return RedirectToAction("VerifyOtp", new { token });
        }

        [HttpPost("VerifyOtp")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyOtp(VerifyCustomerOtpViewModel model)
        {
            if (string.IsNullOrEmpty(model.Token) || !_otpStore.TryGetValue(model.Token, out var otpData))
            {
                ModelState.AddModelError("", "Phiên xác thực đã hết hạn. Vui lòng đăng nhập lại.");
                return View("~/Views/CustomerPortal/VerifyOtp.cshtml", model);
            }

            if (DateTime.UtcNow > otpData.ExpiresAt)
            {
                _otpStore.TryRemove(model.Token, out _);
                ModelState.AddModelError("", "Mã OTP đã hết hạn 10 phút. Vui lòng quay lại đăng nhập.");
                return View("~/Views/CustomerPortal/VerifyOtp.cshtml", model);
            }

            if (model.OtpCode.Trim() != otpData.Otp)
            {
                ModelState.AddModelError("OtpCode", "Mã xác thực OTP không chính xác. Quý khách vui lòng kiểm tra lại email.");
                model.MaskedEmail = MaskEmail(otpData.Email);
                model.ExpiresInSeconds = (int)Math.Max(0, (otpData.ExpiresAt - DateTime.UtcNow).TotalSeconds);
                return View("~/Views/CustomerPortal/VerifyOtp.cshtml", model);
            }

            // OTP chính xác! Lấy thông tin booking và tạo phiên đăng nhập
            var booking = await _db.Bookings
                .Include(b => b.Room)
                .FirstOrDefaultAsync(b => b.Id == otpData.BookingId);

            if (booking == null)
            {
                TempData["Error"] = "Không tìm thấy hồ sơ phòng tương ứng.";
                return RedirectToAction("Login");
            }

            // Xóa OTP đã sử dụng
            _otpStore.TryRemove(model.Token, out _);

            // Thiết lập Cookie Claims và Session
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, booking.Id.ToString()),
                new Claim(ClaimTypes.Name, booking.GuestName),
                new Claim(ClaimTypes.Email, booking.Email ?? ""),
                new Claim(ClaimTypes.Role, "Customer"),
                new Claim("BookingCode", booking.BookingCode ?? ""),
                new Claim("RoomNumber", booking.Room?.RoomNumber ?? "")
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
            HttpContext.Session.SetInt32("CustomerBookingId", booking.Id);

            TempData["Success"] = $"Xác thực thành công! Chào mừng Quý khách {booking.GuestName} đến với Sun Hotel Concierge.";
            return RedirectToAction("Dashboard");
        }

        // ════════════════════════════════════════════════════════════════════
        // 3. CUSTOMER DASHBOARD
        // ════════════════════════════════════════════════════════════════════
        [HttpGet("Dashboard")]
        public async Task<IActionResult> Dashboard()
        {
            var bookingId = GetAuthenticatedBookingId();
            if (!bookingId.HasValue)
            {
                return RedirectToAction("Login");
            }

            var booking = await _db.Bookings
                .Include(b => b.Room)
                .Include(b => b.RoomType)
                .Include(b => b.Invoice)
                .Include(b => b.Services)
                .FirstOrDefaultAsync(b => b.Id == bookingId.Value);

            if (booking == null)
            {
                await HttpContext.SignOutAsync();
                HttpContext.Session.Remove("CustomerBookingId");
                return RedirectToAction("Login");
            }

            var invoice = booking.Invoice;
            bool isRoomPaid = invoice?.PaymentStatus == PaymentStatus.Paid;
            decimal roomCharge = invoice?.RoomCharge ?? booking.TotalAmount;
            decimal extraServicesTotal = booking.Services?.Sum(s => s.TotalAmount) ?? 0;
            // Tổng tiền hiện tại khách đang chi tiêu (không tính tiền check-in/tiền phòng nếu đã trả)
            decimal currentTotal = (isRoomPaid ? 0 : roomCharge) + extraServicesTotal;

            // Lấy danh sách ticket yêu cầu của phòng này
            var tickets = await _db.Tickets
                .Include(t => t.Assignee)
                .Where(t => t.RoomId == booking.RoomId)
                .OrderByDescending(t => t.CreatedAt)
                .Take(20)
                .ToListAsync();

            var viewModel = new CustomerDashboardViewModel
            {
                Booking = booking,
                Invoice = invoice,
                RoomCharge = roomCharge,
                IsRoomPaid = isRoomPaid,
                ExtraServicesTotal = extraServicesTotal,
                CurrentTotalAmount = currentTotal,
                ActiveServices = booking.Services?.OrderByDescending(s => s.CreatedAt).ToList() ?? new List<Service>(),
                ActiveTickets = tickets,
                MenuItems = HotelMenuCatalog.Items
            };

            return View("~/Views/CustomerPortal/Dashboard.cshtml", viewModel);
        }

        // ════════════════════════════════════════════════════════════════════
        // 4. ORDER FOOD & BEVERAGE
        // ════════════════════════════════════════════════════════════════════
        [HttpPost("OrderFood")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> OrderFood(CustomerOrderFoodViewModel model)
        {
            var bookingId = GetAuthenticatedBookingId();
            if (!bookingId.HasValue) return RedirectToAction("Login");

            var booking = await _db.Bookings
                .Include(b => b.Room)
                .Include(b => b.Invoice)
                .FirstOrDefaultAsync(b => b.Id == bookingId.Value);

            if (booking == null) return RedirectToAction("Login");

            var menuItem = HotelMenuCatalog.Items.FirstOrDefault(m => m.Id == model.MenuItemId);
            if (menuItem == null)
            {
                TempData["Error"] = "Món ăn hoặc đồ uống không tồn tại trong thực đơn.";
                return RedirectToAction("Dashboard");
            }

            var qty = Math.Max(1, Math.Min(20, model.Quantity));
            var totalAmount = menuItem.Price * qty;

            // 1. Tạo bản ghi Service gắn vào Booking
            var service = new Service
            {
                BookingId = booking.Id,
                ServiceName = menuItem.Name,
                ServiceType = menuItem.ServiceType,
                Quantity = qty,
                UnitPrice = menuItem.Price,
                TotalAmount = totalAmount,
                Notes = string.IsNullOrWhiteSpace(model.Notes)
                    ? $"[Khách tự đặt từ App] Giao lên phòng {booking.Room?.RoomNumber}"
                    : $"[Khách tự đặt] {model.Notes.Trim()} (Giao P.{booking.Room?.RoomNumber})",
                CreatedAt = DateTime.UtcNow
            };
            _db.Services.Add(service);

            // 2. Cập nhật hóa đơn nếu có
            if (booking.Invoice != null)
            {
                booking.Invoice.ServiceCharge += totalAmount;
                booking.Invoice.TotalAmount += totalAmount;
                booking.Invoice.UpdatedAt = DateTime.UtcNow;
            }

            // 3. Tự động tạo Ticket phân công cho Bếp hoặc Bar
            var targetDept = menuItem.Department == "Bar" ? TicketType.Bar : TicketType.Kitchen;
            var ticketTypeTitle = menuItem.Department == "Bar" ? "Quầy Bar" : "Bếp Trưởng";

            // Tìm nhân viên mặc định hệ thống để gán ReportedBy
            var systemUser = await _db.Users.FirstOrDefaultAsync(u => u.Role == UserRole.Admin) 
                             ?? await _db.Users.FirstAsync();

            var todayStr = DateTime.UtcNow.ToString("yyyyMMdd");
            var ticketCount = await _db.Tickets.CountAsync() + 1;
            var ticketNumber = $"ORD-{todayStr}-{ticketCount:D3}";

            var ticket = new Ticket
            {
                TicketNumber = ticketNumber,
                RoomId = booking.RoomId ?? 1,
                Type = targetDept,
                Description = $"[YÊU CẦU MÓN ĂN/UỐNG - P.{booking.Room?.RoomNumber}] {menuItem.Name} (SL: {qty}) - Đơn giá: {menuItem.Price:N0}đ. Ghi chú: {service.Notes}",
                Priority = TicketPriority.High,
                Status = TicketStatus.Open,
                ReportedById = systemUser.Id,
                CreatedAt = DateTime.UtcNow
            };
            _db.Tickets.Add(ticket);

            await _db.SaveChangesAsync();

            TempData["Success"] = $"🎉 Đã gửi yêu cầu phục vụ {qty}x \"{menuItem.Name}\" thành công! {ticketTypeTitle} đang chuẩn bị và sẽ giao tận phòng {booking.Room?.RoomNumber} trong ít phút.";
            return RedirectToAction("Dashboard");
        }

        // ════════════════════════════════════════════════════════════════════
        // 5. CREATE CUSTOMER TICKET / SERVICE REQUEST
        // ════════════════════════════════════════════════════════════════════
        [HttpPost("CreateTicket")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateTicket(CustomerCreateTicketViewModel model)
        {
            var bookingId = GetAuthenticatedBookingId();
            if (!bookingId.HasValue) return RedirectToAction("Login");

            var booking = await _db.Bookings
                .Include(b => b.Room)
                .FirstOrDefaultAsync(b => b.Id == bookingId.Value);

            if (booking == null) return RedirectToAction("Login");

            if (!ModelState.IsValid)
            {
                TempData["Error"] = "Vui lòng nhập đầy đủ nội dung yêu cầu.";
                return RedirectToAction("Dashboard");
            }

            var systemUser = await _db.Users.FirstOrDefaultAsync(u => u.Role == UserRole.Admin)
                             ?? await _db.Users.FirstAsync();

            var todayStr = DateTime.UtcNow.ToString("yyyyMMdd");
            var ticketCount = await _db.Tickets.CountAsync() + 1;
            var ticketNumber = $"REQ-{todayStr}-{ticketCount:D3}";

            var deptName = model.Type switch
            {
                TicketType.Maintenance => "Bộ phận Kỹ thuật & Bảo trì",
                TicketType.Housekeeping => "Bộ phận Buồng phòng",
                TicketType.Kitchen => "Bộ phận Bếp núc & F&B",
                TicketType.Bar => "Bộ phận Quầy Bar",
                _ => "Bộ phận Lễ tân & Chăm sóc khách hàng"
            };

            var ticket = new Ticket
            {
                TicketNumber = ticketNumber,
                RoomId = booking.RoomId ?? 1,
                Type = model.Type,
                Description = $"[Yêu cầu từ Quý khách {booking.GuestName} - Phòng {booking.Room?.RoomNumber}]: {model.Description.Trim()}",
                Priority = model.Priority,
                Status = TicketStatus.Open,
                ReportedById = systemUser.Id,
                CreatedAt = DateTime.UtcNow
            };

            _db.Tickets.Add(ticket);
            await _db.SaveChangesAsync();

            TempData["Success"] = $"Yêu cầu hỗ trợ #{ticketNumber} đã được chuyển tới {deptName}. Nhân viên phụ trách sẽ xử lý ngay!";
            return RedirectToAction("Dashboard");
        }

        // ════════════════════════════════════════════════════════════════════
        // 6. LOGOUT
        // ════════════════════════════════════════════════════════════════════
        [HttpPost("Logout")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            HttpContext.Session.Remove("CustomerBookingId");
            TempData["Success"] = "Quý khách đã đăng xuất khỏi cổng thông tin Sun Hotel.";
            return RedirectToAction("Login");
        }

        // ════════════════════════════════════════════════════════════════════
        // HELPERS
        // ════════════════════════════════════════════════════════════════════
        private int? GetAuthenticatedBookingId()
        {
            var sessionVal = HttpContext.Session.GetInt32("CustomerBookingId");
            if (sessionVal.HasValue) return sessionVal.Value;

            var claimVal = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrEmpty(claimVal) && int.TryParse(claimVal, out var id) && User.IsInRole("Customer"))
            {
                return id;
            }

            return null;
        }

        private static string MaskEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email) || !email.Contains('@')) return email;
            var parts = email.Split('@');
            var name = parts[0];
            var domain = parts[1];
            if (name.Length <= 2) return $"{name[0]}*@{domain}";
            return $"{name[0]}***{name[^1]}@{domain}";
        }
    }
}
