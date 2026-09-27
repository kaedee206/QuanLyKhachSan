using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuanLyKhachSan.Data;
using QuanLyKhachSan.Models.Entities;
using QuanLyKhachSan.Models.Enums;
using QuanLyKhachSan.Models.ViewModels;
using QuanLyKhachSan.Services;
using System.Security.Claims;

namespace QuanLyKhachSan.Controllers
{
    public class PaymentController : Controller
    {
        private readonly InvoiceService _invoiceService;
        private readonly SePayService _sePayService;
        private readonly EmailSftpService _emailService;
        private readonly SunHotelDbContext _db;
        private readonly ILogger<PaymentController> _logger;

        public PaymentController(
            InvoiceService invoiceService,
            SePayService sePayService,
            EmailSftpService emailService,
            SunHotelDbContext db,
            ILogger<PaymentController> logger)
        {
            _invoiceService = invoiceService;
            _sePayService = sePayService;
            _emailService = emailService;
            _db = db;
            _logger = logger;
        }

        #region ─── Trang chọn phương thức thanh toán ─────────────────────

        [HttpGet]
        public async Task<IActionResult> Index(string code)
        {
            var booking = await _db.Bookings
                .Include(b => b.RoomType)
                .Include(b => b.Room)
                .Include(b => b.Invoice)
                .FirstOrDefaultAsync(b => b.BookingCode == code);

            if (booking == null)
                return NotFound("Không tìm thấy booking");

            var unpaidStatus = new[] { BookingStatus.Pending, BookingStatus.Confirmed };
            if (!unpaidStatus.Contains(booking.Status))
            {
                TempData["Info"] = $"Booking {code} đã được thanh toán hoặc không còn hiệu lực.";
                return RedirectToAction("Confirmation", "Booking", new { code });
            }

            Invoice? invoice = booking.Invoice;
            if (invoice == null)
            {
                invoice = await _db.Invoices.FirstOrDefaultAsync(i => i.BookingId == booking.Id);
            }

            // Chặn thanh toán lại nếu hóa đơn đã được thanh toán
            if (invoice != null && invoice.PaymentStatus == PaymentStatus.Paid)
            {
                TempData["Success"] = "Booking này đã được thanh toán thành công.";
                return RedirectToAction("Confirmation", "Booking", new { code });
            }

            if (invoice == null)
            {
                int? userId = (User.Identity?.IsAuthenticated == true && int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsedId))
                    ? parsedId
                    : null;

                var servicesTotal = await _db.Services
                    .Where(s => s.BookingId == booking.Id)
                    .SumAsync(s => s.TotalAmount);

                var invoiceNumber = await GenerateInvoiceNumber();

                invoice = new Invoice
                {
                    BookingId = booking.Id,
                    InvoiceNumber = invoiceNumber,
                    RoomCharge = booking.TotalAmount,
                    ServiceCharge = servicesTotal,
                    TotalAmount = booking.TotalAmount + servicesTotal,
                    PaymentMethod = PaymentMethod.SePay,
                    PaymentStatus = PaymentStatus.Unpaid,
                    CreatedById = userId
                };

                _db.Invoices.Add(invoice);
                await _db.SaveChangesAsync();

                _logger.LogInformation("Hóa đơn tự động tạo cho booking {BookingCode}: {InvoiceNumber}",
                    code, invoiceNumber);
            }

            var viewModel = new PaymentViewModel
            {
                Booking = booking,
                Invoice = invoice,
                BookingCode = code,
                InvoiceNumber = invoice.InvoiceNumber,
                TotalAmount = invoice.TotalAmount,
                IsMoMoEnabled = !string.IsNullOrEmpty(_db.Database.CanConnect().ToString())
            };

            return View(viewModel);
        }

        #endregion

        #region ─── Thanh toán nhanh (trực tiếp từ Confirmation) ───────────

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> QuickPay(string bookingCode)
        {
            var booking = await _db.Bookings
                .Include(b => b.Invoice)
                .FirstOrDefaultAsync(b => b.BookingCode == bookingCode);

            if (booking == null)
                return Json(new { success = false, message = "Không tìm thấy booking" });

            // Lấy hoặc tạo hóa đơn
            var invoice = booking.Invoice
                ?? await _db.Invoices.FirstOrDefaultAsync(i => i.BookingId == booking.Id);

            if (invoice == null)
            {
                int? userId = (User.Identity?.IsAuthenticated == true && int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsedId))
                    ? parsedId
                    : null;

                var servicesTotal = await _db.Services
                    .Where(s => s.BookingId == booking.Id)
                    .SumAsync(s => s.TotalAmount);

                var invoiceNumber = await GenerateInvoiceNumber();
                invoice = new Invoice
                {
                    BookingId = booking.Id,
                    InvoiceNumber = invoiceNumber,
                    RoomCharge = booking.TotalAmount,
                    ServiceCharge = servicesTotal,
                    TotalAmount = booking.TotalAmount + servicesTotal,
                    PaymentMethod = PaymentMethod.SePay,
                    PaymentStatus = PaymentStatus.Unpaid,
                    CreatedById = userId
                };
                _db.Invoices.Add(invoice);
                await _db.SaveChangesAsync();
            }

            if (invoice.PaymentStatus == PaymentStatus.Paid)
                return Json(new { success = false, message = "Booking này đã được thanh toán." });

            // Load navigation Booking nếu chưa có (cần cho CreatePayment)
            if (invoice.Booking == null)
                await _db.Entry(invoice).Reference(i => i.Booking).LoadAsync();

            invoice.PaymentMethod = PaymentMethod.SePay;
            await _db.SaveChangesAsync();

            var result = await _sePayService.CreatePayment(invoice);
            if (!result.Success)
            {
                _logger.LogError("SePay QuickPay thất bại cho {BookingCode}: {Error}", bookingCode, result.ErrorMessage);
                return Json(new { success = false, message = result.ErrorMessage ?? "Lỗi khởi tạo thanh toán" });
            }

            return Json(new { success = true, checkoutUrl = result.CheckoutUrl });
        }

        /// <summary>Polling endpoint — client gọi để kiểm tra hóa đơn đã được thanh toán chưa</summary>
        [HttpGet]
        public async Task<IActionResult> CheckPaymentStatus(string bookingCode)
        {
            var invoice = await _db.Invoices
                .Include(i => i.Booking)
                .FirstOrDefaultAsync(i => i.Booking != null && i.Booking.BookingCode == bookingCode);

            if (invoice == null)
                return Json(new { paid = false });

            return Json(new { paid = invoice.PaymentStatus == PaymentStatus.Paid });
        }

        #endregion

        #region ─── Khởi tạo thanh toán SePay ───────────────────────────

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> InitializeSePay(string invoiceNumber)
        {
            var invoice = await _invoiceService.GetByNumber(invoiceNumber);
            if (invoice == null)
                return NotFound("Không tìm thấy hóa đơn");

            if (invoice.PaymentStatus == PaymentStatus.Paid)
                return Json(new { success = false, message = "Hóa đơn đã được thanh toán" });

            invoice.PaymentMethod = PaymentMethod.SePay;
            await _db.SaveChangesAsync();

            var result = await _sePayService.CreatePayment(invoice);

            if (!result.Success)
            {
                _logger.LogError("SePay CreatePayment thất bại: {Error}", result.ErrorMessage);
                return Json(new { success = false, message = result.ErrorMessage ?? "Lỗi khởi tạo thanh toán SePay" });
            }

            _logger.LogInformation(
                "Thanh toán SePay đã khởi tạo: Invoice={InvoiceNumber}, OrderId={OrderId}",
                invoiceNumber, result.OrderId);

            return Json(new
            {
                success = true,
                checkoutUrl = result.CheckoutUrl,
                autoRedirect = result.AutoRedirect
            });
        }

        [HttpGet]
        public async Task<IActionResult> PayWithSePay(string invoiceNumber)
        {
            var invoice = await _invoiceService.GetByNumber(invoiceNumber);
            if (invoice == null)
                return NotFound("Không tìm thấy hóa đơn");

            if (invoice.PaymentStatus == PaymentStatus.Paid)
            {
                TempData["Info"] = "Hóa đơn đã được thanh toán";
                return RedirectToAction("Confirmation", "Booking",
                    new { code = invoice.Booking?.BookingCode });
            }

            invoice.PaymentMethod = PaymentMethod.SePay;
            await _db.SaveChangesAsync();

            var result = await _sePayService.CreatePayment(invoice);

            if (!result.Success)
            {
                TempData["Error"] = result.ErrorMessage ?? "Lỗi khởi tạo thanh toán SePay";
                return RedirectToAction("Index", new { code = invoice.Booking?.BookingCode });
            }

            return Redirect(result.CheckoutUrl!);
        }

        #endregion

        #region ─── Callback từ SePay ───────────────────────────────────

        [HttpGet]
        public async Task<IActionResult> SePaySuccess(string order_id, string order_code, string? inv)
        {
            _logger.LogInformation("SePay callback thành công: order_id={OrderId}, order_code={OrderCode}, inv={Inv}",
                order_id, order_code, inv);

            Invoice? invoice = null;

            // Ưu tiên lookup bằng invoiceNumber nhúng trong callback URL
            if (!string.IsNullOrEmpty(inv))
            {
                invoice = await _db.Invoices
                    .Include(i => i.Booking).ThenInclude(b => b.RoomType)
                    .Include(i => i.Booking).ThenInclude(b => b.Room)
                    .FirstOrDefaultAsync(i => i.InvoiceNumber == inv);
            }

            // Fallback: lookup bằng order_code hoặc order_id
            if (invoice == null)
            {
                invoice = await _db.Invoices
                    .Include(i => i.Booking).ThenInclude(b => b.RoomType)
                    .Include(i => i.Booking).ThenInclude(b => b.Room)
                    .FirstOrDefaultAsync(i =>
                        (!string.IsNullOrEmpty(order_code) && i.SePayOrderCode == order_code)
                        || (!string.IsNullOrEmpty(order_id) && i.SePayOrderId == order_id));
            }

            if (invoice == null)
            {
                TempData["Error"] = "Không tìm thấy hóa đơn tương ứng";
                return RedirectToAction("Index", "Home");
            }

            if (invoice.PaymentStatus != PaymentStatus.Paid)
            {
                // Dùng order_code nếu có, fallback sang order_id (SePay production dùng order_id)
                var codeToCheck = !string.IsNullOrEmpty(order_code) ? order_code
                                : !string.IsNullOrEmpty(order_id) ? order_id
                                : invoice.SePayOrderCode ?? invoice.SePayOrderId ?? "";

                var isPaid = await _sePayService.CheckOrderStatus(codeToCheck);
                if (isPaid)
                {
                    invoice.PaymentStatus = PaymentStatus.Paid;
                    invoice.PaymentMethod = PaymentMethod.SePay;
                    invoice.PaymentDate = DateTime.UtcNow;
                    invoice.SePayOrderStatus = "CAPTURED";
                    invoice.SePayTransactionId = order_id ?? "";
                    invoice.SePayPaidAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync();

                    var booking = invoice.Booking;
                    if (booking != null)
                    {
                        var checkInTime = booking.ActualCheckIn
                            ?? booking.CheckInDate.ToDateTime(TimeOnly.FromTimeSpan(TimeSpan.FromHours(14)));
                        await _emailService.SendCheckInReadyEmail(invoice, checkInTime);
                    }

                    TempData["Success"] = "Thanh toán thành công!";
                }
                else
                {
                    // CheckOrderStatus thất bại không có nghĩa là chưa thanh toán —
                    // IPN webhook sẽ xác nhận sau. Vẫn redirect về Confirmation để hiển thị đúng trạng thái.
                    _logger.LogWarning(
                        "SePaySuccess: CheckOrderStatus trả về false cho inv={Inv}, order_id={OrderId}, order_code={OrderCode}. Chờ IPN.",
                        inv, order_id, order_code);
                    TempData["Info"] = "Thanh toán đang được xử lý. Trang sẽ tự cập nhật trong vài phút.";
                }
            }
            else
            {
                TempData["Success"] = "Thanh toán thành công!";
            }

            return RedirectToAction("Confirmation", "Booking",
                new { code = invoice.Booking?.BookingCode });
        }

        [HttpGet]
        public async Task<IActionResult> SePayError(string order_id, string order_code, string? inv)
        {
            _logger.LogWarning("SePay callback lỗi: order_id={OrderId}, order_code={OrderCode}, inv={Inv}",
                order_id, order_code, inv);

            Invoice? invoice = null;
            if (!string.IsNullOrEmpty(inv))
                invoice = await _db.Invoices.Include(i => i.Booking)
                    .FirstOrDefaultAsync(i => i.InvoiceNumber == inv);

            if (invoice == null)
                invoice = await _db.Invoices
                    .Include(i => i.Booking)
                    .FirstOrDefaultAsync(i => i.SePayOrderCode == order_code
                        || (!string.IsNullOrEmpty(order_id) && i.SePayOrderId == order_id));

            if (invoice != null)
            {
                TempData["Error"] = "Thanh toán thất bại. Vui lòng thử lại.";
                return RedirectToAction("Index", new { code = invoice.Booking?.BookingCode });
            }

            TempData["Error"] = "Thanh toán thất bại. Vui lòng thử lại.";
            return RedirectToAction("Index", "Home");
        }

        [HttpGet]
        public async Task<IActionResult> SePayCancel(string order_id, string order_code, string? inv)
        {
            _logger.LogInformation("SePay callback hủy: order_id={OrderId}, order_code={OrderCode}, inv={Inv}",
                order_id, order_code, inv);

            Invoice? invoice = null;
            if (!string.IsNullOrEmpty(inv))
                invoice = await _db.Invoices.Include(i => i.Booking)
                    .FirstOrDefaultAsync(i => i.InvoiceNumber == inv);

            if (invoice == null)
                invoice = await _db.Invoices
                    .Include(i => i.Booking)
                    .FirstOrDefaultAsync(i => i.SePayOrderCode == order_code
                        || (!string.IsNullOrEmpty(order_id) && i.SePayOrderId == order_id));

            if (invoice != null)
            {
                TempData["Info"] = "Bạn đã hủy thanh toán. Bạn có thể thanh toán lại bất cứ lúc nào.";
                return RedirectToAction("Index", new { code = invoice.Booking?.BookingCode });
            }

            TempData["Info"] = "Bạn đã hủy thanh toán.";
            return RedirectToAction("Index", "Home");
        }

        #endregion

        #region ─── Helpers ─────────────────────────────────────────────

        private async Task<string> GenerateInvoiceNumber()
        {
            string number;
            do
            {
                number = "INV-" + DateTime.UtcNow.ToString("yyyyMMdd") + "-" +
                         Random.Shared.Next(1000, 9999).ToString();
            } while (await _db.Invoices.AnyAsync(i => i.InvoiceNumber == number));
            return number;
        }

        #endregion
    }
}
