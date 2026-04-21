using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuanLyKhachSan.Models.Entities;
using QuanLyKhachSan.Models.Enums;
using QuanLyKhachSan.Models.ViewModels;
using QuanLyKhachSan.Services;
using System.Linq;
using System.Security.Claims;

namespace QuanLyKhachSan.Controllers
{
    [Authorize(Roles = "Admin,Manager,Receptionist")]
    public class InvoiceController : Controller
    {
        private readonly InvoiceService _invoiceService;
        private readonly MoMoService _moMoService;
        private readonly EmailSftpService _emailService;
        private readonly ILogger<InvoiceController> _logger;

        public InvoiceController(
            InvoiceService invoiceService,
            MoMoService moMoService,
            EmailSftpService emailService,
            ILogger<InvoiceController> logger)
        {
            _invoiceService = invoiceService;
            _moMoService = moMoService;
            _emailService = emailService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Index(int page = 1)
        {
            var invoices = await _invoiceService.GetInvoices(page);
            return View(invoices);
        }

        [HttpGet]
        [Route("Detail/{id}")]
        public async Task<IActionResult> Detail(string id)
        {
            var invoice = await _invoiceService.GetByNumber(id);
            if (invoice == null) return NotFound();
            return View(invoice);
        }

        [HttpGet]
        [Route("Print/{id}")]
        public async Task<IActionResult> Print(string id)
        {
            var invoice = await _invoiceService.GetByNumber(id);
            if (invoice == null) return NotFound();
            return View("~/Views/Invoice/_InvoicePrint.cshtml", invoice);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ManualMarkPaid(string id)
        {
            try
            {
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
                var invoice = await _invoiceService.ConfirmPayment(id, PaymentMethod.Transfer, userId);

                var booking = invoice.Booking;
                if (booking != null)
                {
                    var checkInTime = booking.ActualCheckIn ?? booking.CheckInDate.ToDateTime(TimeOnly.MinValue);
                    await _emailService.SendCheckInReadyEmail(invoice, checkInTime);
                }

                TempData["Success"] = "Đã đánh dấu hóa đơn là đã thanh toán";
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
            }
            return RedirectToAction("Detail", new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmPayment(string id, ConfirmPaymentViewModel model)
        {
            try
            {
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
                var invoice = await _invoiceService.ConfirmPayment(id, model.PaymentMethod, userId);

                var booking = invoice.Booking;
                if (booking != null)
                {
                    var checkInTime = booking.ActualCheckIn ?? booking.CheckInDate.ToDateTime(TimeOnly.MinValue);
                    await _emailService.SendCheckInReadyEmail(invoice, checkInTime);
                }

                TempData["Success"] = "Xác nhận thanh toán thành công";
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
            }
            return RedirectToAction("Detail", new { id });
        }

        [HttpGet]
        public async Task<IActionResult> VietQR(string id)
        {
            var invoice = await _invoiceService.GetByNumber(id);
            if (invoice == null) return NotFound();

            var qrData = _invoiceService.GenerateVietQR(invoice);
            return View(qrData);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmVietQR(string id, string reference)
        {
            try
            {
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
                var invoice = await _invoiceService.ConfirmVietQR(id, reference, userId);

                var booking = invoice.Booking;
                if (booking != null)
                {
                    var checkInTime = booking.ActualCheckIn ?? booking.CheckInDate.ToDateTime(TimeOnly.MinValue);
                    await _emailService.SendCheckInReadyEmail(invoice, checkInTime);
                }

                TempData["Success"] = "Xác nhận thanh toán VietQR thành công";
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
            }
            return RedirectToAction("Detail", new { id });
        }

        [HttpGet]
        public async Task<IActionResult> MoMoPayment(string id)
        {
            var invoice = await _invoiceService.GetByNumber(id);
            if (invoice == null) return NotFound();

            var result = await _moMoService.CreatePayment(invoice);
            if (result.Success && !string.IsNullOrEmpty(result.PayUrl))
            {
                return Redirect(result.PayUrl);
            }

            TempData["Error"] = result.ErrorMessage ?? "Không thể tạo thanh toán MoMo";
            return RedirectToAction("Detail", new { id });
        }

        [HttpGet]
        public async Task<IActionResult> MoMoReturn(string orderId, string requestId, int? resultCode, string? message, string? localMessage, string? extraData)
        {
            var returnData = new MoMoReturnModel
            {
                OrderId = orderId,
                RequestId = requestId,
                ResultCode = resultCode ?? -1,
                Message = message,
                LocalMessage = localMessage,
                ExtraData = extraData
            };

            var result = await _moMoService.ProcessReturn(returnData);

            if (result.Success)
            {
                var invoice = await _invoiceService.GetByNumber(result.InvoiceNumber!);
                if (invoice != null)
                {
                    var userId = User.Identity?.IsAuthenticated == true
                        ? int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!)
                        : 1;
                    await _invoiceService.ConfirmMoMo(result.InvoiceNumber!, "", userId);

                    var booking = invoice.Booking;
                    if (booking != null)
                    {
                        var checkInTime = booking.ActualCheckIn ?? booking.CheckInDate.ToDateTime(TimeOnly.MinValue);
                        await _emailService.SendCheckInReadyEmail(invoice, checkInTime);
                    }
                }

                TempData["Success"] = result.Message;
                return RedirectToAction("Detail", new { id = result.InvoiceNumber });
            }
            else
            {
                TempData["Error"] = result.ErrorMessage;
                return RedirectToAction("Index");
            }
        }

        [HttpPost]
        public async Task<IActionResult> MoMoIpn([FromBody] MoMoIpnModel ipnData)
        {
            var success = await _moMoService.ProcessIpn(ipnData);
            return Content(success ? "{\"status\":\"OK\"}" : "{\"status\":\"FAIL\"}", "application/json");
        }

        [HttpPost("SePayIpn")]
        [AllowAnonymous]
        public async Task<IActionResult> SePayIpn([FromBody] SePayIpnPayload payload)
        {
            var sePayService = HttpContext.RequestServices.GetRequiredService<SePayService>();
            var success = await sePayService.ProcessIpn(payload);
            return Content(success ? "{\"status\":\"OK\"}" : "{\"status\":\"FAIL\"}", "application/json");
        }

        private async Task SendCheckInEmailForInvoice(Invoice invoice)
        {
            var booking = invoice.Booking;
            if (booking != null)
            {
                var checkInTime = booking.ActualCheckIn ?? booking.CheckInDate.ToDateTime(TimeOnly.MinValue);
                await _emailService.SendCheckInReadyEmail(invoice, checkInTime);
            }
        }
    }
}
