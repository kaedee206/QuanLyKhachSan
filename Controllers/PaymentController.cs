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
    /// <summary>
    /// Controller xu ly cac thao tac thanh toán (chinh la SePay)
    /// </summary>
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

        #region ─── Trang chon phuong thuc thanh toan ─────────────────────

        /// <summary>
        /// Hien thi trang chon phuong thuc thanh toan
        /// Tu dong tao hoa don neu chua co
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Index(string code)
        {
            // Tim booking theo code
            var booking = await _db.Bookings
                .Include(b => b.RoomType)
                .Include(b => b.Room)
                .Include(b => b.Invoice)
                .FirstOrDefaultAsync(b => b.BookingCode == code);

            if (booking == null)
                return NotFound("Khong tim thay booking");

            var unpaidStatus = new[] { BookingStatus.Pending, BookingStatus.Confirmed };
            if (!unpaidStatus.Contains(booking.Status))
            {
                TempData["Info"] = $"Booking {code} da duoc thanh toan hoac khong con hieu luc.";
                return RedirectToAction("Confirmation", "Booking", new { code });
            }

            // Neu chua co invoice, tao moi (check DB truoc de tranh duplicate)
            Invoice? invoice = booking.Invoice;
            if (invoice == null)
            {
                invoice = await _db.Invoices.FirstOrDefaultAsync(i => i.BookingId == booking.Id);
            }
            if (invoice == null)
            {
                var userId = User.Identity?.IsAuthenticated == true
                    ? int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!)
                    : 1;

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

                _logger.LogInformation("Invoice auto-created for booking {BookingCode}: {InvoiceNumber}",
                    code, invoiceNumber);
            }

            // Lay thong tin SePay neu co
            var viewModel = new PaymentViewModel
            {
                Booking = booking,
                Invoice = invoice,
                BookingCode = code,
                InvoiceNumber = invoice.InvoiceNumber,
                TotalAmount = invoice.TotalAmount,
                // MoMo chua duoc config -> disable
                IsMoMoEnabled = !string.IsNullOrEmpty(_db.Database.CanConnect().ToString())
            };

            return View(viewModel);
        }

        #endregion

        #region ─── Khoi tao thanh toan SePay ───────────────────────────

        /// <summary>
        /// Khoi tao thanh toan SePay va tra ve URL de redirect
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> InitializeSePay(string invoiceNumber)
        {
            var invoice = await _invoiceService.GetByNumber(invoiceNumber);
            if (invoice == null)
                return NotFound("Khong tim thay hoa don");

            if (invoice.PaymentStatus == PaymentStatus.Paid)
                return Json(new { success = false, message = "Hoa don da duoc thanh toan" });

            // Cap nhat payment method thanh SePay
            invoice.PaymentMethod = PaymentMethod.SePay;
            await _db.SaveChangesAsync();

            // Tao payment voi SePay
            var result = await _sePayService.CreatePayment(invoice);

            if (!result.Success)
            {
                _logger.LogError("SePay CreatePayment failed: {Error}", result.ErrorMessage);
                return Json(new { success = false, message = result.ErrorMessage ?? "Loi khoi tao thanh toan SePay" });
            }

            _logger.LogInformation(
                "SePay payment initialized: Invoice={InvoiceNumber}, OrderId={OrderId}",
                invoiceNumber, result.OrderId);

            return Json(new
            {
                success = true,
                checkoutUrl = result.CheckoutUrl,
                autoRedirect = result.AutoRedirect
            });
        }

        /// <summary>
        /// Chuyen huong truc tiep den trang thanh toan SePay
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> PayWithSePay(string invoiceNumber)
        {
            var invoice = await _invoiceService.GetByNumber(invoiceNumber);
            if (invoice == null)
                return NotFound("Khong tim thay hoa don");

            if (invoice.PaymentStatus == PaymentStatus.Paid)
            {
                TempData["Info"] = "Hoa don da duoc thanh toan";
                return RedirectToAction("Confirmation", "Booking",
                    new { code = invoice.Booking?.BookingCode });
            }

            // Cap nhat payment method thanh SePay
            invoice.PaymentMethod = PaymentMethod.SePay;
            await _db.SaveChangesAsync();

            // Tao payment
            var result = await _sePayService.CreatePayment(invoice);

            if (!result.Success)
            {
                TempData["Error"] = result.ErrorMessage ?? "Loi khoi tao thanh toan SePay";
                return RedirectToAction("Index", new { code = invoice.Booking?.BookingCode });
            }

            // Redirect den trang thanh toan SePay
            return Redirect(result.CheckoutUrl!);
        }

        #endregion

        #region ─── Callback tu SePay ───────────────────────────────────

        /// <summary>
        /// Callback thanh cong tu SePay (redirect ve trinh duyet)
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> SePaySuccess(string order_id, string order_code)
        {
            _logger.LogInformation("SePay success callback: order_id={OrderId}, order_code={OrderCode}",
                order_id, order_code);

            // Lay invoice tu order_code
            var invoice = await _db.Invoices
                .Include(i => i.Booking).ThenInclude(b => b.RoomType)
                .Include(i => i.Booking).ThenInclude(b => b.Room)
                .FirstOrDefaultAsync(i => i.SePayOrderCode == order_code);

            if (invoice == null)
            {
                TempData["Error"] = "Khong tim thay hoa don tuong ung";
                return RedirectToAction("Index", "Home");
            }

            // Neu chua thanh toan, thu xac nhan
            if (invoice.PaymentStatus != PaymentStatus.Paid)
            {
                var isPaid = await _sePayService.CheckOrderStatus(order_code);
                if (isPaid)
                {
                    invoice.PaymentStatus = PaymentStatus.Paid;
                    invoice.PaymentMethod = PaymentMethod.SePay;
                    invoice.PaymentDate = DateTime.UtcNow;
                    invoice.SePayOrderStatus = "CAPTURED";
                    invoice.SePayTransactionId = "";
                    invoice.SePayPaidAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync();

                    // Gui email check-in
                    var booking = invoice.Booking;
                    if (booking != null)
                    {
                        var checkInTime = booking.ActualCheckIn
                            ?? booking.CheckInDate.ToDateTime(TimeOnly.FromTimeSpan(TimeSpan.FromHours(14)));
                        await _emailService.SendCheckInReadyEmail(invoice, checkInTime);
                    }

                    TempData["Success"] = "Thanh toan thanh cong!";
                }
                else
                {
                    TempData["Info"] = "Thanh toan dang duoc xu ly. Vui long doi 1-2 phut de he thong cap nhat.";
                }
            }
            else
            {
                TempData["Success"] = "Thanh toan thanh cong!";
            }

            return RedirectToAction("Confirmation", "Booking",
                new { code = invoice.Booking?.BookingCode });
        }

        /// <summary>
        /// Callback that bai tu SePay
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> SePayError(string order_id, string order_code)
        {
            _logger.LogWarning("SePay error callback: order_id={OrderId}, order_code={OrderCode}",
                order_id, order_code);

            var invoice = await _db.Invoices
                .Include(i => i.Booking)
                .FirstOrDefaultAsync(i => i.SePayOrderCode == order_code);

            if (invoice != null)
            {
                TempData["Error"] = "Thanh toan that bai. Vui long thu lai.";
                return RedirectToAction("Index", new { code = invoice.Booking?.BookingCode });
            }

            TempData["Error"] = "Thanh toan that bai. Vui long thu lai.";
            return RedirectToAction("Index", "Home");
        }

        /// <summary>
        /// Callback huy tu SePay
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> SePayCancel(string order_id, string order_code)
        {
            _logger.LogInformation("SePay cancel callback: order_id={OrderId}, order_code={OrderCode}",
                order_id, order_code);

            var invoice = await _db.Invoices
                .Include(i => i.Booking)
                .FirstOrDefaultAsync(i => i.SePayOrderCode == order_code);

            if (invoice != null)
            {
                TempData["Info"] = "Ban da huy thanh toan. Ban co the thanh toan lai bat cu luc nao.";
                return RedirectToAction("Index", new { code = invoice.Booking?.BookingCode });
            }

            TempData["Info"] = "Ban da huy thanh toan.";
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
